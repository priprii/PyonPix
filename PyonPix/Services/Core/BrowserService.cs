using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using PyonPix.Config;
using PyonPix.Events;
using PyonPix.Ipc;
using PyonPix.Services.Game;
using PyonPix.Shared.Ipc;
using PyonPix.Shared.Structs.Browser;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Structs.Audio;
using PyonPix.Structs.Browser;
using PyonPix.Structs.Renderer;
using PyonPix.Utility;
using SharpDX.Direct3D11;

namespace PyonPix.Services.Core;

public class BrowserService(Configuration config, IServiceContext services) : BaseService(config, services) {
    private PixService PixService => Services.Get<PixService>();
    private DXService DXService => Services.Get<DXService>();
    private StateService StateService => Services.Get<StateService>();
    private ExtensionsService ExtensionsService => Services.Get<ExtensionsService>();
    private DataService DataService => Services.Get<DataService>();

    private Process? MediatorProcess;
    private MemoryMappedIpc Ipc = null!;

    public BrowserState State { get; private set; }
    public Vector2 PresentationPosition { get; private set; }
    public Vector2 PresentationSize { get; private set; }
    public bool IsResizing { get; set; }
    public bool IsRescaling { get; set; }
    public bool IsHidden { get; set; } = true;
    public uint CursorId { get; private set; }

    private long HeartbeatTick;
    private const uint HeartbeatTickRate = 1000;
    private long SpatialAudioTick;
    private const uint SpatialAudioTickRate = 100;

    public readonly Dictionary<string, Tab> Tabs = [];
    public Tab? FocusedTab { get; private set; }

    public string PresentationUri = string.Empty;
    public bool CanNavigate => State == BrowserState.Running && (FocusedTab?.CanNavigate ?? false);
    public bool CanGoBack => State == BrowserState.Running && (FocusedTab?.CanGoBack ?? false);
    public bool CanGoForward => State == BrowserState.Running && (FocusedTab?.CanGoForward ?? false);
    public bool CanReload => State == BrowserState.Running && (FocusedTab?.CanReload ?? false);
    public bool CanCancel => State == BrowserState.Running && (FocusedTab?.CanCancel ?? false);

    public event Action<StatusUpdate>? OnStatusUpdate;

    public override Task Initialize() {
        Ipc = new MemoryMappedIpc("PyonPix", true);

        PixService.PixSpawned += OnPixSpawned;
        PixService.PixUpdated += OnPixUpdated;
        PixService.PixDespawned += OnPixDespawned;
        PixService.AllPixDespawned += OnAllPixDespawned;

        ExtensionsService.InstallExtensionRequest += (extensionId, extensionName) => { InstallExtension(extensionId, extensionName); };
        ExtensionsService.UninstallExtensionRequest += (extensionId, extensionName) => { UninstallExtension(extensionId, extensionName); };
        ExtensionsService.EnableExtensionRequest += (extensionId, extensionName) => { EnableExtension(extensionId, extensionName); };
        ExtensionsService.DisableExtensionRequest += (extensionId, extensionName) => { DisableExtension(extensionId, extensionName); };
        return Task.CompletedTask;
    }

