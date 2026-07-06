using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Ui.Components;
using PyonPix.Utility;

namespace PyonPix.Ui;

public abstract class BaseWindow : Window, IDisposable {
    protected readonly Configuration Config;
    protected readonly IServiceContext Services;
    protected readonly IWindowContext Windows;

    protected virtual bool ShowTitleBar => true;
    protected virtual bool ShowTitleBarCollapseButton => true;
    protected virtual bool ShowTitleBarTitleText => true;
    protected virtual bool ShowTitleBarSettingsButton => true;
    protected virtual bool ShowTitleBarCloseButton => true;
    protected virtual bool NoResize => false;

    protected virtual float TitleBarHeight => 24f;
    protected virtual float TitleBarXPadding => 2f;
    protected virtual float BorderThickness => 1.5f;
    protected virtual float BorderInset => 0.5f;
    public float BorderSize => BorderThickness + BorderInset;

    protected virtual Vector2 WindowPadding => new Vector2(4f, 0) * ImGuiHelpers.GlobalScale;
    protected virtual float LineHeight => UIShared.LineHeight;
    protected virtual float ItemSpacing => 4f * ImGuiHelpers.GlobalScale;
    protected virtual float IndentWidth => 12f * ImGuiHelpers.GlobalScale;

    protected virtual ImGuiWindowFlags BaseFlags => ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking;

    protected abstract WindowState State { get; }
    public bool IsCollapsed => State == WindowState.Collapsed && ShowTitleBar && ShowTitleBarCollapseButton;
    public bool IsHidden => !IsOpen || IsCollapsed;
    protected virtual Vector2 ExpandedSize => ExpandedMinSize;
    protected abstract Vector2 ExpandedMinSize { get; }
    protected abstract Vector2 ExpandedMaxSize { get; }
    public float CollapsedHeight => TitleBarHeight + (BorderSize * 2);

    public Vector2 BoundsMin => ImGui.GetWindowPos() + new Vector2(BorderSize * ImGuiHelpers.GlobalScale);
    public Vector2 BoundsMax => ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(BorderSize * ImGuiHelpers.GlobalScale);
    public Vector2 HeaderMin => BoundsMin;
    public Vector2 HeaderMax => new Vector2(BoundsMax.X, BoundsMin.Y + (TitleBarHeight * ImGuiHelpers.GlobalScale));
    public Vector2 ContentMin => ShowTitleBar ? new Vector2(BoundsMin.X, HeaderMax.Y + (BorderSize * ImGuiHelpers.GlobalScale)) : BoundsMin;
    public Vector2 ContentMax => BoundsMax;
    public Vector2 ContentSize => ContentMax - ContentMin;

    public float TitleBarFrameHeight => HeaderMax.Y - HeaderMin.Y;

    private WindowState _lastState;
    private bool _initialized;
    protected StatusBar StatusBar = new();

    protected enum WindowState {
        Expanded,
        Collapsed
    }

    public BaseWindow(string name, Configuration config, IServiceContext services, IWindowContext windows, ImGuiWindowFlags flags = ImGuiWindowFlags.None) : base(name) {
        Config = config;
        Services = services;
        Windows = windows;
        Flags = flags == ImGuiWindowFlags.None ? BaseFlags : BaseFlags | flags;
    }

