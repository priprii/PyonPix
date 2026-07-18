using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Interop;
using PyonPix.Services.Game;
using PyonPix.Shared.Structs.Browser;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Structs.Browser;
using PyonPix.Structs.Renderer;
using PyonPix.Utility;
using static PyonPix.Interop.Win32Interop;

namespace PyonPix.Services.Core;

public class PixInputService(Configuration config, IServiceContext services) : BaseService(config, services) {
    private BrowserService BrowserService => Services.Get<BrowserService>();
    private RendererService RendererService => Services.Get<RendererService>();
    private PixService PixService => Services.Get<PixService>();

    private WindowSubclass? WindowSubclass = null!;

    private bool IsMouseInImGuiPresentationRegion = false;
    private bool IsImGuiPresentationRegionFocused = false;
    private bool IsRendererRegionFocused = false;

    private bool WasGameFocused;

    private unsafe Cursor* FFXIVCursor = Cursor.Instance();
    private bool HWCursorInitialState;
    private bool SWCursorInitialState;
    private uint PreviousCursorId;

    private IPix? RendererHoveredPix;
    private IPix? RendererMouseCapturePix;
    private Vector2 RendererMousePos;

    public override Task Initialize() {
        WindowSubclass = new WindowSubclass(Services.PluginInterface.UiBuilder.WindowHandlePtr, WndProcDetour);

        unsafe {
            HWCursorInitialState = FFXIVCursor->UseOsHardwareCursor;
            SWCursorInitialState = FFXIVCursor->UseSoftwareCursor;
        }

        return Task.CompletedTask;
    }

    public Vector2 TranslatePositionRelativeToImGuiPresentation(IPix p, Vector2 mousePos) {
        var presentationSize = BrowserService.PresentationSize;

        switch(p.Browser.ScaleMode) {
            case BrowserScaleMode.GameWindow:
                mousePos *= new Vector2(UiUtil.GameWidth / presentationSize.X, UiUtil.GameHeight / presentationSize.Y);
                return mousePos;
            case BrowserScaleMode.CustomScale:
                mousePos *= new Vector2(p.Browser.CustomScale.X / presentationSize.X, p.Browser.CustomScale.Y / presentationSize.Y);
                return mousePos;
            default:
                return mousePos;
        }
    }

    public void HandleImGuiPresentationMouseInput() {
        if(BrowserService.FocusedTab == null) return;
        if(!PixService.SpawnedPixs.TryGetValue(BrowserService.FocusedTab.PixId, out var pix)) return;

        var io = ImGui.GetIO();
        var bClicked = BrowserUtil.GetMouseButtonsState(io.MouseClicked);
        var bReleased = BrowserUtil.GetMouseButtonsState(io.MouseReleased);
        var bDblClicked = BrowserUtil.GetMouseButtonsState(io.MouseDoubleClicked);
        var anyClicked = bClicked != MouseButton.None || bReleased != MouseButton.None || bDblClicked != MouseButton.None;
        var anyScroll = io.MouseWheelH != 0 || io.MouseWheel != 0;
        var localMouse = TranslatePositionRelativeToImGuiPresentation(pix, io.MousePos - BrowserService.PresentationPosition);
        var mousePos = localMouse.ToLParam();

        ImGui.SetCursorScreenPos(BrowserService.PresentationPosition);
        ImGui.InvisibleButton("##browserInputHitTest", BrowserService.PresentationSize);

        if(bClicked.HasFlag(MouseButton.Left) || bClicked.HasFlag(MouseButton.Right) || bClicked.HasFlag(MouseButton.Middle)) {
            if(IsMouseInImGuiPresentationRegion && !IsImGuiPresentationRegionFocused) {
                IsImGuiPresentationRegionFocused = true; // Gain focus while game window active
            }
        }

        if(!WasGameFocused && Win32Interop.IsGameFocused) {
            if(ImGui.IsItemHovered()) {
                IsImGuiPresentationRegionFocused = true; // Gain focus after game window active
            }
        }

        if(!ImGui.IsWindowFocused() || !Win32Interop.IsGameFocused) {
            if(IsImGuiPresentationRegionFocused) {
                IsImGuiPresentationRegionFocused = false; // Lose focus due to either imgui loss or game loss
                BrowserService.LostFocus();
            }
        }

        WasGameFocused = Win32Interop.IsGameFocused;

        if(!ImGui.IsItemHovered()) {
            if(IsMouseInImGuiPresentationRegion) {
                IsMouseInImGuiPresentationRegion = false;
                BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.MOUSELEAVE, 0, mousePos);
            }
            return;
        }