    private void OnPixSpawned(IPix? p, bool isUserAction) {
        if(p == null) return;
        if(Tabs.TryGetValue(p.Id, out var _)) return;
        DataService.CancelPendingRemoval(p.Id);

        EnsureTabForPix(p);
        if(FocusedTab == null) {
            SetFocusedTab(p.Id, false);
        }

        var onEnter = Config.Global.Browser.TerritorySpawnBehaviour;
        if(isUserAction || onEnter.HasFlag(SpawnBehaviour.Navigate)) {
            NavigateForPix(p);
        }
        if(onEnter.HasFlag(SpawnBehaviour.Unmute)) {
            // todo: Implement mute/unmute externs
        }
    }
    private void OnPixUpdated(PixUpdate u) {
        if(u.Pix == null || !PixService.IsSpawned(u.Pix)) return;
        if(u.Type is not (PixUpdateType.Uri or PixUpdateType.BrowserProperties or PixUpdateType.All or PixUpdateType.AudioProperties)) return;
        if(!Tabs.TryGetValue(u.Pix.Id, out var _)) return;
        if(u.Type == PixUpdateType.AudioProperties) {
            if(State == BrowserState.Running)
                Ipc.SendUpdateSpatialAudio(u.Pix.Id, 1f, 1f);
        } else {
            DataService.CancelPendingRemoval(u.Pix.Id);
            NavigateForPix(u.Pix);
        }
    }
    private void OnPixDespawned(IPix? p, bool isUserAction) {
        if(p == null) return;
        if(!Tabs.TryGetValue(p.Id, out var t)) return;

        var onExit = Config.Global.Browser.TerritoryDespawnBehaviour;
        if(isUserAction || onExit.HasFlag(DespawnBehaviour.Shutdown)) {
            DestroyTab(t);
        } else if(onExit.HasFlag(DespawnBehaviour.Mute)) {
            // todo: Implement mute/unmute externs
        }
    }
    private void OnAllPixDespawned() { }

