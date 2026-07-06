using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Config.Pix;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Services.Game;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Structs.Territory;
using PyonPix.Shared.Sync.Dto.Client;
using PyonPix.Shared.Utility;
using PyonPix.Structs.PlayerState;
using PyonPix.Structs.Ui;
using PyonPix.Ui.Components;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class PixConfigWindow : BaseWindow {
    private PixService PixService => Services.Get<PixService>();
    private SyncService SyncService => Services.Get<SyncService>();
    private StateService StateService => Services.Get<StateService>();
    private BrowserService BrowserService => Services.Get<BrowserService>();

    protected override WindowState State => Config.UI.PixConfig.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.PixConfig.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(420, 190);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;

    public override void OnClose() {
        base.OnClose();
        TransformEditor.HideGizmo();
    }

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.PixConfig.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.PixConfig.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnConfigClicked() => Windows.Get<ConfigWindow>().Toggle();
    protected override void OnCloseClicked() {
        SelectedPix = null;
        IsOpen = false;
    }

    private readonly List<UiTab> Tabs = null!;
    private UiTab ActiveTab = null!;

    private float Spacing => 6f * ImGuiHelpers.GlobalScale;

    public IPix? SelectedPix;
    private ContextMenu? SyncOverrideContextMenu = null;
    private readonly TransformEditor TransformEditor;

    public PixConfigWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"Pix Config###{Plugin.Name}PixConfig", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(420, 420) * ImGuiHelpers.GlobalScale;

        Tabs = [
            new UiTab(FontAwesomeIcon.Info, "Info Properties", DrawInfoTab),
            new UiTab(FontAwesomeIcon.Globe, "Browser Properties", DrawBrowserTab),
            new UiTab(FontAwesomeIcon.Display, "Renderer Properties", DrawRendererTab),
            new UiTab(FontAwesomeIcon.Lightbulb, "Light Properties", DrawLightTab),
            new UiTab(FontAwesomeIcon.Music, "Audio Properties", DrawAudioTab),
            new UiTab(FontAwesomeIcon.Sync, "Sync Properties", DrawSyncTab),
        ];
        ActiveTab = Tabs[0];

        TransformEditor = new();

        SyncService.SyncedPixCreated += (local, synced) => {
            if(SelectedPix?.Id != local.Id) return;
            SetSelectedPix(synced);
        };

        SyncService.SyncedPixDeleted += (syncedPixId, local) => {
            if(!IsSelectedPixId(syncedPixId)) return;
            if(local != null) {
                SetSelectedPix(local);
            } else {
                SetSelectedPix(null);
            }
        };

        SyncService.SyncedPixUnsubscribed += (pixId) => {
            if(IsSelectedPixId(pixId)) Toggle(null);
        };
        SyncService.StateChanged += (connectionState, statusMessage, statusType) => {
            if(SelectedPix is SyncedPix && connectionState == ConnectionState.Disconnected) Toggle(null);
        };
    }

    public bool IsSelectedPixId(string? pixId) {
        if(string.IsNullOrWhiteSpace(pixId)) return false;
        return string.Equals(SelectedPix?.Id, pixId, StringComparison.OrdinalIgnoreCase);
    }

    public void SetSelectedPix(IPix? pix) {
        if(pix == null) {
            SelectedPix = null;
            IsOpen = false;
            return;
        }

        WindowName = $"{pix.Id} Config###{Plugin.Name}PixConfig";
        SelectedPix = pix;
        IsOpen = true;
    }

    public void Toggle(IPix? pix) {
        if(pix == null || pix == SelectedPix) {
            SelectedPix = null;
            IsOpen = false;
            return;
        }
        WindowName = $"{pix.Id} Config###{Plugin.Name}PixConfig";
        SelectedPix = pix;
        IsOpen = true;
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(SelectedPix == null) IsOpen = false;
        if(!IsOpen) return;

        DrawTabs();

        ImGui.BeginChild("##pixContainer", ImGui.GetContentRegionAvail());
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(cursorPos + new Vector2(Spacing, Spacing));
        ImGui.BeginChild("##pixContent", ImGui.GetContentRegionAvail() - new Vector2(0, Spacing));
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
                TransformEditor.HideGizmo();
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

    private ContextMenu? WorldContextMenu = null;
    private bool ResidentialOnly = true;
    private void DrawInfoTab() {
        var infoBinding = PixService.BindOwnerField(SelectedPix!, p => p.Info, (p, v) => p.Info = v);
        var props = infoBinding.Value;
        var isSynced = SelectedPix!.Sync.IsSynced;
        var canEdit = infoBinding.CanEdit && (!isSynced || SyncService.IsConnectedAuth);
        var state = UIState.None;
        var changed = false;

        var avail = ImGui.GetContentRegionAvail();
        var scale = ImGuiHelpers.GlobalScale;

        var typeEnumWidth = 70f * scale;
        changed |= ImGuiEx.EnumCombo("##pixType", string.Empty, ref props.Type, ComboButtonDisplayType.Items, !canEdit, width: typeEnumWidth, tooltip: "Pix Category",
            tooltipSub: "Select a category which best relates to the primary use for this Pix.\n" +
                        "- Video: Watching videos/livestreams.\n" +
                        "- Audio: Listening to music or background ambience.\n" +
                        "- Image: Displaying static/animated images.\n" +
                        "- Game: Playing/spectating games.\n" +
                        "- Light: Rendering a source of light.\n" +
                        "- Other: Any use in general.");
        ImGui.SameLine(0, Spacing);
        state |= ImGuiEx.StyledInput("##name", ref props.Name, "Name", !canEdit, maxLength: NameUtil.PixMaxLength, width: avail.X - typeEnumWidth - (Spacing * 2), tooltip: "Pix Name", tooltipSub: "A name to identify this Pix in place of its Id.");
        state |= ImGuiEx.StyledInput("##desc", ref props.Description, "Description", !canEdit, maxLength: NameUtil.PixDescMaxLength, width: avail.X - Spacing, tooltip: "Pix Description", tooltipSub: "Optional description detailing the usage of this Pix.");

        var err = string.Empty;
        var isValid = !isSynced || NameUtil.ValidatePix(props.Name, props.Description, SelectedPix!.Sync.SecretKey, SelectedPix.Sync.Privacy, SyncService.Client.Premium, out err);
        if((changed || state == UIState.Ended) && isValid && canEdit) {
            infoBinding.Commit(props, true);
            PixService.UpdateInfoProperties(SelectedPix, true);
        } else if(!isValid && canEdit) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText(err, animationType: AnimationType.Pulse, colorA: new Vector3(0.6f, 0, 0), colorB: new Vector3(1f, 0, 0), glowStrength: 0.1f, bgOpacity: 0.4f);
            }
        }
         
        ImGuiEx.Separator(avail.X - Spacing);

        var tProps = SelectedPix!.Territory;
        var curTerritory = StateService.CurrentTerritory;
        var isCurrentTerritory = tProps.Matches(curTerritory, tProps.Persistent);
        if(SelectedPix!.Sync.IsSynced) {
            using(UIShared.SubFont.Push()) {
                var col = isCurrentTerritory ? UIShared.AccentActive.AsVector3() : UIShared.AccentHovered.AsVector3();
                var glowStrength = 0.1f;
                var bgOpacity = isCurrentTerritory ? 0.4f : 0.2f;

                var world = StateService.GetWorldName(tProps.WorldId);
                if(!string.IsNullOrEmpty(world)) {
                    ImGui.SameLine(0, 0);
                    ImGuiEx.StyledText($"{world}", colorA: col, glowStrength: glowStrength, bgOpacity: bgOpacity);
                }
                var territory = StateService.GetTerritoryName(tProps.TerritoryId);
                if(!string.IsNullOrEmpty(territory)) {
                    ImGui.SameLine(0, 0);
                    ImGuiEx.StyledText($"{territory}", colorA: col, glowStrength: glowStrength, bgOpacity: bgOpacity);
                }
                if(tProps.Ward > 0) {
                    ImGui.SameLine(0, 0);
                    ImGuiEx.StyledText($"W{tProps.Ward}", colorA: col, glowStrength: glowStrength, bgOpacity: bgOpacity);
                }
                if(tProps.Plot > 0) {
                    ImGui.SameLine(0, 0);
                    ImGuiEx.StyledText($"P{tProps.Plot}", colorA: col, glowStrength: glowStrength, bgOpacity: bgOpacity);
                }
                if(tProps.Room > 0) {
                    ImGui.SameLine(0, 0);
                    ImGuiEx.StyledText($"R{tProps.Room}", colorA: col, glowStrength: glowStrength, bgOpacity: bgOpacity);
                }
                if(tProps.Floor != Floor.None) {
                    ImGui.SameLine(0, 0);
                    ImGuiEx.StyledText($"{tProps.Floor}", colorA: col, glowStrength: glowStrength, bgOpacity: bgOpacity);
                }
            }
            return;
        }

        if(ImGuiEx.IconButton(FontAwesomeIcon.MapMarkerAlt, "##setTerritory", curTerritory == null, tooltip: "Set to Current Territory", size: LineHeight)) {
            tProps.WorldId = curTerritory!.WorldId;
            tProps.TerritoryId = curTerritory!.TerritoryId;
            tProps.Ward = curTerritory!.Ward;
            tProps.Plot = curTerritory!.Plot;
            tProps.Room = curTerritory!.Room;
            tProps.Floor = curTerritory!.Floor;
            PixService.UpdateTerritory(SelectedPix);

            var rProps = SelectedPix.Renderer;
            rProps.Position = new(StateService.LocalPlayerPosition.X, StateService.LocalPlayerPosition.Y + 1f, StateService.LocalPlayerPosition.Z);
            PixService.UpdateRendererTransform(SelectedPix, true);
        }

        ImGui.SameLine(0, Spacing);
        var worldListWidth = 100f * ImGuiHelpers.GlobalScale;
        var isContextOpen = WorldContextMenu?.IsOpen() ?? false;
        var contextPos = ImGui.GetCursorScreenPos();
        var isClicked = ImGuiEx.IconTextButton(isContextOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight, StateService.GetWorldName(tProps.WorldId), "##world", width: worldListWidth, height: LineHeight);
        if(isClicked) {
            var tabs = new List<ContextMenuTab>();
            foreach(var region in Enum.GetValues<Region>()) {
                var regionWorlds = StateService.Worlds[region];
                var items = new List<ContextMenuItem>();
                foreach(var world in regionWorlds) {
                    items.Add(new ContextMenuButton(
                        world.Name,
                        onClick: () => {
                            if(tProps.WorldId == world.Id) return;
                            tProps.WorldId = world.Id;
                            PixService.UpdateTerritory(SelectedPix);
                        },
                        isActive: () => tProps.WorldId == world.Id
                    ));
                }
                tabs.Add(new ContextMenuTab($"{region}", $"{region}", items));
            }
            var activeRegionIndex = (int)StateService.GetRegionFromWorld(tProps.WorldId) - 1;
            WorldContextMenu = new ContextMenu("##worldContext", tabs, activeRegionIndex, 240f, 26f);
            WorldContextMenu.Open();
        }
        if(isContextOpen) WorldContextMenu?.Draw(new(contextPos.X, contextPos.Y + LineHeight));

        ImGui.SameLine(0, Spacing);
        if(ImGuiEx.ListCombo("##territory", string.Empty, "Selected Territory", ref tProps.TerritoryId, 
            StateService.UITerritoryList.Select(t => (t.Id, $"{t.Name} ({t.Id})")), 
            drawHeader: (() => {
                var checkSize = UiUtil.CalcTextSize(UIShared.NormalIconFont, FontAwesomeIcon.CheckSquare.ToIconString());
                var offsetY = (LineHeight - checkSize.Y) * 0.5f;
                var cursorPos = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(Spacing, offsetY));
                if(ImGuiEx.Checkbox("Residential Only##terrFilter", ref ResidentialOnly)) {
                    StateService.BuildUITerritoryList(ResidentialOnly);
                }
            }), width: avail.X - worldListWidth - LineHeight - (Spacing * 3))) {
            tProps.Ward = tProps.Plot = tProps.Room = 0;
            PixService.UpdateTerritory(SelectedPix);
        }

        var resi = StateService.ResidentialTerritories.FirstOrDefault(x => x.Id == tProps.TerritoryId);
        if(resi != null && resi.ResidentialType != ResidentialType.Workshop) {
            var rWidth = 80 * ImGuiHelpers.GlobalScale;
            switch(resi.ResidentialType) {
                case ResidentialType.Ward:
                    if(ImGuiEx.Drag<short>("Ward##ward", ref tProps.Ward, 0.1f, 1, 30, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Drag<short>("Plot##plot", ref tProps.Plot, 0.1f, 0, 60, width: rWidth, tooltip: "Plot (Garden)", tooltipSub: "Set to '0' to have this Pix active within a Ward outside of garden Plots.\nOtherwise limit to a specific Plot's garden.") == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Checkbox("Persistent Plot##persist", ref tProps.Persistent, size: LineHeight, tooltip: "Persistent Plot", tooltipSub: "If true, this Pix will persist across garden Plots within the Ward.")) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    break;
                case ResidentialType.House:
                    if(ImGuiEx.Drag<short>("Ward##ward", ref tProps.Ward, 0.1f, 1, 30, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Drag<short>("Plot##plot", ref tProps.Plot, 0.1f, 1, 60, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.EnumCombo("##floor", string.Empty, ref tProps.Floor, ComboButtonDisplayType.Items, width: rWidth, ignoredValue: Floor.None)) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Checkbox("Persistent Floor##persist", ref tProps.Persistent, size: LineHeight, tooltip: "Persistent Floor", tooltipSub: "If true, this Pix will persist across Floors within the Plot.")) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    break;
                case ResidentialType.Chambers:
                    if(ImGuiEx.Drag<short>("Ward##ward", ref tProps.Ward, 0.1f, 1, 30, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Drag<short>("Plot##plot", ref tProps.Plot, 0.1f, 1, 60, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Drag<short>("Room##room", ref tProps.Room, 0.1f, 1, 512, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    break;
                case ResidentialType.Apartment:
                    if(ImGuiEx.Drag<short>("Ward##ward", ref tProps.Ward, 0.1f, 1, 30, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    ImGui.SameLine();
                    if(ImGuiEx.Drag<short>("Room##room", ref tProps.Room, 0.1f, 1, 90, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    break;
                case ResidentialType.ApartmentLobby:
                    if(ImGuiEx.Drag<short>("Ward##ward", ref tProps.Ward, 0.1f, 1, 30, width: rWidth) == UIState.Ended) {
                        PixService.UpdateTerritory(SelectedPix);
                    }
                    break;
            }
        }
    }

    private void DrawBrowserTab() {
        var props = SelectedPix!.Browser;
        var region = ImGui.GetContentRegionAvail();
        var variant = PixService.GetVariant(SelectedPix, true)!;

        var canEdit = true;
        if(SelectedPix is SyncedPix syncedPix) canEdit = syncedPix.CanSyncEdit && SyncService.IsConnectedAuth;
        if(ImGuiEx.StyledInput("##uri", ref props.Uri, "Uri", disabled: !canEdit, maxLength: ushort.MaxValue, width: region.X - Spacing, tooltip: "Current Uri", tooltipSub: "Uri updates when navigating to other pages.\n" +
            "Local files are also supported with file:/// scheme (but won't be synced).") == UIState.Ended) {
            PixService.UpdateUri(SelectedPix, false);
        }

        var gpuBinding = PixService.BindBrowserField(SelectedPix, p => p.GpuAcceleration, (p, v) => p.GpuAcceleration = v, o => o.GpuAcceleration, (o, v) => o.GpuAcceleration = v);
        var gpu = gpuBinding.Value;
        if(ImGuiEx.Checkbox("GPU Acceleration", ref gpu, tooltip: "GPU Acceleration", tooltipSub: "You may need to disable this when viewing DRM protected content.\nChanges will only be applied when the Pix is restarted.")) {
            gpuBinding.Commit(gpu, true);
        }
        DrawSyncOverrideContext(gpuBinding, "##syncBrowserGpu");

        ImGui.SameLine();
        if(ImGuiEx.Checkbox("Persistent Cache", ref variant.PersistentCache, tooltip: "Persistent Cache", tooltipSub: "Disables automatic clearing of cached data when this Pix despawns.")) {
            Config.Save();
        }

        ImGui.SameLine();
        if(ImGuiEx.Checkbox("Shared Cookies", ref variant.SyncCookies, tooltip: "Shared Cookies", tooltipSub: "Whether this Pix should share cookies via the Host environment.\nThis allows auto login to web services across Pix environments.\nCan cause session issues with some services like Youtube.\nNote: Cookies are not synced with other users, this option only controls sharing cookies between local Pix environments.")) {
            Config.Save();
        }

        ImGuiEx.Separator(region.X - Spacing);

        var scaleModeBinding = PixService.BindBrowserField(SelectedPix, p => p.ScaleMode, (p, v) => p.ScaleMode = v, o => o.ScaleMode, (o, v) => o.ScaleMode = v);
        var scaleMode = scaleModeBinding.Value;
        var scaleEnumWidth = 190 * ImGuiHelpers.GlobalScale;
        var scaleChanged = ImGuiEx.EnumCombo("##scaleMode", string.Empty, ref scaleMode, ComboButtonDisplayType.Items, width: scaleEnumWidth, tooltip: "Render Scale Mode",
            tooltipSub: "Determines how the texture received from the browser should be scaled.\n" +
                        "- BrowserWindow: Use same scale as the interactive browser.\n" +
                        "- GameWindow: Use the same scale as the game window.\n" +
                        "- GameWindowWhenHidden: Use game scale while browser is collapsed/closed, otherwise use browser scale.\n" +
                        "- CustomScale: Use custom scale defined by the Width/Height inputs.\n" +
                        "- CustomScaleWhenHidden: Use custom scale while browser is collapsed/closed, otherwise use browser scale.");
        if(scaleChanged) scaleModeBinding.Commit(scaleMode, true);
        DrawSyncOverrideContext(scaleModeBinding, "##syncBrowserScaleMode");

        ImGui.SameLine(0, Spacing);
        var scaleWidth = (region.X - scaleEnumWidth - (Spacing * 3)) * 0.5f;

        var widthBinding = PixService.BindBrowserField(SelectedPix, p => p.CustomScaleWidth, (p, v) => p.CustomScaleWidth = v, o => o.CustomScaleWidth, (o, v) => o.CustomScaleWidth = v);
        var customWidth = widthBinding.Value;
        var widthState = ImGuiEx.Drag<uint>("Width##scaleWidth", ref customWidth, 1f, 5, 5120, 0, width: scaleWidth, tooltip: "Custom Scale Width");
        if(widthState != UIState.None) widthBinding.Commit(customWidth, widthState == UIState.Ended);
        DrawSyncOverrideContext(widthBinding, "##syncBrowserScaleWidth");

        ImGui.SameLine(0, Spacing);

        var heightBinding = PixService.BindBrowserField(SelectedPix, p => p.CustomScaleHeight, (p, v) => p.CustomScaleHeight = v, o => o.CustomScaleHeight, (o, v) => o.CustomScaleHeight = v);
        var customHeight = heightBinding.Value;
        var heightState = ImGuiEx.Drag<uint>("Height##scaleHeight", ref customHeight, 1f, 5, 5120, 0, width: scaleWidth, tooltip: "Custom Scale Height");
        if(heightState != UIState.None) heightBinding.Commit(customHeight, heightState == UIState.Ended);
        DrawSyncOverrideContext(heightBinding, "##syncBrowserScaleHeight");

        var scalingState = widthState | heightState;
        if(scalingState == UIState.Using && !BrowserService.IsRescaling) {
            BrowserService.IsRescaling = true;
        } else if(scalingState == UIState.Ended && BrowserService.IsRescaling) {
            BrowserService.IsRescaling = false;
        }
    }

    private void DrawRendererTab() {
        var region = ImGui.GetContentRegionAvail();

        var posBinding = PixService.BindRendererTransformField(SelectedPix!, p => p.Position, (p, v) => p.Position = v, o => o.Position, (o, v) => o.Position = v);
        var rotBinding = PixService.BindRendererTransformField(SelectedPix!, p => p.Rotation, (p, v) => p.Rotation = v, o => o.Rotation, (o, v) => o.Rotation = v);
        var sclBinding = PixService.BindRendererTransformField(SelectedPix!, p => p.Scale, (p, v) => p.Scale = v, o => o.Scale, (o, v) => o.Scale = v);
        var pos = posBinding.Value;
        var rot = rotBinding.Value;
        var scl = sclBinding.Value;
        var res = TransformEditor.DrawTable("##rendererTable", ref pos, ref rot, ref scl,
            posAction: new((id) => { DrawSyncOverrideContext(posBinding, $"##syncRendererPos{id}"); }),
            rotAction: new((id) => { DrawSyncOverrideContext(rotBinding, $"##syncRendererRot{id}"); }),
            sclAction: new((id) => { DrawSyncOverrideContext(sclBinding, $"##syncRendererScl{id}"); }));
        if(res != UIState.None) {
            posBinding.Commit(pos, false);
            rotBinding.Commit(rot, false);
            sclBinding.Commit(scl, res == UIState.Ended);
        }
        res = TransformEditor.DrawGizmo("##rendererGizmo", ref pos, ref rot, ref scl);
        if(res != UIState.None) {
            posBinding.Commit(pos, false);
            rotBinding.Commit(rot, false);
            sclBinding.Commit(scl, res == UIState.Ended);
        }

        ImGuiEx.Separator(region.X - Spacing);

        var screenTintBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.ScreenTint, (p, v) => p.ScreenTint = v, o => o.ScreenTint, (o, v) => o.ScreenTint = v);
        var edgeColourBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.EdgeColour, (p, v) => p.EdgeColour = v, o => o.EdgeColour, (o, v) => o.EdgeColour = v);
        var backColourBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.BackColour, (p, v) => p.BackColour = v, o => o.BackColour, (o, v) => o.BackColour = v);

        var screenTint = screenTintBinding.Value;
        var edgeColour = edgeColourBinding.Value;
        var backColour = backColourBinding.Value;

        var screenTintState = ImGuiEx.ColorPicker4("Screen Tint##screenTint", ref screenTint);
        if(screenTintState != UIState.None) screenTintBinding.Commit(screenTint, screenTintState == UIState.Ended);
        DrawSyncOverrideContext(screenTintBinding, "##syncRendererScreenTint");

        ImGui.SameLine();
        var edgeColourState = ImGuiEx.ColorPicker4("Edge Colour##edgeColour", ref edgeColour);
        if(edgeColourState != UIState.None) edgeColourBinding.Commit(edgeColour, edgeColourState == UIState.Ended);
        DrawSyncOverrideContext(edgeColourBinding, "##syncRendererEdgeColour");

        ImGui.SameLine();
        var backColourState = ImGuiEx.ColorPicker4("Back Colour##backColour", ref backColour);
        if(backColourState != UIState.None) backColourBinding.Commit(backColour, backColourState == UIState.Ended);
        DrawSyncOverrideContext(backColourBinding, "##syncRendererBackColour");

        ImGuiEx.Separator(region.X - Spacing);

        var borderColourBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.BorderColour, (p, v) => p.BorderColour = v, o => o.BorderColour, (o, v) => o.BorderColour = v);
        var borderModeBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.BorderMode, (p, v) => p.BorderMode = v, o => o.BorderMode, (o, v) => o.BorderMode = v);
        var borderWidthHBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.BorderWidthH, (p, v) => p.BorderWidthH = v, o => o.BorderWidthH, (o, v) => o.BorderWidthH = v);
        var borderWidthVBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.BorderWidthV, (p, v) => p.BorderWidthV = v, o => o.BorderWidthV, (o, v) => o.BorderWidthV = v);

        var borderColour = borderColourBinding.Value;
        var borderColourState = ImGuiEx.ColorPicker4("Border Colour##borderColour", ref borderColour);
        if(borderColourState != UIState.None) borderColourBinding.Commit(borderColour, borderColourState == UIState.Ended);
        DrawSyncOverrideContext(borderColourBinding, "##syncRendererBorderColour");

        var borderMode = borderModeBinding.Value;
        if(ImGuiEx.EnumCombo("##borderMode", "Border Mode: ", ref borderMode, ComboButtonDisplayType.Items, width: region.X - Spacing)) {
            borderModeBinding.Commit(borderMode, true);
        }
        DrawSyncOverrideContext(borderModeBinding, "##syncRendererBorderMode");

        var borderWidthH = borderWidthHBinding.Value;
        var borderHState = ImGuiEx.Drag("HBorder Width##hBorderWidth", ref borderWidthH, 0.001f, 0f, 1f, width: region.X - Spacing);
        if(borderHState != UIState.None) borderWidthHBinding.Commit(borderWidthH, borderHState == UIState.Ended);
        DrawSyncOverrideContext(borderWidthHBinding, "##syncRendererBorderWidthH");

        var borderWidthV = borderWidthVBinding.Value;
        var borderVState = ImGuiEx.Drag("VBorder Width##vBorderWidth", ref borderWidthV, 0.001f, 0f, 1f, width: region.X - Spacing);
        if(borderVState != UIState.None) borderWidthVBinding.Commit(borderWidthV, borderVState == UIState.Ended);
        DrawSyncOverrideContext(borderWidthVBinding, "##syncRendererBorderWidthV");

        ImGuiEx.Separator(region.X - Spacing);

        var borderFeatherBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.BorderFeather, (p, v) => p.BorderFeather = v, o => o.BorderFeather, (o, v) => o.BorderFeather = v);
        var edgeFeatherBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.EdgeFeather, (p, v) => p.EdgeFeather = v, o => o.EdgeFeather, (o, v) => o.EdgeFeather = v);

        var borderFeather = borderFeatherBinding.Value;
        var borderFeatherState = ImGuiEx.Drag("Border Feather##borderFeather", ref borderFeather, 0.001f, 0f, 10f, width: region.X - Spacing);
        if(borderFeatherState != UIState.None) borderFeatherBinding.Commit(borderFeather, borderFeatherState == UIState.Ended);
        DrawSyncOverrideContext(borderFeatherBinding, "##syncRendererBorderFeather");

        var edgeFeather = edgeFeatherBinding.Value;
        var edgeFeatherState = ImGuiEx.Drag("Edge Feather##edgeFeather", ref edgeFeather, 0.001f, 0f, 10f, width: region.X - Spacing);
        if(edgeFeatherState != UIState.None) edgeFeatherBinding.Commit(edgeFeather, edgeFeatherState == UIState.Ended);
        DrawSyncOverrideContext(edgeFeatherBinding, "##syncRendererEdgeFeather");

        ImGuiEx.Separator(region.X - Spacing);

        var depthBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.Depth, (p, v) => p.Depth = v, o => o.Depth, (o, v) => o.Depth = v);
        var depthOffsetBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.DepthOffset, (p, v) => p.DepthOffset = v, o => o.DepthOffset, (o, v) => o.DepthOffset = v);
        var depthCompBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.DepthComparison, (p, v) => p.DepthComparison = v, o => o.DepthComparison, (o, v) => o.DepthComparison = v);
        var cullModeBinding = PixService.BindRendererPropertyField(SelectedPix!, p => p.CullMode, (p, v) => p.CullMode = v, o => o.CullMode, (o, v) => o.CullMode = v);

        var depth = depthBinding.Value;
        if(ImGuiEx.Checkbox("Enable Depth##enableDepth", ref depth)) {
            depthBinding.Commit(depth, true);
        }
        DrawSyncOverrideContext(depthBinding, "##syncRendererDepth");

        var depthOffset = depthOffsetBinding.Value;
        var depthOffsetState = ImGuiEx.Drag("Depth Offset##depthOffset", ref depthOffset, 0.001f, 0f, 10f, disabled: !depth, width: region.X - Spacing);
        if(depthOffsetState != UIState.None) depthOffsetBinding.Commit(depthOffset, depthOffsetState == UIState.Ended);
        DrawSyncOverrideContext(depthOffsetBinding, "##syncRendererDepthOffset");

        var depthComp = depthCompBinding.Value;
        if(ImGuiEx.EnumCombo("##depthComp", "Depth Comparison: ", ref depthComp, ComboButtonDisplayType.Items, disabled: !depth, width: region.X - Spacing)) {
            depthCompBinding.Commit(depthComp, true);
        }
        DrawSyncOverrideContext(depthCompBinding, "##syncRendererDepthComp");

        var cullMode = cullModeBinding.Value;
        if(ImGuiEx.EnumCombo("##cullMode", "Cull Mode: ", ref cullMode, ComboButtonDisplayType.Items, width: region.X - Spacing)) {
            cullModeBinding.Commit(cullMode, true);
        }
        DrawSyncOverrideContext(cullModeBinding, "##syncRendererCullMode");
    }

    private void DrawLightTab() {
        var region = ImGui.GetContentRegionAvail();

        var posBinding = PixService.BindLightTransformField(SelectedPix!, p => p.Position, (p, v) => p.Position = v, o => o.Position, (o, v) => o.Position = v);
        var rotBinding = PixService.BindLightTransformField(SelectedPix!, p => p.Rotation, (p, v) => p.Rotation = v, o => o.Rotation, (o, v) => o.Rotation = v);
        var pos = posBinding.Value;
        var rot = rotBinding.Value;
        var res = TransformEditor.DrawTable("##lightTable", ref pos, ref rot,
            posAction: new((id) => { DrawSyncOverrideContext(posBinding, $"##syncRendererPos{id}"); }),
            rotAction: new((id) => { DrawSyncOverrideContext(rotBinding, $"##syncRendererRot{id}"); }));
        if(res != UIState.None) {
            var ended = res == UIState.Ended;
            posBinding.Commit(pos, false);
            rotBinding.Commit(rot, ended);
        }
        var renderer = SelectedPix!.Renderer;
        var worldPos = Vector3.Transform(pos, renderer.Rotation) + renderer.Position;
        var worldRot = Quaternion.Normalize(Quaternion.Multiply(renderer.Rotation, rot));
        res = TransformEditor.DrawGizmo("##lightGizmo", ref worldPos, ref worldRot);
        if(res != UIState.None) {
            var invRendererRot = Quaternion.Inverse(renderer.Rotation);
            var localPos = Vector3.Transform(worldPos - renderer.Position, invRendererRot);
            var localRot = Quaternion.Normalize(Quaternion.Multiply(invRendererRot, worldRot));
            var ended = res == UIState.Ended;
            posBinding.Commit(localPos, false);
            rotBinding.Commit(localRot, ended);
            pos = localPos;
            rot = localRot;
        }

        ImGuiEx.Separator(region.X - Spacing);

        var enabledBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.Enabled, (p, v) => p.Enabled = v, o => o.Enabled, (o, v) => o.Enabled = v);
        var enabled = enabledBinding.Value;
        if(ImGuiEx.Checkbox("Enable Light##enableLight", ref enabled)) {
            enabledBinding.Commit(enabled, true);
        }
        DrawSyncOverrideContext(enabledBinding, "##syncLightEnabled");

        var typeBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.LightType, (p, v) => p.LightType = v, o => o.LightType, (o, v) => o.LightType = v);
        var lightType = typeBinding.Value;
        if(ImGuiEx.EnumCombo("##lightType", "Light Type: ", ref lightType, ComboButtonDisplayType.Items, disabled: !enabled, width: region.X - Spacing)) {
            typeBinding.Commit(lightType, true);
        }
        DrawSyncOverrideContext(typeBinding, "##syncLightType");

        var colourBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.Colour, (p, v) => p.Colour = v, o => o.Colour, (o, v) => o.Colour = v);
        var colour = colourBinding.Value;
        var colourState = ImGuiEx.ColorPicker4("Colour##lightColour", ref colour);
        if(colourState != UIState.None) colourBinding.Commit(colour, colourState == UIState.Ended);
        DrawSyncOverrideContext(colourBinding, "##syncLightColour");

        var intensityBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.Intensity, (p, v) => p.Intensity = v, o => o.Intensity, (o, v) => o.Intensity = v);
        var intensity = intensityBinding.Value;
        var intensityState = ImGuiEx.Drag("Intensity##lightIntensity", ref intensity, 0.01f, 0f, 100f, disabled: !enabled, width: region.X - Spacing);
        if(intensityState != UIState.None) intensityBinding.Commit(intensity, intensityState == UIState.Ended);
        DrawSyncOverrideContext(intensityBinding, "##syncLightIntensity");

        ImGuiEx.Separator(region.X - Spacing);

        var influenceBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.ScreenColourInfluence, (p, v) => p.ScreenColourInfluence = v, o => o.ScreenColourInfluence, (o, v) => o.ScreenColourInfluence = v);
        var influence = influenceBinding.Value;
        var influenceState = ImGuiEx.Drag("Screen Influence##screenInfluence", ref influence, 0.001f, 0f, 1f, disabled: !enabled, width: region.X - Spacing);
        if(influenceState != UIState.None) influenceBinding.Commit(influence, influenceState == UIState.Ended);
        DrawSyncOverrideContext(influenceBinding, "##syncLightInfluence");

        var colourIntensityBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.InfluenceColourIntensity, (p, v) => p.InfluenceColourIntensity = v, o => o.InfluenceColourIntensity, (o, v) => o.InfluenceColourIntensity = v);
        var colourIntensity = colourIntensityBinding.Value;
        var colourIntensityState = ImGuiEx.Drag("Screen Colour Intensity##colourIntensity", ref colourIntensity, 0.001f, 0f, 10f, disabled: !enabled, width: region.X - Spacing);
        if(colourIntensityState != UIState.None) colourIntensityBinding.Commit(colourIntensity, colourIntensityState == UIState.Ended);
        DrawSyncOverrideContext(colourIntensityBinding, "##syncLightColourIntensity");

        var brightnessIntensityBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.InfluenceBrightnessIntensity, (p, v) => p.InfluenceBrightnessIntensity = v, o => o.InfluenceBrightnessIntensity, (o, v) => o.InfluenceBrightnessIntensity = v);
        var brightnessIntensity = brightnessIntensityBinding.Value;
        var brightnessIntensityState = ImGuiEx.Drag("Screen Brightness Intensity##brightnessIntensity", ref brightnessIntensity, 0.001f, 0f, 10f, disabled: !enabled, width: region.X - Spacing);
        if(brightnessIntensityState != UIState.None) brightnessIntensityBinding.Commit(brightnessIntensity, brightnessIntensityState == UIState.Ended);
        DrawSyncOverrideContext(brightnessIntensityBinding, "##syncLightBrightnessIntensity");

        var gammaBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.InfluenceGammaCurve, (p, v) => p.InfluenceGammaCurve = v, o => o.InfluenceGammaCurve, (o, v) => o.InfluenceGammaCurve = v);
        var gamma = gammaBinding.Value;
        var gammaState = ImGuiEx.Drag("Screen Gamma Curve##gammaCurve", ref gamma, 0.001f, 0f, 1f, disabled: !enabled, width: region.X - Spacing);
        if(gammaState != UIState.None) gammaBinding.Commit(gamma, gammaState == UIState.Ended);
        DrawSyncOverrideContext(gammaBinding, "##syncLightGamma");

        ImGuiEx.Separator(region.X - Spacing);

        var rangeBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.Range, (p, v) => p.Range = v, o => o.Range, (o, v) => o.Range = v);
        var range = rangeBinding.Value;
        var rangeState = ImGuiEx.Drag("Light Range##lightRange", ref range, 0.01f, 0f, 100f, disabled: !enabled, width: region.X - Spacing);
        if(rangeState != UIState.None) rangeBinding.Commit(range, rangeState == UIState.Ended);
        DrawSyncOverrideContext(rangeBinding, "##syncLightRange");

        var angleBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.LightAngle, (p, v) => p.LightAngle = v, o => o.LightAngle, (o, v) => o.LightAngle = v);
        var angle = angleBinding.Value;
        var angleState = ImGuiEx.Drag("Light Angle##lightAngle", ref angle, 0.01f, 0f, 180f, disabled: !enabled, width: region.X - Spacing);
        if(angleState != UIState.None) angleBinding.Commit(angle, angleState == UIState.Ended);
        DrawSyncOverrideContext(angleBinding, "##syncLightAngle");

        ImGuiEx.Separator(region.X - Spacing);

        var falloffTypeBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.FalloffType, (p, v) => p.FalloffType = v, o => o.FalloffType, (o, v) => o.FalloffType = v);
        var falloffType = falloffTypeBinding.Value;
        if(ImGuiEx.EnumCombo("##falloffType", "Falloff Type: ", ref falloffType, ComboButtonDisplayType.Items, disabled: !enabled, width: region.X - Spacing)) {
            falloffTypeBinding.Commit(falloffType, true);
        }
        DrawSyncOverrideContext(falloffTypeBinding, "##syncLightFalloffType");

        var falloffAngleBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.FalloffAngle, (p, v) => p.FalloffAngle = v, o => o.FalloffAngle, (o, v) => o.FalloffAngle = v);
        var falloffAngle = falloffAngleBinding.Value;
        var falloffAngleState = ImGuiEx.Drag("Falloff Angle##falloffAngle", ref falloffAngle, 0.01f, 0f, 180f, disabled: !enabled, width: region.X - Spacing);
        if(falloffAngleState != UIState.None) falloffAngleBinding.Commit(falloffAngle, falloffAngleState == UIState.Ended);
        DrawSyncOverrideContext(falloffAngleBinding, "##syncLightFalloffAngle");

        var falloffPowerBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.FalloffPower, (p, v) => p.FalloffPower = v, o => o.FalloffPower, (o, v) => o.FalloffPower = v);
        var falloffPower = falloffPowerBinding.Value;
        var falloffPowerState = ImGuiEx.Drag("Falloff Power##falloffPower", ref falloffPower, 0.01f, 0f, 100f, disabled: !enabled, width: region.X - Spacing);
        if(falloffPowerState != UIState.None) falloffPowerBinding.Commit(falloffPower, falloffPowerState == UIState.Ended);
        DrawSyncOverrideContext(falloffPowerBinding, "##syncLightFalloffPower");

        ImGuiEx.Separator(region.X - Spacing);

        var flagsBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.Flags, (p, v) => p.Flags = v, o => o.Flags, (o, v) => o.Flags = v);
        var flags = flagsBinding.Value;
        if(ImGuiEx.EnumFlagsCombo("##shadowFlags", "Shadow Flags", ref flags, ComboButtonDisplayType.Label, disabled: !enabled, width: region.X - Spacing)) {
            flagsBinding.Commit(flags, true);
        }
        DrawSyncOverrideContext(flagsBinding, "##syncLightFlags");

        var shadowRangeBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.ShadowRange, (p, v) => p.ShadowRange = v, o => o.ShadowRange, (o, v) => o.ShadowRange = v);
        var shadowRange = shadowRangeBinding.Value;
        var shadowRangeState = ImGuiEx.Drag("Shadow Range##shadowRange", ref shadowRange, 0.01f, 0f, 50f, disabled: !enabled, width: region.X - Spacing);
        if(shadowRangeState != UIState.None) shadowRangeBinding.Commit(shadowRange, shadowRangeState == UIState.Ended);
        DrawSyncOverrideContext(shadowRangeBinding, "##syncLightShadowRange");

        var shadowNearBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.ShadowNear, (p, v) => p.ShadowNear = v, o => o.ShadowNear, (o, v) => o.ShadowNear = v);
        var shadowNear = shadowNearBinding.Value;
        var shadowNearState = ImGuiEx.Drag("Shadow Near##shadowNear", ref shadowNear, 0.01f, 0f, 50f, disabled: !enabled, width: region.X - Spacing);
        if(shadowNearState != UIState.None) shadowNearBinding.Commit(shadowNear, shadowNearState == UIState.Ended);
        DrawSyncOverrideContext(shadowNearBinding, "##syncLightShadowNear");

        var shadowFarBinding = PixService.BindLightPropertyField(SelectedPix!, p => p.ShadowFar, (p, v) => p.ShadowFar = v, o => o.ShadowFar, (o, v) => o.ShadowFar = v);
        var shadowFar = shadowFarBinding.Value;
        var shadowFarState = ImGuiEx.Drag("Shadow Far##shadowFar", ref shadowFar, 0.01f, 0f, 50f, disabled: !enabled, width: region.X - Spacing);
        if(shadowFarState != UIState.None) shadowFarBinding.Commit(shadowFar, shadowFarState == UIState.Ended);
        DrawSyncOverrideContext(shadowFarBinding, "##syncLightShadowFar");
    }

    private void DrawAudioTab() {
        var region = ImGui.GetContentRegionAvail();

        var spatialBinding = PixService.BindAudioField(SelectedPix, p => p.SpatialEnabled, (p, v) => p.SpatialEnabled = v, o => o.SpatialEnabled, (o, v) => o.SpatialEnabled = v);
        var spatialEnabled = spatialBinding.Value;
        if(ImGuiEx.Checkbox("Enable Spatial Audio##enableSpatial", ref spatialEnabled)) {
            spatialBinding.Commit(spatialEnabled, true);
        }
        DrawSyncOverrideContext(spatialBinding, "##syncSpatialAudio");

        var volumeBinding = PixService.BindAudioField(SelectedPix, p => p.Volume, (p, v) => p.Volume = v, o => o.Volume, (o, v) => o.Volume = v);
        var volume = volumeBinding.Value;
        var volumeState = ImGuiEx.Drag("Volume##volume", ref volume, 0.001f, 0f, 1f, disabled: !spatialEnabled, width: region.X - Spacing);
        if(volumeState != UIState.None) {
            volumeBinding.Commit(volume, volumeState == UIState.Ended);
        }
        DrawSyncOverrideContext(volumeBinding, "##syncAudioVolume");

        var falloffDistanceBinding = PixService.BindAudioField(SelectedPix, p => p.FalloffMaxDistance, (p, v) => p.FalloffMaxDistance = v, o => o.FalloffMaxDistance, (o, v) => o.FalloffMaxDistance = v);
        var falloffDistance = falloffDistanceBinding.Value;
        var falloffDistanceState = ImGuiEx.Drag("Falloff Distance##falloffDistance", ref falloffDistance, 0.1f, 0f, 100f, 1, disabled: !spatialEnabled, width: region.X - Spacing, tooltip: "Falloff Max Distance", tooltipSub: "The max distance from the rendered screen in world before volume is completely faded out.");
        if(falloffDistanceState != UIState.None) {
            falloffDistanceBinding.Commit(falloffDistance, falloffDistanceState == UIState.Ended);
        }
        DrawSyncOverrideContext(falloffDistanceBinding, "##syncAudioFalloffDistance");

        var falloffStrengthBinding = PixService.BindAudioField(SelectedPix, p => p.FalloffStrength, (p, v) => p.FalloffStrength = v, o => o.FalloffStrength, (o, v) => o.FalloffStrength = v);
        var falloffStrength = falloffStrengthBinding.Value;
        var falloffStrengthState = ImGuiEx.Drag("Falloff Strength##falloffStrength", ref falloffStrength, 0.1f, 0f, 50f, 1, disabled: !spatialEnabled, width: region.X - Spacing, tooltip: "Falloff Strength", tooltipSub: "Controls how significant the falloff adjustment is relative to distance.");
        if(falloffStrengthState != UIState.None) {
            falloffStrengthBinding.Commit(falloffStrength, falloffStrengthState == UIState.Ended);
        }
        DrawSyncOverrideContext(falloffStrengthBinding, "##syncAudioFalloffStrength");
    }

    private void DrawSyncTab() {
        var syncBinding = PixService.BindOwnerField(SelectedPix!, p => p.Sync, (p, v) => p.Sync = v);
        var props = syncBinding.Value;
        var canEdit = syncBinding.CanEdit && (!SelectedPix!.Sync.IsSynced || SyncService.IsConnectedAuth);
        var state = UIState.None;
        var changed = false;

        var region = ImGui.GetContentRegionAvail();
        var scale = ImGuiHelpers.GlobalScale;

        changed |= ImGuiEx.EnumCombo("##privacy", string.Empty, ref props.Privacy, ComboButtonDisplayType.Items, disabled: !canEdit, width: 80f * scale, tooltip: "Privacy", 
            tooltipSub: "Public - Pix will be publicly listed in the Sync Search window.\n" +
            "Unlisted - Pix will only be listed in the Sync Search window for users in the same territory.\n" +
            "Private - Pix will not be listed at all, Id/Password required.");
        var secretKey = !canEdit || props.SecretKey == null ? string.Empty : props.SecretKey;
        ImGuiEx.SpacingX(Spacing, true, true);
        state |= ImGuiEx.StyledInput("##secretKey", ref secretKey, "Password", !canEdit || props.Privacy != PixPrivacy.Private, maxLength: NameUtil.PixPassMaxLength, width: ImGui.GetContentRegionAvail().X - (Spacing * 2), tooltip: "Password", tooltipSub: "Password required for joining a private pix.");
        if(canEdit) {
            if(props.Privacy == PixPrivacy.Private) {
                changed = false;
                if(state != UIState.None) props.SecretKey = string.IsNullOrWhiteSpace(secretKey) ? null : secretKey;
            } else {
                props.SecretKey = null;
            }
        }

        changed |= ImGuiEx.EnumCombo("##editRank", string.Empty, ref props.EditorRank, ComboButtonDisplayType.Items, disabled: !canEdit, width: 80f * scale, tooltip: "Editor Rank", tooltipSub: "The minimum rank required for a user to make changes to synced properties.");
        ImGuiEx.SpacingX(Spacing, true, true);
        changed |= ImGuiEx.Checkbox("Nsfw##nsfw", ref props.Nsfw, disabled: !canEdit, tooltip: "Nsfw", tooltipSub: "Whether this pix may feature mature content.", size: LineHeight);

        ImGuiEx.Separator(region.X - Spacing);

        if(canEdit && !NameUtil.ValidatePix(SelectedPix!.Info.Name, SelectedPix.Info.Description, props.SecretKey, props.Privacy, SyncService.Client.Premium, out var err)) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText(err, animationType: AnimationType.Pulse, colorA: new Vector3(0.6f, 0, 0), colorB: new Vector3(1f, 0, 0), glowStrength: 0.1f, bgOpacity: 0.4f);
            }
            ImGuiEx.Separator(region.X - Spacing);
        } else if(SelectedPix!.Sync.IsSynced && !canEdit) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText($"You do not have editing permissions for this synced pix.", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
                ImGuiEx.StyledText($"You can override properties: Browser, Renderer, Light, Audio", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
                ImGuiEx.StyledText($"Overridden properties can be resynced by right-clicking them.", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
            ImGuiEx.Separator(region.X - Spacing);
        }

        if((state == UIState.Ended || changed) && canEdit) {
            if(NameUtil.ValidatePix(SelectedPix!.Info.Name, SelectedPix.Info.Description, props.SecretKey, props.Privacy, SyncService.Client.Premium, out _)) {
                syncBinding.Commit(props, true);
                PixService.UpdateSyncProperties(SelectedPix, true);
            }
        }

        var syncedPix = SelectedPix as SyncedPix;
        if(!SelectedPix.Sync.IsSynced) {
            if(ImGuiEx.IconTextButton(FontAwesomeIcon.SyncAlt, "Sync Pix", "##syncButton", disabled: !SyncService.IsConnectedAuth, tooltip: "Sync Pix", tooltipSub: "Upload this pix to the Sync Service")) {
                if(NameUtil.ValidatePix(SelectedPix!.Info.Name, SelectedPix.Info.Description, props.SecretKey, props.Privacy, SyncService.Client.Premium, out err)) {
                    SyncService.CreateSyncedPix(SelectedPix, SelectedPix.GetSyncedMetaData());
                } else {
                    StatusBar.Show(err!, 4000, statusType: Events.StatusType.Error);
                }
            }
        } else if(syncedPix != null) {
            if(syncedPix.SelfRank == PixRank.Owner) {
                if(ImGuiEx.IconTextButton(FontAwesomeIcon.TrashAlt, "Unsync Pix", "##syncDeleteButton", disabled: !SyncService.IsConnectedAuth, tooltip: "Unsync Pix", tooltipSub: "Remove this pix from the Sync Service")) {
                    SyncService.DeleteSyncedPix(syncedPix.Id);
                }
            } else {
                if(ImGuiEx.IconTextButton(FontAwesomeIcon.Unlink, "Leave Pix", "##syncLeaveButton", disabled: !SyncService.IsConnectedAuth, tooltip: "Leave Pix", tooltipSub: "Unsubscribe from this pix")) {
                    SyncService.UnsubscribePix(syncedPix.Id);
                }
            }
        }

        if(!SyncService.IsConnectedAuth) {
            var syncStatus = SyncService.State != ConnectionState.Connected ? "Disconnected" : !SyncService.Client.IsAuthenticated ? "Authentication Required" : "Unavailable";
            StatusBar.Show($"Sync Service: {syncStatus}", 100, statusType: Events.StatusType.Error);
        }
    }

    private void DrawSyncOverrideContext<T>(PixFieldBinding<T> binding, string id) {
        if(SelectedPix == null) return;
        if(!SyncService.IsConnectedAuth) return;
        var isContextOpen = SyncOverrideContextMenu?.IsOpen(id) ?? false;
        if(ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
            var items = new List<ContextMenuItem>();
            if(binding.HasOverride) {
                items.Add(new ContextMenuButton("Resync", icon: FontAwesomeIcon.Link, onClick: () => { binding.ResetOverride(true); }, tooltip: () => ("Resync Property", "Resets overridden property to the synced value.")));
            } else if(binding.CanSyncEdit) {
                items.Add(new ContextMenuButton("Sync Origin", icon: FontAwesomeIcon.Link, isDisabled: () => { return true; }, tooltip: () => ("Sync Origin", "You have editing permissions for this pix.\nChanges will be synced to other connected users.")));
            } else {
                items.Add(new ContextMenuButton("Synced", icon: FontAwesomeIcon.Link, isDisabled: () => { return true; }, tooltip: () => ("Synced Property", "This property is currently synced, changes will override & desync until resynced.")));
            }

            SyncOverrideContextMenu = new ContextMenu(id, items, width: 100f, itemHeight: 26f);
            SyncOverrideContextMenu.Open(id);
        }
        if(isContextOpen) { SyncOverrideContextMenu?.Draw(id); }
    }
}
