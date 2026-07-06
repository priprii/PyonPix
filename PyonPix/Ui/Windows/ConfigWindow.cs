using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Game;
using PyonPix.Structs.Browser;
using PyonPix.Structs.Ui;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class ConfigWindow : BaseWindow {
    private RendererService RendererService => Services.Get<RendererService>();

    protected override WindowState State => Config.UI.Config.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.Config.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(350, 150);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;
    protected override bool ShowTitleBarSettingsButton => false;

    public override void OnOpen() {
        base.OnOpen();
        Config.UI.Config.IsOpen = true;
        Config.Save();
    }
    public override void OnClose() {
        base.OnClose();
        Config.UI.Config.IsOpen = false;
        Config.Save();
    }

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.Config.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.Config.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnCloseClicked() => IsOpen = false;

    private readonly List<UiTab> Tabs = null!;
    private UiTab ActiveTab = null!;

    public ConfigWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} Config###{Plugin.Name}Config", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(350, 420) * ImGuiHelpers.GlobalScale;

        Tabs = [
            new UiTab(FontAwesomeIcon.PaintBrush, "UI Properties", DrawUiTab),
            new UiTab(FontAwesomeIcon.Globe, "Shared Browser Properties", DrawBrowserTab),
            new UiTab(FontAwesomeIcon.Display, "Shared Renderer Properties", DrawRendererTab),
            new UiTab(FontAwesomeIcon.Lightbulb, "Shared Lighting Properties", DrawLightTab),
            new UiTab(FontAwesomeIcon.Music, "Shared Audio Properties", DrawAudioTab)
        ];
        ActiveTab = Tabs[0];
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(!IsOpen) return;

        DrawTabs();

        ImGui.BeginChild("##container", ImGui.GetContentRegionAvail());
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(cursorPos + WindowPadding);
        ImGui.BeginChild("##content", ImGui.GetContentRegionAvail());
        ActiveTab.Draw();
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void DrawTabs() {
        var cursorPos = ImGui.GetCursorScreenPos();

        var iconSize = LineHeight;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(cursorPos, cursorPos + new Vector2(iconSize, ImGui.GetContentRegionAvail().Y), ImGui.GetColorU32(UIShared.TabBg));

        foreach(UiTab tab in Tabs) {
            var i = Tabs.IndexOf(tab);
            var tMin = cursorPos + new Vector2(0, iconSize * i);
            var tMax = tMin + new Vector2(iconSize, iconSize);
            if(DrawTab(tMin, tMax, iconSize, tab, ActiveTab == tab)) {
                ActiveTab = tab;
            }
        }

        ImGui.SetCursorScreenPos(cursorPos + new Vector2(iconSize, 0));
    }

    private bool DrawTab(Vector2 min, Vector2 max, float iconSize, UiTab tab, bool active) {
        var draw = ImGui.GetWindowDrawList();

        var hovered = UiUtil.IsRectHovered(min, max);
        var clicked = UiUtil.IsRectClicked(min, max);

        var bgCol = active ? UIShared.TabBgActive : clicked ? UIShared.TabBgClicked : hovered ? UIShared.TabBgHovered : UIShared.TabBgNormal;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(bgCol), UIShared.TabRounding);

        var iconPos = UiUtil.AlignCenter(min, max, iconSize);
        ImGui.SetCursorScreenPos(iconPos);
        if(clicked || ImGuiEx.IconToggleButton(tab.Icon, $"##tab{tab.Icon}", active, tooltip: tab.Tooltip, size: iconSize)) {
            return true;
        }
        return false;
    }

    private void DrawUiTab() {
        var props = Config.Global;
        var region = ImGui.GetContentRegionAvail();
        var state = UIState.None;
        var changed = false;

        ImGuiEx.StyledText("UI Accent Colours");
        state |= ImGuiEx.ColorPicker4("Window Bg##accentBg", ref props.General.AccentBg);
        ImGui.SameLine();
        state |= ImGuiEx.ColorPicker4("Window Title##accentTitle", ref props.General.AccentTitle);
        state |= ImGuiEx.ColorPicker4("Item Hovered##accentHovered", ref props.General.AccentHovered);
        ImGui.SameLine();
        state |= ImGuiEx.ColorPicker4("Item Active##accentActive", ref props.General.AccentActive);

        if(state != UIState.None) UIShared.Update();
        if(changed || state == UIState.Ended) Config.Save();
    }

    private void DrawBrowserTab() {
        var props = Config.Global;
        var region = ImGui.GetContentRegionAvail().X - WindowPadding.X;
        var state = UIState.None;
        var changed = false;

        ImGuiEx.StyledText("Shared Browser Properties");

        var homeEnumWidth = 70f * ImGuiHelpers.GlobalScale;
        changed |= ImGuiEx.EnumCombo("##homeType", string.Empty, ref props.Browser.HomeUriType, displayType: ComboButtonDisplayType.Items, width: homeEnumWidth, tooltip: "Home Uri Type",
            tooltipSub: "- Blank: Display a blank homepage (pix:// or about:blank)\n" +
                        "- Starry: Display a starry homepage (pix://starry)\n" +
                        "- Custom: Display a custom homepage");
        ImGui.SameLine(0, ItemSpacing);
        state |= ImGuiEx.StyledInput("##home", ref props.Browser.HomeUri, "Custom Home Uri", disabled: props.Browser.HomeUriType != HomeUriType.Custom, maxLength: ushort.MaxValue, width: region - homeEnumWidth - ItemSpacing, tooltip: "Custom Home Uri", tooltipSub: "Homepage to display when creating a new Pix or when clicking the browser Home button.\n- Eg. https://google.com");

        ImGuiEx.SpacingY(ItemSpacing);

        var eWidth = (region - ItemSpacing) * 0.5f;
        changed |= ImGuiEx.EnumFlagsCombo("##spawnBehaviour", "Spawn Behaviour", ref props.Browser.TerritorySpawnBehaviour, displayType: ComboButtonDisplayType.Label, width: eWidth, tooltip: "Territory Spawn Behaviour",
            tooltipSub: "The spawn behaviour of a Pix browser environment when changing territory.");
        ImGui.SameLine(0, ItemSpacing);
        changed |= ImGuiEx.EnumFlagsCombo("##despawnBehaviour", "Despawn Behaviour", ref props.Browser.TerritoryDespawnBehaviour, displayType: ComboButtonDisplayType.Label, width: eWidth, tooltip: "Territory Despawn Behaviour",
            tooltipSub: "The despawn behaviour of a Pix browser environment when changing territory.");

        if(changed || state == UIState.Ended) Config.Save();
    }

    private void DrawRendererTab() {
        var props = Config.Global;
        var region = ImGui.GetContentRegionAvail().X - WindowPadding.X;
        var state = UIState.None;
        var changed = false;

        ImGuiEx.StyledText("Shared Renderer Properties");

        state |= ImGuiEx.Drag("Pix Spawn Limit##pixLimit", ref props.General.PixSpawnLimit, 0.2f, 1, 99, width: region, tooltip: "Pix Spawn Limit",
            tooltipSub: "When this limit is exceeded, the earliest activated Pix will be despawned.\n" +
            "High spawn limit may incur high system resource usage depending on media content.");

        ImGuiEx.Separator(region);

        var rWidth = (region - ItemSpacing) * 0.7f;
        if(ImGuiEx.EnumCombo("##renderMode", "Render Mode: ", ref props.Renderer.RenderMode, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Render Mode", tooltipSub: "Determines when in the render cycle the screen should be drawn.\nThis will affect whether the screen obscures 3D UI elements like nameplates.")) {
            changed = true;
        }
        if(ImGuiEx.EnumCombo("##depthMode", "Depth Mode: ", ref props.Renderer.DepthMode, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Depth Mode", tooltipSub: "Preferably set this to first/last if either works because the Auto option may affect transparency.")) {
            RendererService.ClearViews();
            changed = true;
        }

        ImGuiEx.Separator(region);

        if(ImGuiEx.EnumCombo("##renderFormat", "Format: ", ref props.Renderer.Format, ComboButtonDisplayType.Items, disabled: props.Renderer.UseShaderTarget, width: rWidth, tooltip: "Render Format", tooltipSub: "You may need to adjust this in combination with Binding Type if the screen isn't visible.")) {
            RendererService.ClearViews();
            changed = true;
        }
        ImGui.SameLine(0, ItemSpacing);
        if(ImGuiEx.Checkbox("Shader Target", ref props.Renderer.UseShaderTarget, size: LineHeight, tooltip: "Shader Target", tooltipSub: "If no combination of Render Format/Binding Type produces a visible screen, you can try this option which renders after shaders applied by sources like Reshade/Gshade.\nYou will need to set 'SourceAlphaBlend' below to 'Zero'.")) {
            RendererService.ClearViews();
            changed = true;
        }
        if(ImGuiEx.EnumCombo("##bindingType", "Binding Type: ", ref props.Renderer.ResourceBindingType, ComboButtonDisplayType.Items, disabled: props.Renderer.UseShaderTarget, width: rWidth, tooltip: "Resource Binding Type", tooltipSub: "You may need to adjust this in combination with Render Format if the screen isn't visible.")) {
            RendererService.ClearViews();
            changed = true;
        }

        ImGuiEx.Separator(region);

        changed |= ImGuiEx.Checkbox("Enable Blending", ref props.Renderer.IsBlendEnabled, tooltip: "Enable Blending", tooltipSub: "Default: True");
        changed |= ImGuiEx.Checkbox("AlphaToCoverage", ref props.Renderer.AlphaToCoverageEnable, tooltip: "AlphaToCoverage", tooltipSub: "Default: False");
        changed |= ImGuiEx.Checkbox("IndependentBlend", ref props.Renderer.IndependentBlendEnable, tooltip: "Independent Blend", tooltipSub: "Default: False");
        changed |= ImGuiEx.EnumCombo("##SourceBlend", "Source: ", ref props.Renderer.SourceBlend, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Source Blend", tooltipSub: "Default: SourceAlpha");
        changed |= ImGuiEx.EnumCombo("##DestinationBlend", "Destination: ", ref props.Renderer.DestinationBlend, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Destination Blend", tooltipSub: "Default: InverseSourceAlpha");
        changed |= ImGuiEx.EnumCombo("##BlendOperation", "BlendOps: ", ref props.Renderer.BlendOperation, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Blend Operation", tooltipSub: "Default: Add");
        changed |= ImGuiEx.EnumCombo("##SourceAlphaBlend", "SourceAlpha: ", ref props.Renderer.SourceAlphaBlend, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Source Alpha Blend", tooltipSub: "Default: One\nShader Target: Zero");
        changed |= ImGuiEx.EnumCombo("##DestinationAlphaBlend", "DestinationAlpha: ", ref props.Renderer.DestinationAlphaBlend, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Destination Alpha Blend", tooltipSub: "Default: Zero");
        changed |= ImGuiEx.EnumCombo("##AlphaBlendOperation", "AlphaBlendOps: ", ref props.Renderer.AlphaBlendOperation, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Alpha Blend Operation", tooltipSub: "Default: Add");
        changed |= ImGuiEx.EnumFlagsCombo("##RenderTargetWriteMask", "WriteMask: ", ref props.Renderer.RenderTargetWriteMask, ComboButtonDisplayType.Items, width: rWidth, tooltip: "Render Target Write Mask", tooltipSub: "Default: All");

        if(changed) {
            RendererService.RebuildGlobalProperties(Config.Global.Renderer);
        }

        if(changed || state == UIState.Ended) Config.Save();
    }

    private void DrawLightTab() {
        var props = Config.Global;
        var region = ImGui.GetContentRegionAvail().X - WindowPadding.X;
        var state = UIState.None;
        var changed = false;

        ImGuiEx.StyledText("Shared Light Properties");

        state |= ImGuiEx.Drag("Influence Smoothing##influenceSmoothing", ref props.Light.InfluenceSmoothing, 0.001f, 0f, 1f, width: region, tooltip: "Screen Colour Influence Smoothing",
            tooltipSub: "Adjust how smooth the transition of colour changes are for rendered screens with light that is influenced by screen colour.\n" +
            "Higher smoothing will reduce light flickering when rendered frames rapidly change colours.");
        state |= ImGuiEx.Drag("Smoothing Duration##smoothingDuration", ref props.Light.InfluenceSmoothingDuration, 0.004f, 0f, 2f, width: region, tooltip: "Influence Smoothing Duration",
            tooltipSub: "The time taken (in seconds) for a smoothing transition to complete.");

        if(changed || state == UIState.Ended) Config.Save();
    }

    private void DrawAudioTab() {
        var props = Config.Global;
        var region = ImGui.GetContentRegionAvail().X - WindowPadding.X;
        var state = UIState.None;
        var changed = false;

        ImGuiEx.StyledText("Shared Audio Properties");

        var audioEnumWidth = 90f * ImGuiHelpers.GlobalScale;
        changed |= ImGuiEx.EnumCombo("##listenerType", string.Empty, ref props.Audio.ListenerType, displayType: ComboButtonDisplayType.Items, width: audioEnumWidth, tooltip: "Spatial Audio Listener Type",
            tooltipSub: "- Character: Spatial audio relative to character position & facing direction from screen.\n" +
                        "- Camera: Spatial audio relative to camera position & rotation from screen.");
        ImGui.SameLine(0, ItemSpacing);
        state |= ImGuiEx.Drag("Master Volume##masterVolume", ref props.Audio.MasterVolume, 0.001f, 0f, 1f, width: region - audioEnumWidth - ItemSpacing, tooltip: "Master Volume",
            tooltipSub: "Per Pix audio volume will be in relation to this master volume.");

        if(changed || state == UIState.Ended) Config.Save();
    }
}