    public override void PreDraw() {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 4f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Vector4.Zero);
        base.PreDraw();
    }

    public override void PostDraw() {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(6);
        base.PostDraw();
    }

    public override void Draw() {
        if(!IsOpen) return;

        var winPos = ImGui.GetWindowPos();
        if(winPos.Y < 0) {
            ImGui.SetWindowPos(new Vector2(winPos.X, 0));
        }

        UpdateWindowState();
        SizeConstraints = GetConstraints();

        DrawWindowBackground();

        if(ShowTitleBar) {
            DrawTitleBarBackground();
            var leftCursor = DrawTitleBarCollapse();
            var rightCursor = DrawTitleBarControls();
            rightCursor = DrawControlExtras(rightCursor);
            if(ShowTitleBarTitleText)
                DrawTitleBarText(leftCursor, rightCursor);
        }

        ImGui.SetCursorScreenPos(ContentMin);

        var statusBarVisible = !IsHidden && (StatusBar?.IsVisible ?? false);
        var contentSize = ContentSize;
        if(statusBarVisible && !StatusBar!.IsOverlay)
            contentSize -= new Vector2(0, StatusBar.Height);

        ImGui.BeginChild($"##rootContent", contentSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawContent();

        ImGui.EndChild();

        if(statusBarVisible)
            StatusBar!.Draw(BoundsMin, BoundsMax);
    }

    protected virtual void DrawWindowBackground() {
        var borderThickness = BorderThickness * ImGuiHelpers.GlobalScale;
        var borderInset = (BorderThickness * BorderInset) * ImGuiHelpers.GlobalScale;

        var draw = ImGui.GetWindowDrawList();

        Vector2 pos = ImGui.GetWindowPos();
        Vector2 size = ImGui.GetWindowSize();

        Vector2 borderPos = pos + new Vector2(borderInset, borderInset);
        Vector2 borderSize = pos + size - new Vector2(borderInset, borderInset);

        draw.AddImageRounded(UIShared.GradientTexture!.Handle, pos, pos + size, new Vector2(0, 0), new Vector2(1, 1), ImGui.GetColorU32(UIShared.WindowBgTint), UIShared.WindowRounding);
        draw.AddRect(borderPos, borderSize, ImGui.GetColorU32(UIShared.WindowBorder), UIShared.WindowRounding - (BorderThickness * BorderInset), ImDrawFlags.None, borderThickness);
    }

    protected virtual void DrawTitleBarBackground() {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(HeaderMin, HeaderMax, ImGui.GetColorU32(UIShared.TitleBarBg), UIShared.WindowRounding * 0.5f, IsCollapsed ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersTop);
    }
    protected virtual float DrawTitleBarCollapse() {
        float leftCursor = HeaderMin.X + (TitleBarXPadding * ImGuiHelpers.GlobalScale);
        if(ShowTitleBarCollapseButton) {
            Vector2 collapseBtnPos = new Vector2(leftCursor, HeaderMin.Y);
            ImGui.SetCursorScreenPos(collapseBtnPos);
            if(ImGuiEx.IconButton(IsCollapsed ? FontAwesomeIcon.CaretRight : FontAwesomeIcon.CaretDown, "##collapse", size: TitleBarFrameHeight))
                SetState(IsCollapsed ? WindowState.Expanded : WindowState.Collapsed);
            leftCursor += TitleBarFrameHeight; //  + (2 * ImGuiHelpers.GlobalScale);
        }

        return leftCursor;
    }
    protected virtual float DrawTitleBarControls() {
        float rightCursor = HeaderMax.X - (TitleBarXPadding * ImGuiHelpers.GlobalScale);
        if(ShowTitleBarCloseButton) {
            rightCursor -= TitleBarFrameHeight;
            Vector2 closeBtnPos = new Vector2(rightCursor, HeaderMin.Y);
            ImGui.SetCursorScreenPos(closeBtnPos);

            if(ImGuiEx.IconButton(FontAwesomeIcon.Times, "##close", size: TitleBarFrameHeight, iconScale: 0.8f))
                OnCloseClicked();
        }

        if(ShowTitleBarSettingsButton) {
            rightCursor -= TitleBarFrameHeight; // + (2 * ImGuiHelpers.GlobalScale)
            Vector2 configBtnPos = new Vector2(rightCursor, HeaderMin.Y);
            ImGui.SetCursorScreenPos(configBtnPos);

            if(ImGuiEx.IconButton(FontAwesomeIcon.Cog, "##settings", size: TitleBarFrameHeight, iconScale: 0.8f))
                OnConfigClicked();
        }

        return rightCursor;
    }
    protected virtual void DrawTitleBarText(float leftCursor, float rightCursor) {
        using(UIShared.NormalFont.Push()) {
            ImGui.PushClipRect(new Vector2(leftCursor, HeaderMin.Y), new Vector2(rightCursor, HeaderMax.Y), true);
            float fontSize = 16f;
            Vector2 textSize = UiUtil.CalcTextSize(WindowName, fontSize);
            float textY = HeaderMin.Y + (TitleBarFrameHeight - textSize.Y) * 0.5f;
            Vector2 textPos = new Vector2(leftCursor, textY);
            ImGui.SetCursorScreenPos(textPos);
            
            ImGuiEx.StyledText(WindowName, colorA: UIShared.WindowTitle.AsVector3(), wrapWidth: textSize.X);
            ImGui.PopClipRect();
        }
    }

    protected virtual float DrawControlExtras(float rightCursor) { return rightCursor; }

    protected virtual void DrawContent() { }

    protected void UpdateWindowState() {
        Flags = BaseFlags;

        if(!_initialized) {
            _lastState = State;
            _initialized = true;
            return;
        }

        if(IsCollapsed || NoResize) {
            Flags |= ImGuiWindowFlags.NoResize;
        }

        if(_lastState != State) {
            if(IsCollapsed) {
                var expandedSize = ImGui.GetWindowSize();
                ImGui.SetWindowSize(new Vector2(expandedSize.X, CollapsedHeight));
                OnCollapsed(expandedSize);
            } else {
                ImGui.SetWindowSize(ExpandedSize);
            }
            _lastState = State;
        }
    }

    protected virtual void OnCollapsed(Vector2 windowSize) { }
    protected virtual void SetState(WindowState newState) { }
    protected virtual void OnConfigClicked() { }
    protected virtual void OnCloseClicked() { }

    protected WindowSizeConstraints GetConstraints() {
        if(IsCollapsed) {
            return new WindowSizeConstraints {
                MinimumSize = new Vector2(ExpandedMinSize.X, CollapsedHeight),
                MaximumSize = new Vector2(ExpandedMaxSize.X, CollapsedHeight)
            };
        }

        return new WindowSizeConstraints {
            MinimumSize = ExpandedMinSize,
            MaximumSize = new(ExpandedMaxSize.X < ImGui.GetMainViewport().Size.X ?
                ExpandedMaxSize.X : ImGui.GetMainViewport().Size.X,
                ExpandedMaxSize.Y < ImGui.GetMainViewport().Size.Y ?
                ExpandedMaxSize.Y : ImGui.GetMainViewport().Size.Y)
        };
    }

    public virtual void Dispose() { }
}