    private void InitializeMediator() {
        var procExists = true;

        if(!TryGetMediatorProcess(out MediatorProcess)) {
            MediatorProcess.StartInfo = new ProcessStartInfo() {
                FileName = Path.Combine(Services.PluginInterface.AssemblyLocation.DirectoryName!, "PyonPix.Mediator.exe"),
                Arguments = string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            MediatorProcess.Start();
            procExists = false;
        }
        if(procExists) Ipc.SendCommand(CommandType.MediatorInitializeRequest);
    }

    private bool TryGetMediatorProcess(out Process p) {
        p = new();
        var existingProc = Process.GetProcessesByName("PyonPix.Mediator");
        if(existingProc.Length > 0) {
            p = existingProc[0];
            return true;
        }
        return false;
    }

    public void InitializeBrowser() {
        if(DXService.D3D11Device == null || State != BrowserState.Stopped) return;
        State = BrowserState.Initializing;

        Ipc.OnLog += (e) => {
            switch(e.Type) {
                case LogType.Verbose: Services.Log.Verbose($"[Browser] {e.Message}"); break;
                case LogType.Info: Services.Log.Info($"[Browser] {e.Message}"); break;
                case LogType.Warn: Services.Log.Warning($"[Browser] {e.Message}"); break;
                case LogType.Error:
                    OnStatusUpdate?.Invoke(new(e.Message, StatusType.Error));
                    Services.Log.Error($"[Browser] {e.Message}");
                    break;
            }
        };

        Ipc.OnCommand += (e) => {
            switch(e.Type) {
                case CommandType.MediatorInitializeSuccess:
                    OnStatusUpdate?.Invoke(new("Initializing Browser"));
                    Services.Log.Verbose($"[Mediator] Initializing Browser");
                    Ipc.SendInitializeBrowser(Config.GetConfigPath(), (uint)Environment.ProcessId, DXService.Luid);
                    break;
                case CommandType.BrowserInitializeSuccess:
                    OnStatusUpdate?.Invoke(new("Browser Initialized"));
                    Services.Log.Info("[Browser] Initialized");
                    State = BrowserState.Running;
                    break;
                case CommandType.BrowserInitializeFailed:
                    OnStatusUpdate?.Invoke(new("Browser Initialization Failed", StatusType.Error));
                    Services.Log.Error("[Browser] Initialization Failed");
                    State = BrowserState.Stopped;
                    break;
            }
        };

        Ipc.OnHostInitializeState += (e) => {
            switch(e.Type) {
                case StateType.Success:
                    OnStatusUpdate?.Invoke(new("BrowserHost Initialized"));
                    Services.Log.Info("[BrowserHost] Initialized");
                    State = BrowserState.Running;
                    _ = DataService.RefreshCacheAsync();
                    foreach(var kv in Tabs) {
                        var t = kv.Value;
                        if(t.State == TabState.Uninitialized || t.State == TabState.WaitingForHost) {
                            CreateNativeTab(t);
                        }
                    }
                    break;
                case StateType.Failed:
                    OnStatusUpdate?.Invoke(new("BrowserHost Failed", StatusType.Error));
                    Services.Log.Error($"[BrowserHost] {e.Message}");
                    State = BrowserState.Stopped;
                    // todo: StatusMessage
                    InvokeShutdown();
                    break;
            }
        };

        Ipc.OnTabInitializeState += (e) => {
            if(e.Type == StateType.Success) {
                if(!Tabs.TryGetValue(e.PixId, out var t)) {
                    Services.Log.Warning($"[Browser:{e.PixId}] Unknown Tab Initialized");
                    return;
                }
                Services.Log.Verbose($"[Browser:{e.PixId}] Initialized");

                t.State = TabState.Ready;

                if(!string.IsNullOrEmpty(t.PendingUri) && t.NavState == NavigationState.Pending) {
                    t.NavState = NavigationState.Starting;
                    Ipc.SendNavigate(e.PixId, BrowserUtil.NormalizeUri(t.PendingUri));
                } else {
                    t.NavState = NavigationState.Ready;
                }

                _ = DataService.RefreshCacheAsync();
                if(Config.Global.Browser.CheckUpdateExtensions) {
                    _ = ExtensionsService.CheckUpdateAllAsync(Config.Global.Browser.AutoUpdateExtensions);
                }
            } else if(e.Type == StateType.Failed) {
                OnStatusUpdate?.Invoke(new($"Browser:{e.PixId} Failed", StatusType.Error));
                Services.Log.Error($"[Browser:{e.PixId}] {e.Message}");
                if(!Tabs.TryGetValue(e.PixId, out var t)) return;
                //t.State = TabState.Failed;
                DestroyTab(t);
            } else if(e.Type == StateType.TabDestroyed) {
                var v = PixService.GetVariant(e.PixId);

                Services.Log.Verbose($"[Browser:{e.PixId}] Destroyed");
                if(v == null || !v.PersistentCache) {
                    DataService.RemoveUDF(e.PixId);
                }
            }
        };

        Ipc.OnUpdateFrame += (e) => {
            UpdateFrame(e.PixId, (nint)e.SharedTexture, e.W, e.H);
        };

        Ipc.OnCursorChanged += (e) => CursorId = e.CursorId;

        Ipc.OnNavigationStarting += (e) => {
            if(!e.UserInitiated) return;
            if(!Tabs.TryGetValue(e.PixId, out var t)) return;
            OnStatusUpdate?.Invoke(new($"Navigating to {e.Uri}"));
            t.NavState = NavigationState.Started;
            t.PendingUri = e.Uri;
        };

        Ipc.OnHistoryChanged += (e) => {
            if(!Tabs.TryGetValue(e.PixId, out var t)) return;
            if(t.PendingUri != null && t.PendingUri.StartsWith("data:text/html;")) {
                t.NavState = NavigationState.Ready;
                t.PendingUri = null;
                return; // No history update on error response injection
            }
            var uri = e.Uri;
            if(uri == "about:blank") {
                if(t.PendingUri == null || !t.PendingUri.StartsWith("pix://")) return;
                uri = t.PendingUri;
            } else {
                OnStatusUpdate?.Invoke(new($"Navigating to {e.Uri}", displayTime: 2500));
            }

            t.PendingUri = null;
            t.PresentationUri = uri;
            if(FocusedTab == null || FocusedTab.PixId == t.PixId) {
                PresentationUri = t.PresentationUri;
            }
            if(t.CurrentNavigationItem?.Uri == uri) return; // No history update on page reload

            if(PixService.SpawnedPixs.TryGetValue(e.PixId, out var p)) {
                p.Browser.Uri = uri;
                PixService.UpdateUri(p, true);
            }

            if(t.CurrentNavigationItem != null && Uri.TryCreate(uri, UriKind.Absolute, out var absolute) && !string.IsNullOrWhiteSpace(absolute.Fragment)) {
                if(t.CurrentNavigationItem.Uri.StartsWith(uri.Replace(absolute.Fragment, string.Empty))) {
                    t.CurrentNavigationItem.Uri = uri;
                    return; // No history update if only #fragment changed
                }
            }

            // Navigation while in history, make current uri the most recent
            if(t.CurrentNavigationIndex < t.History.Count - 1)
                t.History.RemoveRange(t.CurrentNavigationIndex + 1, t.History.Count - t.CurrentNavigationIndex - 1);
            var existingIndex = t.History.FindIndex(x => x.Uri == uri);
            if(existingIndex != -1) t.History.RemoveAt(existingIndex);

            t.History.Add(new NavigationItem(uri));
            if(t.History.Count > 10) t.History.RemoveAt(0);

            t.CurrentNavigationIndex = t.History.Count - 1;
            t.NavState = NavigationState.Ready;
        };

        Ipc.OnTitleChanged += (e) => {
            if(!Tabs.TryGetValue(e.PixId, out var t)) return;
            if(t.CurrentNavigationItem == null) return;
            t.CurrentNavigationItem.Title = e.Title;
        };

        Ipc.OnNavigationCompleted += (e) => {
            if(!Tabs.TryGetValue(e.PixId, out var t)) return;
            OnStatusUpdate?.Invoke(new(string.Empty, StatusType.None));
            if(t.NavState == NavigationState.Ready) return;
            t.NavState = NavigationState.Ready;
            t.PresentationUri = t.CurrentNavigationItem?.Uri ?? string.Empty;
        };

        Ipc.OnNavigationCanceled += (e) => {
            if(!Tabs.TryGetValue(e.PixId, out var t)) return;
            OnStatusUpdate?.Invoke(new(string.Empty, StatusType.None));
            t.NavState = NavigationState.Ready;
        };

        Ipc.OnFavIconChanged += (e) => {
            if(!Tabs.TryGetValue(e.PixId, out var t)) return;
            var bytes = e.GetDataArray();
            if(bytes == null || bytes.Length == 0) return;

            _ = Services.Framework.RunOnTick(async () => {
                var newTex = await Services.TextureProvider.CreateFromImageAsync(bytes);
                var old = t.FavIcon;
                t.FavIcon = newTex;
                old?.Dispose();
            });
        };

        Ipc.OnExtensionOperation += (e) => {
            ExtensionsService.IsOperating = false;
            switch(e.ExtensionOp) {
                case ExtensionOp.Install: OnStatusUpdate?.Invoke(new($"Extension Installed: {e.ExtensionId}")); break;
                case ExtensionOp.Remove: OnStatusUpdate?.Invoke(new($"Extension Removed: {e.ExtensionId}")); break;
                case ExtensionOp.Enable: OnStatusUpdate?.Invoke(new($"Extension Enabled: {e.ExtensionId}")); break;
                case ExtensionOp.Disable: OnStatusUpdate?.Invoke(new($"Extension Disabled: {e.ExtensionId}")); break;
            }
        };

        Ipc.Start();
        InitializeMediator();
    }

    private void CreateNativeTab(Tab t) {
        if(t.State == TabState.Creating || t.State == TabState.Ready) return;

        t.State = TabState.Creating;
        var x = (int)t.RenderPos.X;
        var y = (int)t.RenderPos.Y;
        var w = (uint)Math.Max(1, t.RenderSize.X);
        var h = (uint)Math.Max(1, t.RenderSize.Y);

        var ext = ExtensionsService.GetExtensionsToInstall();
        Ipc.SendCreateTab(t.PixId, t.GpuAcceleration, x, y, w, h, t.SyncCookies, ext);
    }

    private Tab EnsureTabForPix(IPix p) {
        if(Tabs.TryGetValue(p.Id, out var existingTab)) return existingTab;

        (var renderPos, var renderSize) = GetRenderBounds(p);
        var v = PixService.GetVariant(p);
        var t = new Tab {
            PixId = p.Id,
            GpuAcceleration = p.Browser.GpuAcceleration,
            SyncCookies = v?.SyncCookies ?? false,
            State = TabState.Uninitialized,
            NavState = NavigationState.Ready,
            RenderPos = renderPos,
            RenderSize = renderSize
        };
        Tabs[p.Id] = t;
        return t;
    }

    public void Update() {
        if(State != BrowserState.Running) return;

        var now = Environment.TickCount64;
        if(now - HeartbeatTick >= HeartbeatTickRate) {
            HeartbeatTick = now;
            Heartbeat();
        }
    }

    public bool Draw(Vector2 imguiPos, Vector2 imguiSize) {
        if(!UpdateLayout(imguiPos, imguiSize)) return false;
        if(imguiSize.X < 1 || imguiSize.Y < 1) return false;

        PresentationPosition = imguiPos;
        PresentationSize = imguiSize;

        if(State != BrowserState.Running || FocusedTab?.SRV == null)
            return false;

        ImGui.Image(new ImTextureID(FocusedTab.SRV.NativePointer), PresentationSize);
        return true;
    }

    public bool UpdateLayout(Vector2 imguiPos, Vector2 imguiSize) {
        if(State != BrowserState.Running) return true;
        if(FocusedTab?.SRV == null) return true;

        // todo: This causes stretch on resize but it prevents crash caused by input racing
        // Need to separate message posting in render loop instead of sleeping on it
        if(IsResizing || IsRescaling) return true;

        foreach(var p in PixService.SpawnedPixs.Values) {
            if(!Tabs.TryGetValue(p.Id, out var t)) continue;
            if(t.State != TabState.Ready) continue;

            (var renderPos, var renderSize) = GetRenderBounds(p, imguiPos, imguiSize);
            if(renderSize.X < 1 || renderSize.Y < 1) continue;

            if(renderSize != t.RenderSize) {
                t.RenderSize = renderSize;
                t.RenderPos = renderPos;
                Ipc.SendResize(p.Id, (int)t.RenderPos.X, (int)t.RenderPos.Y, (uint)t.RenderSize.X, (uint)t.RenderSize.Y);
            } else if(renderPos != t.RenderPos) {
                t.RenderPos = renderPos;
                Ipc.SendReposition(p.Id, (int)t.RenderPos.X, (int)t.RenderPos.Y);
            }
        }

        return true;
    }
    private (Vector2, Vector2) GetRenderBounds(IPix p, Vector2 defPos = default, Vector2 defSize = default) {
        var gameRes = UiUtil.GameResolution;

        if(defSize == default) {
            if(PresentationSize.X <= 1 || PresentationSize.Y <= 1) {
                defPos = Vector2.Zero;
                defSize = gameRes;
            } else {
                defPos = PresentationPosition;
                defSize = PresentationSize;
            }
        }

        var renderPos = Vector2.Zero;
        var renderSize = Vector2.Zero;
        var scaleMode = p.Browser.ScaleMode;
        switch(scaleMode) {
            case BrowserScaleMode.GameWindow:
                renderPos = Vector2.Zero;
                renderSize = gameRes;
                break;
            case BrowserScaleMode.GameWindowWhenHidden:
                renderPos = IsHidden ? Vector2.Zero : defPos;
                renderSize = IsHidden ? gameRes : defSize;
                break;
            case BrowserScaleMode.CustomScale:
                renderPos = Vector2.Zero;
                renderSize = p.Browser.CustomScale;
                break;
            case BrowserScaleMode.CustomScaleWhenHidden:
                renderPos = IsHidden ? Vector2.Zero : defPos;
                renderSize = IsHidden ? p.Browser.CustomScale : defSize;
                break;
            case BrowserScaleMode.BrowserWindow:
                renderPos = defPos;
                renderSize = defSize;
                break;
        }
        return (renderPos, renderSize);
    }
    public Vector2 TranslatePositionRelative(IPix p, Vector2 mousePos) {
        switch(p.Browser.ScaleMode) {
            case BrowserScaleMode.GameWindow:
                mousePos *= new Vector2(UiUtil.GameWidth / PresentationSize.X, UiUtil.GameHeight / PresentationSize.Y);
                return mousePos;
            case BrowserScaleMode.CustomScale:
                mousePos *= new Vector2(p.Browser.CustomScale.X / PresentationSize.X, p.Browser.CustomScale.Y / PresentationSize.Y);
                return mousePos;
            default:
                return mousePos;
        }
    }

    private void UpdateFrame(string tabId, nint sharedHandle, uint width, uint height) {
        if(DXService.D3D11Device == null || sharedHandle == nint.Zero) return;
        if(DXService.D3D11Device.DeviceRemovedReason != SharpDX.Result.Ok) return;
        if(!Tabs.TryGetValue(tabId, out var t)) return;

        if(t.SharedHandle == sharedHandle) return;

        t.SRV?.Dispose();
        t.SRV = null;

        try {
            using var sharedTex = DXService.D3D11Device.OpenSharedResource<Texture2D>(sharedHandle);
            if(sharedTex == null) return;

            var srvDesc = new ShaderResourceViewDescription {
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                Texture2D = new ShaderResourceViewDescription.Texture2DResource { MostDetailedMip = 0, MipLevels = 1 }
            };
            var newSrv = new ShaderResourceView(DXService.D3D11Device, sharedTex, srvDesc);

            t.SharedHandle = sharedHandle;
            t.SRV = newSrv;
            if(tabId == FocusedTab?.PixId) {
                SwapFocusedSRV(newSrv, sharedHandle);
            }
        } catch(Exception ex) {
            Services.Log.Error(ex, "UpdateFrame Failed");
        }
    }

    public void UpdateSpatialAudio(Dictionary<string, Renderer>.ValueCollection renderers) {
        if(State != BrowserState.Running || PixService.SpawnedPixs.Count == 0) return;

        var now = Environment.TickCount64;
        if(now - SpatialAudioTick >= SpatialAudioTickRate) {
            SpatialAudioTick = now;
            var globalAudioProps = Config.Global.Audio;

            Vector3 listenerPos;
            Vector3 listenerRight;
            var cameraRelative = globalAudioProps.ListenerType == AudioListenerType.Camera || Services.Objects.LocalPlayer == null;
            if(cameraRelative) {
                Matrix4x4.Invert(CameraService.GetViewMatrix(), out var camWorld);
                listenerPos = camWorld.Translation;
                listenerRight = Vector3.Normalize(new Vector3(camWorld.M11, camWorld.M12, camWorld.M13));
            } else {
                listenerPos = StateService.LocalPlayerPosition;
                listenerRight = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, StateService.LocalPlayerRotation));
                //listenerRight.Y = 0f;
            }

            foreach(var r in renderers) {
                if(r.ScreenTransform == null) continue;
                if(!PixService.SpawnedPixs.TryGetValue(r.PixId, out var p)) continue;
                var audioProps = p.Audio;
                if(!audioProps.SpatialEnabled) continue;
                var screenPos = r.ScreenTransform.Value.Translation;

                Vector3 toSource = screenPos - listenerPos;
                float distance = toSource.Length();
                //if(distance > 0.0001f) toSource /= distance;

                float maxDistance = MathF.Max(0.01f, audioProps.FalloffMaxDistance);
                float t = MathF.Min(distance / maxDistance, 1.0f);

                // smoothstep falloff
                float attenuation = 1.0f - (t * t * (3 - (2 * t)));

                float finalVolume = audioProps.Volume * attenuation * globalAudioProps.MasterVolume;

                // horizontal only
                toSource.Y = 0f;
                listenerRight.Y = 0f;

                toSource = Vector3.Normalize(toSource);
                listenerRight = Vector3.Normalize(listenerRight);

                float pan = cameraRelative ? Vector3.Dot(toSource, listenerRight) : -Vector3.Dot(toSource, listenerRight);
                pan = MathF.Max(-1f, MathF.Min(1f, pan));

                // equal-power pan
                float left = finalVolume * MathF.Sqrt(0.5f * (1.0f - pan));
                float right = finalVolume * MathF.Sqrt(0.5f * (1.0f + pan));

                /* todo: reimplement
                var normalized = distance / maxDistance;
                float inv = 1.0f / (1.0f + audioProps.FalloffStrength * normalized);
                float invAtMax = 1.0f / (1.0f + audioProps.FalloffStrength);
                var attenuation = (inv - invAtMax) / (1.0f - invAtMax);
                attenuation = MathF.Max(0.0f, MathF.Min(1.0f, attenuation));

                float angle = (pan + 1.0f) * 0.25f * 3.14159265f;
                float left = finalVolume * MathF.Cos(angle);
                float right = finalVolume * MathF.Sin(angle);
                */

                Ipc.SendUpdateSpatialAudio(r.PixId, left, right);
            }
        }
    }

    public void NavigateForPix(IPix pix) {
        if(pix == null) return;

        var props = Config.Global.Browser;
        var t = EnsureTabForPix(pix);
        t.PendingUri = string.IsNullOrWhiteSpace(pix.Browser.Uri) ? t.GetHomeUri(props.HomeUriType, props.HomeUri) : pix.Browser.Uri;
        t.NavState = NavigationState.Pending;

        if(State != BrowserState.Running) {
            t.State = TabState.WaitingForHost;
            if(State == BrowserState.Stopped) InitializeBrowser();
            return;
        }

        if(t.State == TabState.Uninitialized || t.State == TabState.WaitingForHost) {
            CreateNativeTab(t);
            return;
        }

        if(t.State == TabState.Ready) {
            t.NavState = NavigationState.Starting;
            Ipc.SendNavigate(t.PixId, BrowserUtil.NormalizeUri(t.PendingUri));
        }
    }

    private void SwapFocusedSRV(ShaderResourceView? newSrv, nint sharedHandle) {
        if(FocusedTab?.SharedHandle == sharedHandle) return;
        var old = FocusedTab?.SRV;
        FocusedTab?.SRV = newSrv;
        FocusedTab?.SharedHandle = sharedHandle;
        old?.Dispose();
    }

    private void ReleaseFocusedSRV() {
        FocusedTab?.SRV?.Dispose();
        FocusedTab?.SRV = null;
        FocusedTab?.SharedHandle = nint.Zero;
    }

    public void SetFocusedTab(string? pixId, bool byUserInput) {
        if(string.IsNullOrEmpty(pixId)) {
            ReleaseFocusedSRV();
            FocusedTab = null;
            PresentationUri = string.Empty;
            return;
        }
        
        PixService.SpawnedPixs.TryGetValue(pixId, out var p);
        if(!Tabs.TryGetValue(pixId, out var t)) {
            if(p == null) return;
            t = EnsureTabForPix(p);
        }

        PresentationUri = t.PresentationUri ?? p?.Browser.Uri ?? string.Empty;
        FocusedTab = t;
        Ipc.SendSetFocusedTab(pixId, byUserInput);

        if(t.SRV != null) {
            SwapFocusedSRV(t.SRV, t.SharedHandle);
        } else {
            ReleaseFocusedSRV();
        }
    }

    public void LostFocus() {
        if(State != BrowserState.Running) return;
        Ipc.SendCommand(CommandType.BrowserLostFocus);
    }

    public void Navigate(string uri) {
        if(!CanNavigate) return;
        if(!PixService.SpawnedPixs.TryGetValue(FocusedTab!.PixId, out var p)) return;
        p.Browser.Uri = uri;
        FocusedTab.NavState = NavigationState.Starting;
        PixService.UpdateUri(p, false);
    }
    public void NavHome() {
        if(!CanNavigate) return;
        var props = Config.Global.Browser;
        Navigate(FocusedTab!.GetHomeUri(props.HomeUriType, props.HomeUri));
    }
    public void NavBack() {
        if(!CanGoBack) return;
        if(!PixService.SpawnedPixs.TryGetValue(FocusedTab!.PixId, out var p)) return;
        FocusedTab.CurrentNavigationIndex--;
        p.Browser.Uri = FocusedTab.CurrentNavigationItem!.Uri;
        FocusedTab.NavState = NavigationState.Starting;
        PixService.UpdateUri(p, false);
    }
    public void NavHistory(int index) {
        if(!CanGoBack && !CanGoForward) return;
        if(!PixService.SpawnedPixs.TryGetValue(FocusedTab!.PixId, out var p)) return;
        FocusedTab.CurrentNavigationIndex = index;
        p.Browser.Uri = FocusedTab.CurrentNavigationItem!.Uri;
        FocusedTab.NavState = NavigationState.Starting;
        PixService.UpdateUri(p, false);
    }
    public void NavForward() {
        if(!CanGoForward) return;
        if(!PixService.SpawnedPixs.TryGetValue(FocusedTab!.PixId, out var p)) return;
        FocusedTab.CurrentNavigationIndex++;
        p.Browser.Uri = FocusedTab.CurrentNavigationItem!.Uri;
        FocusedTab.NavState = NavigationState.Starting;
        PixService.UpdateUri(p, false);
    }

    public void NavReload() {
        if(!CanReload) return;
        if(!PixService.SpawnedPixs.TryGetValue(FocusedTab!.PixId, out var _)) return;
        FocusedTab.NavState = NavigationState.Starting;
        Ipc.SendReload(FocusedTab.PixId);
    }

    public void NavCancel() {
        if(!CanCancel) return;
        FocusedTab!.NavState = NavigationState.Ready;
        Ipc.SendStopNavigation(FocusedTab.PixId);
    }

    public void SendMouseEvent(string pixId, uint msg, nint wParam, nint lParam) {
        if(State != BrowserState.Running) return;
        if(!Tabs.TryGetValue(pixId, out var t)) return;
        if(t.State != TabState.Ready) return;
        Ipc.SendSendMouseEvent(pixId, msg, wParam, lParam);
    }

    public void OpenDevTools() {
        if(State != BrowserState.Running) return;
        if(FocusedTab == null) return;
        if(FocusedTab.State != TabState.Ready) return;
        Ipc.SendOpenDevTools(FocusedTab.PixId);
    }

    public void InstallExtension(string extensionId, string extensionName) {
        if(State != BrowserState.Running || FocusedTab == null) {
            ExtensionsService.IsOperating = false;
            return;
        }
        Ipc.SendInstallExtension(extensionId, extensionName);
    }
    public void UninstallExtension(string extensionId, string extensionName) {
        if(State != BrowserState.Running || FocusedTab == null) {
            ExtensionsService.IsOperating = false;
            return;
        }
        Ipc.SendUninstallExtension(extensionId, extensionName);
    }
    public void EnableExtension(string extensionId, string extensionName) {
        if(State != BrowserState.Running || FocusedTab == null) {
            ExtensionsService.IsOperating = false;
            return;
        }
        Ipc.SendEnableExtension(extensionId, extensionName);
    }
    public void DisableExtension(string extensionId, string extensionName) {
        if(State != BrowserState.Running || FocusedTab == null) {
            ExtensionsService.IsOperating = false;
            return;
        }
        Ipc.SendDisableExtension(extensionId, extensionName);
    }

    private void Heartbeat() {
        if(State != BrowserState.Running) return;
        Ipc.SendCommand(CommandType.BrowserHeartbeat);
    }

    public void Shutdown() {
        if(State == BrowserState.Stopped || State == BrowserState.Stopping) return;
        State = BrowserState.Stopping;
    }

    public void InvokeShutdown() { // todo: remove this
        State = BrowserState.Stopped;

        DestroyAllTabs();
        Ipc.SendCommand(CommandType.BrowserShutdown);
    }

    private void DestroyTab(Tab? t) {
        if(State != BrowserState.Running || t == null) return;
        var pixId = t.PixId;

        if(FocusedTab?.PixId == pixId)
            SetFocusedTab(null, false);

        Ipc.SendDestroyTab(t.PixId);
        t.Dispose();

        Tabs.Remove(pixId);
    }

    private void DestroyAllTabs() {
        var tabs = Tabs.Values.ToList();
        foreach(var t in tabs) {
            DestroyTab(t);
        }
        Tabs.Clear();
    }

    public override Task Dispose() {
        PixService.PixSpawned -= OnPixSpawned;
        PixService.PixUpdated -= OnPixUpdated;
        PixService.PixDespawned -= OnPixDespawned;
        PixService.AllPixDespawned -= OnAllPixDespawned;
        InvokeShutdown();

        Ipc?.Dispose();
        // todo: mediator persist across plugin reload?
        MediatorProcess?.Kill();
        MediatorProcess?.Dispose();
        return Task.CompletedTask;
    }
}