        if(!Win32Interop.IsGameFocused) {
            IsMouseInImGuiPresentationRegion = false;
            return;
        }
        IsMouseInImGuiPresentationRegion = true;

        ImGui.SetMouseCursor(BrowserUtil.TranslateCursor(BrowserService.CursorId));

        if(io.MouseDelta == Vector2.Zero && !anyClicked && !anyScroll) return;
        if(!anyClicked && !anyScroll) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.MOUSEMOVE, 0, mousePos);

        if(bClicked.HasFlag(MouseButton.Left)) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.LBUTTONDOWN, 0x0001, mousePos);
        if(bReleased.HasFlag(MouseButton.Left)) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.LBUTTONUP, 0, mousePos);

        if(bClicked.HasFlag(MouseButton.Right)) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.RBUTTONDOWN, 0x0001, mousePos);
        if(bReleased.HasFlag(MouseButton.Right)) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.RBUTTONUP, 0, mousePos);

        if(bClicked.HasFlag(MouseButton.Middle)) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.MBUTTONDOWN, 0x0001, mousePos);
        if(bReleased.HasFlag(MouseButton.Middle)) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.MBUTTONUP, 0, mousePos);

        if(io.MouseWheel != 0f) BrowserService.SendMouseEvent(pix.Id, (uint)Win32Interop.WM.MOUSEWHEEL, (int)(io.MouseWheel * 120) << 16, mousePos);
    }

    public void ClearImGuiPresentationFocus() {
        IsMouseInImGuiPresentationRegion = false;
        IsImGuiPresentationRegionFocused = false;
    }

    public Vector2 TranslatePositionRelativeToRenderer(IPix pix, Vector2 uv) {
        if(!BrowserService.TryGetRenderBounds(pix, out _, out var renderSize))
            return Vector2.Zero;
        return uv * renderSize;
    }

    public unsafe void HandleRendererMouseInput(Renderer? renderer, Tab? tab) {
        if(renderer == null || tab == null) return;
        if(!PixService.SpawnedPixs.TryGetValue(tab.PixId, out var pix)) return;
        if(renderer.ScreenTransform == null) return;
        if(!Matrix4x4.Invert(renderer.ScreenTransform.Value, out var invWorld)) return;
        var camera = CameraService.GetSceneCamera();
        if(camera == null) return;

        var io = ImGui.GetIO();
        var ray = camera->ScreenPointToRay(new((int)io.MousePos.X, (int)io.MousePos.Y));

        var localOrigin = Vector3.Transform(ray.Origin, invWorld);
        var localDirection = Vector3.Normalize(Vector3.TransformNormal(ray.Direction, invWorld));
        if(MathF.Abs(localDirection.Z) < 0.0001f) return;
        var t = -localOrigin.Z / localDirection.Z;
        if(t < 0) return;

        var hit = localOrigin + localDirection * t;

        var isHovered = hit.X >= -0.5f && hit.X <= 0.5f && hit.Y >= -0.5f && hit.Y <= 0.5f;

        var u = Math.Clamp(hit.X + 0.5f, 0f, 1f);
        var v = Math.Clamp(0.5f - hit.Y, 0f, 1f);

        var localMouse = TranslatePositionRelativeToRenderer(pix, new Vector2(u, v));
        var mousePos = localMouse.ToLParam();
        
        if(!isHovered || ImGui.GetIO().WantCaptureMouse) {
            if(RendererHoveredPix == pix) {
                RendererHoveredPix = null;
                BrowserService.SendMouseEvent( pix.Id, (uint)Win32Interop.WM.MOUSELEAVE, 0, mousePos);

                ResetCursor();
            }
            return;
        }

        if(!Win32Interop.IsGameFocused) {
            if(RendererHoveredPix == pix) {
                RendererHoveredPix = null;
                if(IsRendererRegionFocused) {
                    IsRendererRegionFocused = false;
                    BrowserService.LostFocus();
                }
                ResetCursor(true);
            }
            return;
        }
        RendererHoveredPix = pix;
        RendererMousePos = localMouse;

        ChangeCursor(BrowserService.CursorId);
    }

    public void Update() {
        if(PreviousCursorId == 0) return;
        Win32Interop.SetOSCursor(PreviousCursorId);
    }

    private unsafe void ChangeCursor(uint cursorId) {
        FFXIVCursor->UseOsHardwareCursor = true;
        FFXIVCursor->UseSoftwareCursor = false;
        PreviousCursorId = cursorId;
    }

    private unsafe void ResetCursor(bool force = false) {
        if(PreviousCursorId == 0 && !force) return;
        FFXIVCursor->UseOsHardwareCursor = HWCursorInitialState;
        FFXIVCursor->UseSoftwareCursor = SWCursorInitialState;
        PreviousCursorId = 0;
    }

    private bool HandleRendererMouseEvent(WM msg, ulong wParam, long lParam) {
        if(!Win32Interop.IsGameFocused) return false;

        switch(msg) {
            case WM.LBUTTONDOWN:
            case WM.RBUTTONDOWN:
            case WM.MBUTTONDOWN:
                if(RendererHoveredPix == null) {
                    if(IsRendererRegionFocused) {
                        IsRendererRegionFocused = false;
                        BrowserService.LostFocus();
                        ResetCursor(true);
                    }
                    return false;
                }

                RendererMouseCapturePix = RendererHoveredPix;
                IsRendererRegionFocused = true;
                BrowserService.FocusTab(RendererMouseCapturePix.Id);
                BrowserService.SendMouseEvent(RendererMouseCapturePix, (uint)msg, (nint)wParam, RendererMousePos.ToLParam());
                return true;
            case WM.LBUTTONUP:
            case WM.RBUTTONUP:
            case WM.MBUTTONUP:
                if(RendererMouseCapturePix == null) return false;

                BrowserService.SendMouseEvent(RendererMouseCapturePix, (uint)msg, (nint)wParam, RendererMousePos.ToLParam());
                RendererMouseCapturePix = null;
                return true;
            case WM.MOUSEWHEEL:
                if(RendererHoveredPix == null) return false;

                BrowserService.SendMouseEvent(RendererHoveredPix, (uint)msg, (nint)wParam, (nint)lParam);
                return true;
            case WM.MOUSEMOVE:
                if(RendererHoveredPix == null) return false;

                BrowserService.SendMouseEvent(RendererHoveredPix, (uint)WM.MOUSEMOVE, (nint)wParam, RendererMousePos.ToLParam());
                return true;
        }

        return false;
    }

    private long WndProcDetour(nint hWnd, uint msg, ulong wParam, long lParam) {
        if(BrowserService.State == BrowserState.Running) {
            switch((WM)msg) {
                case WM.ENTERSIZEMOVE:
                    BrowserService.IsResizing = true;
                    break;
                case WM.EXITSIZEMOVE:
                    BrowserService.IsResizing = false;
                    break;

                case WM.LBUTTONDOWN:
                case WM.RBUTTONDOWN:
                case WM.MBUTTONDOWN:
                    if(HandleRendererMouseEvent((WM)msg, wParam, lParam)) return 0;
                    break;
                case WM.LBUTTONUP:
                case WM.RBUTTONUP:
                case WM.MBUTTONUP:
                    if(HandleRendererMouseEvent((WM)msg, wParam, lParam)) return 0;
                    break;
                case WM.MOUSEWHEEL:
                    if(HandleRendererMouseEvent((WM)msg, wParam, lParam)) return 0;
                    break;
                case WM.MOUSEMOVE:
                    HandleRendererMouseEvent((WM)msg, wParam, lParam);
                    break;
            }
        }
        return WindowSubclass!.CallOriginal(hWnd, msg, wParam, lParam);
    }

    public override Task Dispose() {
        ResetCursor();
        WindowSubclass?.Dispose();
        WindowSubclass = null;

        return Task.CompletedTask;
    }
}
