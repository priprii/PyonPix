using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Services.Game;
using PyonPix.Shared.Extensions;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync.Dto.Syncable;
using PyonPix.Shared.Utility;
using PyonPix.Ui.Components;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class SyncSearchWindow : BaseWindow {
    private SyncService SyncService => Services.Get<SyncService>();
    private PixService PixService => Services.Get<PixService>();
    private StateService StateService => Services.Get<StateService>();

    protected override WindowState State => Config.UI.SyncSearch.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.SyncSearch.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(460, 220);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;
    protected override bool ShowTitleBarSettingsButton => false;

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.SyncSearch.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.SyncSearch.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnCloseClicked() => IsOpen = false;
    public override void OnOpen() {
        base.OnOpen();
        Config.UI.SyncSearch.IsOpen = true;
        Config.Save();
        SyncService.QuerySyncablePixs();
    }
    public override void OnClose() {
        base.OnClose();
        Config.UI.SyncSearch.IsOpen = false;
        Config.Save();
    }

    private float RowHeight => 92f * ImGuiHelpers.GlobalScale;
    private float IconSize => 16f * ImGuiHelpers.GlobalScale;
    private float HorizontalPadding => 8f * ImGuiHelpers.GlobalScale;
    private float VerticalPadding => 8f * ImGuiHelpers.GlobalScale;
    private float Spacing => 6f * ImGuiHelpers.GlobalScale;

    private string Search = string.Empty;

    private string JoinPixId = string.Empty;
    private string JoinPixPass = string.Empty;

    private string? StatusMessage;

    private ContextMenu? FilterCategoryContextMenu = null;
    private ContextMenu? FilterWorldContextMenu = null;

    public SyncSearchWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} Sync Search###{Plugin.Name}SyncSearch", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(520, 440) * ImGuiHelpers.GlobalScale;

        SyncService.SyncablePixsUpdated += () => { StatusMessage = null; };
        SyncService.SubscriptionFailed += reason => { StatusMessage = reason; };
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(!IsOpen) return;

        DrawHeader();

        ImGuiEx.Separator(ImGui.GetContentRegionAvail().X);

        DrawRows();

        if(!SyncService.IsConnectedAuth) {
            var syncStatus = SyncService.State != ConnectionState.Connected ? "Disconnected" : !SyncService.Client.IsAuthenticated ? "Authentication Required" : "Unavailable";
            StatusBar.Show($"Sync Service: {syncStatus}", 100, statusType: Events.StatusType.Error);
        } else if(!string.IsNullOrWhiteSpace(StatusMessage)) {
            StatusBar.Show(StatusMessage, 4000, statusType: Events.StatusType.Error);
            StatusMessage = null;
        }
    }

    private void DrawHeader() {
        var syncConfig = Config.UI.SyncSearch;
        var draw = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var contentMin = new Vector2(cursorPos.X + WindowPadding.X, cursorPos.Y);
        var contentMax = contentMin + ImGui.GetContentRegionAvail() - WindowPadding;
        ImGui.SetCursorScreenPos(new(cursorPos.X + WindowPadding.X, cursorPos.Y));

        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGuiEx.StyledText("Join via Pix Id/Password");

        var joinButtonWidth = UiUtil.CalcIconTextSize(FontAwesomeIcon.Plus, "Join").X;
        var joinInputWidth = (availWidth - IndentWidth - (ItemSpacing * 2) - joinButtonWidth) * 0.5f;

        cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new(cursorPos.X + IndentWidth, cursorPos.Y));
        ImGuiEx.StyledInput("##joinId", ref JoinPixId, "Pix Id", maxLength: NameUtil.PixIdLength, width: joinInputWidth);
        ImGui.SameLine(0, ItemSpacing);
        ImGuiEx.StyledInput("##joinPass", ref JoinPixPass, "Password", maxLength: NameUtil.PixPassMaxLength, width: joinInputWidth);

        ImGui.SetCursorScreenPos(new(cursorPos.X + availWidth - joinButtonWidth, cursorPos.Y));
        if(ImGuiEx.IconTextButton(FontAwesomeIcon.Plus, "Join", "##joinButton", !SyncService.IsConnectedAuth, "Join Pix", $"Attempt to subscribe to specified Synced Pix.\nA Synced Pix Id has '{NameUtil.PixIdSyncedPrefix}' prefix like: '{NameUtil.PixIdSyncedPrefix}?????????'\nPassword is only required if the Pix is private.", height: LineHeight)) {
            StatusMessage = null;
            if(NameUtil.ValidateSyncedPixId(JoinPixId, out StatusMessage)){
                SyncService.SubscribePix(JoinPixId, JoinPixPass);
            }
        }

        ImGuiEx.Separator(ImGui.GetContentRegionAvail().X);

        cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new(cursorPos.X + WindowPadding.X, cursorPos.Y));

        // Filters
        var filterTotalWidth = ((LineHeight + ItemSpacing) * 5) + ItemSpacing;
        var refreshButtonWidth = UiUtil.CalcIconTextSize(FontAwesomeIcon.ArrowsSpin, "Refresh").X;

        ImGui.SetCursorScreenPos(cursorPos + new Vector2(WindowPadding.X, 0));

        ImGuiEx.StyledInput("##mew", ref Search, "Search..", width: availWidth - filterTotalWidth - refreshButtonWidth - ItemSpacing - WindowPadding.X, labelIcon: FontAwesomeIcon.Search);

        ImGui.SameLine(0, ItemSpacing);
        cursorPos = ImGui.GetCursorScreenPos();
        draw.AddRectFilled(cursorPos, cursorPos + new Vector2(filterTotalWidth, LineHeight), UIShared.IconTextBgNormal.ToU32(), 6f);

        ImGui.SetCursorScreenPos(new(cursorPos.X + ItemSpacing, cursorPos.Y));
        var hasFilters = syncConfig.TypeFilters.Count != 0 || syncConfig.WorldFilters.Count != 0 || syncConfig.SameTerritoryOnly;
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.TimesCircle, "##filterNone", false, !hasFilters, size: LineHeight, tooltip: "Clear Filters")) {
            syncConfig.TypeFilters.Clear();
            syncConfig.WorldFilters.Clear();
            syncConfig.SameTerritoryOnly = false;
            Config.Save();
        }
        ImGui.SameLine(0, ItemSpacing);

        var isClicked = ImGuiEx.IconToggleButton(FontAwesomeIcon.Database, "##filterCategory", syncConfig.TypeFilters.Count != 0, size: LineHeight, tooltip: "Filter Category");
        DrawFilterCategoryContextMenu(isClicked);

        ImGui.SameLine(0, ItemSpacing);

        isClicked = ImGuiEx.IconToggleButton(FontAwesomeIcon.Globe, "##filterWorld", syncConfig.WorldFilters.Count != 0, size: LineHeight, tooltip: "Filter World");
        DrawFilterWorldContextMenu(isClicked);

        ImGui.SameLine(0, ItemSpacing);
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.MapMarkerAlt, "##filterTerritory", syncConfig.SameTerritoryOnly, size: LineHeight, tooltip: "Show Current Territory Only")) {
            syncConfig.SameTerritoryOnly = !syncConfig.SameTerritoryOnly;
            Config.Save();
        }
        ImGui.SameLine(0, ItemSpacing);
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Fire, "##filterNsfw", syncConfig.ShowNsfw, size: LineHeight, tooltip: "Show Nsfw")) {
            syncConfig.ShowNsfw = !syncConfig.ShowNsfw;
            Config.Save();
        }

        ImGui.SameLine(0, ItemSpacing);

        ImGui.SetCursorScreenPos(new(contentMax.X - refreshButtonWidth, cursorPos.Y));
        if(ImGuiEx.IconTextButton(FontAwesomeIcon.ArrowsSpin, "Refresh", "##refreshButton", !SyncService.IsConnectedAuth, height: LineHeight)) {
            SyncService.QuerySyncablePixs();
        }
    }

    private void DrawFilterCategoryContextMenu(bool isClicked) {
        var syncConfig = Config.UI.SyncSearch;
        var isContextOpen = FilterCategoryContextMenu?.IsOpen() ?? false;
        if(isClicked) {
            var items = new List<ContextMenuItem>();
            items.Add(new ContextMenuCheckbox($"Any", () => syncConfig.TypeFilters.Count == 0, (x) => {
                if(x) {
                    syncConfig.TypeFilters.Clear();
                    Config.Save();
                }
            }));
            items.Add(new ContextMenuSeparator());
            foreach(var type in Enum.GetValues<PixType>()) {
                items.Add(new ContextMenuCheckbox($"{type}", () => syncConfig.TypeFilters.Contains(type), (x) => {
                    if(x) {
                        syncConfig.TypeFilters.Add(type);
                    } else {
                        syncConfig.TypeFilters.Remove(type);
                    }
                    Config.Save();
                }));
            }

            FilterCategoryContextMenu = new ContextMenu("##filterCategory", items, width: 100f, itemHeight: 26f);
            FilterCategoryContextMenu.Open();
        }
        if(isContextOpen) { FilterCategoryContextMenu?.Draw(); }
    }

    private void DrawFilterWorldContextMenu(bool isClicked) {
        var syncConfig = Config.UI.SyncSearch;
        var isContextOpen = FilterWorldContextMenu?.IsOpen() ?? false;
        if(isClicked) {
            var tabs = new List<ContextMenuTab>();

            foreach(var region in Enum.GetValues<Region>()) {
                var regionWorlds = StateService.Worlds[region];
                var items = new List<ContextMenuItem> {
                new ContextMenuCheckbox(
                    $"Any {region} World",
                    () => syncConfig.WorldFilters.Count == 0 || syncConfig.WorldFilters.Count(x => regionWorlds.Any(w => w.Id == x)) == regionWorlds.Count,
                    x => {
                        if(x) {
                            foreach(var w in regionWorlds)
                                syncConfig.WorldFilters.Add(w.Id);
                        } else {
                            foreach(var w in regionWorlds)
                                syncConfig.WorldFilters.Remove(w.Id);
                        }
                        Config.Save();
                    }),
                new ContextMenuSeparator()
            };

                foreach(var world in regionWorlds) {
                    items.Add(new ContextMenuCheckbox(
                        world.Name,
                        () => syncConfig.WorldFilters.Contains(world.Id),
                        x => {
                            if(x) syncConfig.WorldFilters.Add(world.Id); else syncConfig.WorldFilters.Remove(world.Id);
                            Config.Save();
                        }));
                }

                tabs.Add(new ContextMenuTab($"{region}", $"{region}", items));
            }

            FilterWorldContextMenu = new ContextMenu("##filterWorld", tabs, syncConfig.RegionActiveTabIndex, 240f, 26f, activeTabUpdated: (i) => {
                syncConfig.RegionActiveTabIndex = i;
                Config.Save();
            });
            FilterWorldContextMenu.Open();
        }

        if(isContextOpen) {
            FilterWorldContextMenu?.Draw();
        }
    }

    private void DrawRows() {
        var all = SyncService.SyncablePixs;
        var filtered = all.Where(MatchesFilter).ToList();

        using(UIShared.SubFont.Push()) {
            ImGuiEx.StyledText($" Listing {filtered.Count}/{all.Count} Results", colorA: UIShared.Muted.AsVector3());
        }

        ImGui.BeginChild("##syncRows", ImGui.GetContentRegionAvail(), false);
        foreach(var item in filtered) {
            DrawRow(item);
        }
        ImGui.EndChild();
    }

    private bool MatchesFilter(SyncablePixQueryItemDto item) {
        var syncConfig = Config.UI.SyncSearch;
        if(StateService.CurrentTerritory == null) return false;
        if(!syncConfig.ShowNsfw && item.Nsfw) return false;

        if(syncConfig.TypeFilters.Count > 0 && !syncConfig.TypeFilters.Contains(item.PixType))
            return false;

        if(syncConfig.SameTerritoryOnly) {
            if(item.Territory.WorldId != (short)StateService.CurrentTerritory.WorldId || item.Territory.TerritoryId != (short)StateService.CurrentTerritory.TerritoryId)
                return false;
        } else if(syncConfig.WorldFilters.Count > 0 && !syncConfig.WorldFilters.Contains((ushort)item.Territory.WorldId)) {
            return false;
        }

        if(!string.IsNullOrWhiteSpace(Search)) {
            var q = Search.Trim();
            if(!item.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !item.OwnerAlias.Contains(q, StringComparison.OrdinalIgnoreCase) && !item.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private void DrawRow(SyncablePixQueryItemDto item) {
        ImGui.PushID(item.PixId);

        float width = ImGui.GetContentRegionAvail().X;
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        Vector2 rowMax = rowMin + new Vector2(width, RowHeight);

        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
        if(hovered) {
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ItemBgHovered));
        }

        var isSubscribed = PixService.IsSubscribed(item.PixId);

        var actionX = rowMax.X - HorizontalPadding - IconSize;
        var actionY = rowMin.Y + ((RowHeight - IconSize) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(actionX, actionY));
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Link, "##toggleSubscribe", isSubscribed, !SyncService.IsConnectedAuth, isSubscribed ? "Unsubscribe" : "Subscribe", size: IconSize, toggledIcon: FontAwesomeIcon.Unlink)) {
            StatusMessage = null;
            if(isSubscribed) {
                SyncService.UnsubscribePix(item.PixId);
            } else {
                SyncService.SubscribePix(item.PixId, null);
            }
        }

        using(UIShared.SubFont.Push()) {
            var privacyText = item.Privacy.ToString().ToUpperInvariant();
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - HorizontalPadding - ImGui.CalcTextSize(privacyText).X, rowMin.Y + VerticalPadding));
            ImGuiEx.StyledText(privacyText, colorA: UIShared.Muted.AsVector3());

            if(item.Nsfw) {
                var nsfwText = "NSFW";
                ImGui.SetCursorScreenPos(new Vector2(rowMax.X - HorizontalPadding - ImGui.CalcTextSize(privacyText).X, rowMax.Y - VerticalPadding - ImGui.GetFontSize()));
                ImGuiEx.StyledText(nsfwText, colorA: UIShared.Muted.AsVector3());
            }
        }

        float textLeft = rowMin.X + HorizontalPadding;
        float textRight = actionX - Spacing;
        ImGui.PushClipRect(new Vector2(textLeft, rowMin.Y), new Vector2(textRight, rowMax.Y), true);

        var title = string.IsNullOrWhiteSpace(item.Name) ? item.PixId : item.Name;
        var titleColour = isSubscribed ? UIShared.AccentActive : UIShared.ItemHeader;
        using(UIShared.NormalFont.Push()) {
            ImGui.SetCursorScreenPos(new Vector2(textLeft, rowMin.Y + VerticalPadding));
            ImGuiEx.IconLabel(UiUtil.GetIconForPixType(item.PixType), $"##pixType{item.OwnerId}", color: UIShared.PixTypeSynced, size: UIShared.NormalFontSize, iconScale: 0.7f);
            ImGui.SetCursorScreenPos(new Vector2(textLeft + UIShared.NormalFontSize, rowMin.Y + VerticalPadding));
            ImGuiEx.StyledText(title, animationType: item.OwnerPixStyle?.AnimationType ?? default, colorA: item.OwnerPixStyle?.ColourA?.ToVector3(), colorB: item.OwnerPixStyle?.ColourB?.ToVector3(), glowA: item.OwnerPixStyle?.GlowA?.ToVector3(), glowB: item.OwnerPixStyle?.GlowB?.ToVector3());
        }

        using(UIShared.SubFont.Push()) {
            ImGui.SetCursorScreenPos(new Vector2(textLeft, rowMin.Y + VerticalPadding + UIShared.SubFontSize));
            ImGuiEx.IconLabel(FontAwesomeIcon.Crown, $"##rank{item.OwnerId}", "Owner", color: UIShared.PixRankOwner, size: UIShared.SubFontSize, iconScale: 0.7f);
            ImGui.SetCursorScreenPos(new Vector2(textLeft + UIShared.NormalFontSize, rowMin.Y + VerticalPadding + UIShared.SubFontSize));
            ImGuiEx.StyledText(item.OwnerAlias, animationType: item.OwnerAliasStyle?.AnimationType ?? default, colorA: item.OwnerAliasStyle?.ColourA?.ToVector3(), colorB: item.OwnerAliasStyle?.ColourB?.ToVector3(), glowA: item.OwnerAliasStyle?.GlowA?.ToVector3(), glowB: item.OwnerAliasStyle?.GlowB?.ToVector3());
        }

        using(UIShared.SubFont.Push()) {
            var desc = string.IsNullOrWhiteSpace(item.Description) ? "No description" : item.Description;
            ImGui.SetCursorScreenPos(new Vector2(textLeft, rowMin.Y + VerticalPadding + (ImGui.GetFontSize() * 2f) + 2f));
            ImGuiEx.StyledText(desc, tooltip: desc, colorA: UIShared.Dimmed.AsVector3());

            var uriText = string.IsNullOrWhiteSpace(item.Uri) ? "about:blank" : item.Uri;
            ImGui.SetCursorScreenPos(new Vector2(textLeft, rowMin.Y + VerticalPadding + (ImGui.GetFontSize() * 3f) + 5f));
            ImGuiEx.StyledText(uriText, tooltip: uriText, colorA: UIShared.Muted.AsVector3());

            var worldName = StateService.GetWorldName((uint)item.Territory.WorldId);
            var territoryName = StateService.GetTerritoryName((ushort)item.Territory.TerritoryId);
            var residence = BuildResidence(item.Territory.Ward, item.Territory.Plot, item.Territory.Room);
            var terrText = $"{worldName} - {territoryName} {residence}".Trim();
            ImGui.SetCursorScreenPos(new Vector2(textLeft, rowMin.Y + VerticalPadding + (ImGui.GetFontSize() * 4f) + 8f));
            ImGuiEx.StyledText(terrText, colorA: UIShared.Normal.AsVector3());
        }

        ImGui.PopClipRect();

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y + Spacing));
        ImGui.PopID();
    }

    private static string BuildResidence(short ward, short plot, short room) {
        string text = string.Empty;
        if(ward > 0) text += $"W{ward}";
        if(plot > 0) text += $" P{plot}";
        if(room > 0) text += $" R{room}";
        return text;
    }
}
