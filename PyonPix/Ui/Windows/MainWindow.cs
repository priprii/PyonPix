using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Config.Pix;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Game;
using PyonPix.Services.Core;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Structs.Territory;
using PyonPix.Structs.Browser;
using PyonPix.Ui.Components;
using PyonPix.Utility;
using PyonPix.Shared.Sync.Dto.Client;
using PyonPix.Events;
using PyonPix.Shared.Extensions;

namespace PyonPix.Ui.Windows;

public class MainWindow : BaseWindow {
    private PixService PixService => Services.Get<PixService>();
    private SyncService SyncService => Services.Get<SyncService>();
    private StateService StateService => Services.Get<StateService>();
    private BrowserService BrowserService => Services.Get<BrowserService>();

    protected override WindowState State => Config.UI.Main.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.Main.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(300, 250);
    protected override Vector2 ExpandedMaxSize => new Vector2(300, UiUtil.GameHeight);

    public override void OnOpen() {
        base.OnOpen();
        Config.UI.Main.IsOpen = true;
        Config.Save();
    }
    public override void OnClose() {
        base.OnClose();
        Config.UI.Main.IsOpen = false;
        Config.Save();
    }

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.Main.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        Config.UI.Main.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnConfigClicked() {
        Windows.Get<ConfigWindow>().Toggle();
    }
    protected override void OnCloseClicked() {
        IsOpen = false;
    }

    protected override float DrawControlExtras(float rightCursor) {
        if(!IsOpen) return rightCursor;

        rightCursor -= TitleBarFrameHeight;
        var btnPos = new Vector2(rightCursor, HeaderMin.Y);
        ImGui.SetCursorScreenPos(btnPos);
        var updatesWindow = Windows.Get<UpdatesWindow>();
        var uText = updatesWindow.IsOpen ? $"Close {Plugin.Name} Changelog" : $"Open {Plugin.Name} Changelog";
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.InfoCircle, "##updates", updatesWindow.IsOpen, size: TitleBarFrameHeight, iconScale: 0.8f, tooltip: uText)) {
            updatesWindow.Toggle();
        }

        rightCursor -= TitleBarFrameHeight;
        btnPos = new Vector2(rightCursor, HeaderMin.Y);
        ImGui.SetCursorScreenPos(btnPos);
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Heart, "##kofi", true, size: TitleBarFrameHeight, iconScale: 0.8f, tooltip: "Support me on Ko-fi!")) {
            UiUtil.OpenKofi();
        }

        rightCursor -= TitleBarFrameHeight;
        btnPos = new Vector2(rightCursor, HeaderMin.Y);
        ImGui.SetCursorScreenPos(btnPos);
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Star, "##discord", true, size: TitleBarFrameHeight, iconScale: 0.8f, tooltip: "Join the Pyon Discord!")) {
            UiUtil.OpenDiscord();
        }

        return rightCursor;
    }

    private ContextMenu? PixContextMenu = null;

    public MainWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} {Plugin.Version}###{Plugin.Name}Main", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(300, 450) * ImGuiHelpers.GlobalScale;

        SyncService.StateChanged += (connectionState, statusMessage, statusType) => {
            if(statusType == StatusType.None) return;
            if(statusType == StatusType.Hide) { StatusBar.Hide(); return; }
            StatusBar.Show($"{statusMessage}", 8000, true, statusType: statusType);
        };
    }

    public override void Draw() => base.Draw();
    protected override void DrawContent() {
        if(!IsOpen) return;

        DrawHeader();

        ImGuiEx.Separator(ImGui.GetContentRegionAvail().X, 0);

        DrawPixTree();
    }

    private void DrawHeader() {
        var draw = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var headerPadding = 4f * scale;
        var spacing = 4f * scale;
        var padding = 6f * scale;
        var iconSize = 18f * scale;

        var syncStateText = SyncService.State switch {
            ConnectionState.Disconnected => "offline",
            ConnectionState.Connecting => "syncing",
            ConnectionState.Connected => "online",
            _ => string.Empty
        };
        var syncStateSize = UiUtil.CalcTextSize(UIShared.SubFont, syncStateText) + (UIShared.TextBgPadding * 2);

        // Header height and regions
        float headerHeight = iconSize + syncStateSize.Y + (headerPadding * 2) + spacing;
        var headerMin = ImGui.GetCursorScreenPos();
        var headerMax = headerMin + new Vector2(ImGui.GetContentRegionAvail().X, headerHeight);
        var headerSize = headerMax - headerMin;
        var headerCenterY = headerMin.Y + (headerSize.Y * 0.5f);

        float leftWidth = (iconSize * 2) + (padding * 2f) + spacing;
        var leftCenterX = headerMin.X + (leftWidth * 0.5f);
        //draw.AddLine(new(leftCenterX, headerMin.Y), new(leftCenterX, headerMax.Y), ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
        float rightWidth = (iconSize * 3f) + (spacing * 2f) + (padding * 2f);
        var rightMinX = headerMax.X - rightWidth + padding;
        var midMinX = headerMin.X + leftWidth;
        var midMaxX = headerMax.X - rightWidth;
        var midCenterX = midMinX + ((midMaxX - midMinX) * 0.5f);
        var iconsTop = headerMin.Y + headerPadding;

        // Left
        var syncX = headerMin.X + padding;
        ImGui.SetCursorScreenPos(new Vector2(headerMin.X + padding, iconsTop));
        var playerExists = StateService.LocalPlayerExists;
        var isConnected = SyncService.State == ConnectionState.Connected;
        var isConnecting = SyncService.State == ConnectionState.Connecting;
        var connCol = isConnected ? new Vector3(0, 0.8f, 0) : isConnecting ? new Vector3(0.8f, 0.8f, 0) : new Vector3(0.8f, 0, 0f);
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Link, "##syncConnect", isConnected || isConnecting, !playerExists && !isConnected, toggledIcon: FontAwesomeIcon.Unlink, tooltip: isConnected ? "Disconnect from Sync Service" : "Connect to Sync Service", size: iconSize)) {
            if(isConnected) {
                SyncService.Disconnect();
                Config.Sync.AutoConnect = false;
            } else if(isConnecting) {
                SyncService.AbortConnection();
                Config.Sync.AutoConnect = false;
            } else {
                SyncService.Connect();
                Config.Sync.AutoConnect = true;
            }
            Config.Save();
        }

        ImGui.SetCursorScreenPos(new Vector2(syncX + iconSize + spacing, iconsTop));
        var syncWindow = Windows.Get<SyncSearchWindow>();
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Search, "##openSyncWindow", syncWindow.IsOpen, !StateService.LocalPlayerExists || !isConnected, tooltip: syncWindow.IsOpen ? "Close Sync Search" : "Open Sync Search", size: iconSize)) {
            syncWindow.Toggle();
        }

        // Connection State
        using(UIShared.SubFont.Push()) {
            var textPos = new Vector2(leftCenterX - (syncStateSize.X * 0.5f), iconsTop + iconSize + spacing);
            ImGui.SetCursorScreenPos(textPos);
            ImGuiEx.StyledText(syncStateText, colorA: connCol, glowStrength: 0.1f, bgOpacity: 0.3f);
        }


        // Middle
        //draw.AddRect(new(midMinX, headerMin.Y), new(midMaxX, headerMax.Y), ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
        ImGui.SetCursorScreenPos(new Vector2(midMinX, headerMin.Y + headerHeight * 0.1f));
        if(SyncService.State == ConnectionState.Connected && !SyncService.Client.IsAuthenticated && !string.IsNullOrEmpty(SyncService.Client.AuthKey)) {
            using(UIShared.SubFont.Push()) {
                ImGui.SetCursorScreenPos(new Vector2(midMinX + padding, headerMin.Y + padding));
                var authKeyText = "AuthKey:";
                var authKeyTextWidth = ImGui.CalcTextSize(authKeyText).X;
                ImGuiEx.StyledText(authKeyText);

                ImGui.SetCursorScreenPos(new Vector2(midMinX + padding + authKeyTextWidth + spacing, headerMin.Y + padding));
                ImGuiEx.StyledText(SyncService.Client.AuthKey, animationType: AnimationType.RainbowWave, tooltip: "PyonPix Sync Service Registration", tooltipSub: "- Click this key to copy it.\n- Go to the Pyon Discord server (if you have not yet joined, click the star above)\n- Check #pyonpix channel for registration form.", action: new(() => { if(!string.IsNullOrEmpty(SyncService.Client.AuthKey)) ImGui.SetClipboardText(SyncService.Client.AuthKey); }));

                var expText = $"Expires in {SyncService.Client.GetAuthExpirationTime()}";
                var expPos = new Vector2(midMinX + padding, iconsTop + iconSize + spacing);
                ImGui.SetCursorScreenPos(expPos);
                ImGuiEx.StyledText(expText, glowStrength: 0.1f, colorA: new Vector3(0.8f, 0, 0f), bgOpacity: 0.3f);
            }
        } else if(SyncService.State == ConnectionState.Connected) {
            using(UIShared.SubFont.Push()) {
                ImGui.SetCursorScreenPos(new Vector2(midMinX + padding, headerMin.Y + padding));
                var userWindow = Windows.Get<UserWindow>();
                if(ImGuiEx.IconToggleButton(FontAwesomeIcon.UserEdit, "##userWindow", userWindow.IsOpen, iconScale: 0.7f, tooltip: userWindow.IsOpen ? "Close User Config" : "Open User Config")) {
                    userWindow.Toggle();
                }
                ImGui.SameLine(0, 0);
                var style = SyncService.Client.Style;
                var alias = style.Alias;
                var aliasSize = ImGui.CalcTextSize(alias);
                var aliasWidth = rightMinX - midMinX - iconSize;
                if(SyncService.Client.Premium.IsSubscriber) {
                    ImGuiEx.StyledText(alias, wrapWidth: aliasWidth, animationType: style.AliasAnimationType, colorA: style.AliasColourA, colorB: style.AliasColourB, glowA: style.AliasGlowA, glowB: style.AliasGlowB);
                } else {
                    ImGuiEx.StyledText(alias, wrapWidth: aliasWidth);
                }

                var usersText = $"{SyncService.Server.UserCount} users";
                var usersWidth = ImGui.CalcTextSize(usersText).X + (UIShared.TextBgPadding.X * 2);
                ImGui.SetCursorScreenPos(new Vector2(midMinX + padding, iconsTop + iconSize + spacing));
                ImGuiEx.StyledText(usersText, colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);

                var pixText = $"{SyncService.Server.PixCount} pixs";
                ImGui.SetCursorScreenPos(new Vector2(midMinX + padding + usersWidth + spacing, iconsTop + iconSize + spacing));
                ImGuiEx.StyledText(pixText, colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
        } else if(!string.IsNullOrEmpty(SyncService.StatusMessage)) {
            using(UIShared.SubFont.Push()) {
                var txt = SyncService.StatusMessage;
                var txtSize = ImGui.CalcTextSize(txt);

                if(SyncService.Client.IsSecretKeyInvalid) {
                    var icon = FontAwesomeIcon.UserEdit;
                    var iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X * 0.7f;

                    ImGui.SetCursorScreenPos(new Vector2(midCenterX - (txtSize.X * 0.5f) - padding - iconWidth, headerCenterY - (txtSize.Y * 0.5f)));
                    var userWindow = Windows.Get<UserWindow>();
                    if(ImGuiEx.IconToggleButton(icon, "##userWindow", userWindow.IsOpen, iconScale: 0.7f, tooltip: userWindow.IsOpen ? "Close User Config" : "Open User Config")) {
                        userWindow.Toggle();
                    }
                }

                ImGui.SetCursorScreenPos(new Vector2(midCenterX - (txtSize.X * 0.5f), headerCenterY - (txtSize.Y * 0.5f)));
                ImGuiEx.StyledText(txt, animationType: AnimationType.Pulse, colorA: new Vector3(connCol.X - 0.2f, connCol.Y - 0.2f, 0), colorB: new Vector3(connCol.X + 0.2f, connCol.Y + 0.2f, 0), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
        }

        // Right
        var rightY = headerMin.Y + ((headerHeight - iconSize) * 0.5f);
        var rightX = headerMax.X - padding;
        ImGui.SetCursorScreenPos(new Vector2(rightX - iconSize, rightY));
        if(ImGuiEx.IconButton(FontAwesomeIcon.Plus, "##addHeader", false, tooltip: "Create Pix", size: iconSize)) {
            var pix = PixService.CreateLocalPix();
            Windows.Get<PixConfigWindow>().Toggle(pix);

            var territoryKey = pix.Territory.ToString();
            if(!Config.UI.Main.ExpandedTerritories.Contains(territoryKey)) {
                Config.UI.Main.ExpandedTerritories.Add(territoryKey);
                Config.Save();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(rightX - (iconSize * 2) - spacing, rightY));
        if(ImGuiEx.IconButton(FontAwesomeIcon.Paste, "##pasteHeader", false, tooltip: "Paste Pix", size: iconSize)) {
            var pix = PixService.PastePixFromClipboard();
            Windows.Get<PixConfigWindow>().Toggle(pix);
        }
        
        ImGui.SetCursorScreenPos(new Vector2(rightX - (iconSize * 3) - (spacing * 2), rightY));
        var browser = Windows.Get<BrowserWindow>();
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Globe, "##browserHeader", browser.IsOpen, size: iconSize, tooltip: browser.IsOpen ? "Hide Browser" : "Show Browser")) {
            browser.Toggle();
        }

        ImGui.SetCursorScreenPos(headerMin + new Vector2(0, headerSize.Y));
    }

    private void DrawPixTree() {
        ImGui.BeginChild($"PixTree", ImGui.GetContentRegionAvail(), false);

        var currentTerritory = StateService.CurrentTerritory;
        if(currentTerritory != null) {
            DrawTerritoryRow(currentTerritory, true);
        }

        var territories = PixService.GetPixTerritories();
        foreach(var territory in territories) {
            if(territory.MatchesWTWP(currentTerritory)) continue;
            DrawTerritoryRow(territory, false);
        }

        ImGui.EndChild();
    }

    private void DrawTerritoryRow(TerritoryData t, bool isCurrentTerritory) {
        var territoryKey = t.ToString();

        ImGui.PushID(territoryKey);

        float scale = ImGuiHelpers.GlobalScale;
        float horizontalPadding = 8f * scale;
        float verticalPadding = 6f * scale;
        float spacing = 4f * scale;
        float iconSize = 18f * scale;

        string worldLine = $"{t.WorldName} {StateService.GetResidenceFormatted(t)}".Trim();
        var territoryName = t.TerritoryName;
        var territorySubName = t.TerritorySubName;
        var territoryLine = string.IsNullOrEmpty(territorySubName) ? territoryName : $"{territoryName} - {territorySubName}";

        Vector2 worldSize;
        Vector2 territorySize;
        using(UIShared.NormalFont.Push())
            worldSize = ImGui.CalcTextSize(worldLine);
        using(UIShared.SubFont.Push())
            territorySize = ImGui.CalcTextSize(territoryLine);

        float textBlockHeight = worldSize.Y + spacing + territorySize.Y;
        float rowHeight = textBlockHeight + (verticalPadding * 2);
        rowHeight = MathF.Max(rowHeight, iconSize + (verticalPadding * 2));

        Vector2 rowStart = ImGui.GetCursorScreenPos();
        float rowWidth = ImGui.GetContentRegionAvail().X;

        Vector2 rowMin = rowStart;
        Vector2 rowMax = rowStart + new Vector2(rowWidth, rowHeight);

        float caretY = rowMin.Y + ((rowHeight - iconSize) * 0.5f);
        Vector2 caretPos = new Vector2(rowMin.X + horizontalPadding, caretY);

        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
        bool mouseInnerRow = ImGui.IsMouseHoveringRect(new(caretPos.X + iconSize, rowMin.Y), new(rowMax.X - spacing, rowMax.Y));
        bool rowClicked = hovered && mouseInnerRow && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool isExpanded = Config.UI.Main.ExpandedTerritories.Contains(territoryKey);
        var col = rowClicked ? UIShared.PixTerritoryBgActive : isExpanded && hovered ? UIShared.PixTerritoryBgExpandedHovered : isExpanded ? UIShared.PixTerritoryBgExpanded : hovered ? UIShared.PixTerritoryBgHovered : UIShared.PixTerritoryBgNormal;
        ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(col));

        ImGui.SetCursorScreenPos(caretPos);
        
        if(rowClicked || ImGuiEx.IconButton(isExpanded ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight, $"##expand", size: iconSize)) {
            if(isExpanded) {
                Config.UI.Main.ExpandedTerritories.Remove(territoryKey);
            } else {
                Config.UI.Main.ExpandedTerritories.Add(territoryKey);
            }
            Config.Save();
        }

        float textLeft = rowMin.X + horizontalPadding + iconSize + spacing;
        float textRight = rowMax.X - spacing;
        ImGui.PushClipRect(new Vector2(textLeft, rowMin.Y), new Vector2(textRight, rowMax.Y), true);
        float textStartY = rowMin.Y + ((rowHeight - textBlockHeight) * 0.5f);
        using(UIShared.NormalFont.Push()) {
            var worldTextCol = isCurrentTerritory ? UIShared.AccentActive : UIShared.ItemSubText;
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textLeft, textStartY), ImGui.GetColorU32(worldTextCol), worldLine);
        }

        using(UIShared.SubFont.Push()) {
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textLeft, textStartY + worldSize.Y + spacing), ImGui.GetColorU32(UIShared.ItemSubText), territoryLine);
        }
        ImGui.PopClipRect();

        ImGui.PopID();

        if(isExpanded) {
            float indent = 20f * scale;
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));

            var pixs = PixService.GetOrderedPixsForTerritory(t, true);
            foreach(var pix in pixs) {
                DrawPixRow(pix, indent, isCurrentTerritory);
            }
        } else {
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));
        }
    }

    private void DrawPixRow(IPix pix, float indent, bool isCurrentTerritory) {
        ImGui.PushID(pix.Id);

        BrowserService.Tabs.TryGetValue(pix.Id, out var tab);

        var syncedPix = pix as SyncedPix;

        var scale = ImGuiHelpers.GlobalScale;
        var horizontalPadding = 8f * scale;
        var verticalPadding = 4f * scale;
        var spacing = 4f * scale;
        var iconSize = 16f * scale;

        var rowStart = ImGui.GetCursorScreenPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;

        var rowHeight = iconSize + (verticalPadding * 2);

        var rowMin = rowStart;
        var rowMax = rowStart + new Vector2(rowWidth, rowHeight);

        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
        if(hovered) {
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)));
        }

        var typeIcon = UiUtil.GetIconForPixType(pix.Info.Type);

        var iconY = rowMin.Y + ((rowHeight - iconSize) * 0.5f);
        var iconPos = new Vector2(rowMin.X + horizontalPadding + indent, iconY);

        ImGui.SetCursorScreenPos(iconPos);
        if(syncedPix == null) {
            ImGuiEx.IconLabel(typeIcon, "##icon", size: iconSize, hover: false, tooltip: $"Local {pix.Info.Type} ({pix.Id})");
        } else {
            ImGuiEx.IconLabel(typeIcon, "##icon", size: iconSize, hover: false, color: UIShared.PixTypeSynced, tooltip: $"Synced {pix.Info.Type} ({pix.Id})", tooltipSub: $"Owner: {syncedPix.OwnerAlias}\nPrivacy: {syncedPix.Sync.Privacy}");
        }

        var settingsX = rowMax.X - horizontalPadding - iconSize;
        var playX = settingsX - spacing - iconSize;

        var playPos = new Vector2(playX, iconY);
        var settingsPos = new Vector2(settingsX, iconY);

        var isSpawned = PixService.IsSpawned(pix);
        var isActive = PixService.IsActive(pix);
        var isDisabled = (BrowserService.State != BrowserState.Stopped && BrowserService.State != BrowserState.Running) || (tab != null && (tab.State != TabState.Ready || tab.NavState != NavigationState.Ready));
        ImGui.SetCursorScreenPos(playPos);
        if(ImGuiEx.IconToggleButton(isActive ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, "##pixToggle", isActive && isSpawned, isDisabled, size: iconSize,
            tooltip: isCurrentTerritory ? $"{(isSpawned ? "Despawn Pix" : "Spawn Pix")}" : $"{(isActive ? "Disable Pix" : "Enable Pix")}",
            tooltipSub: isCurrentTerritory ? string.Empty : "Toggle whether to spawn this Pix when you're in the same territory.")) {
            PixService.Toggle(pix);
        }

        var isContextOpen = PixContextMenu?.IsOpen() ?? false;
        ImGui.SetCursorScreenPos(settingsPos);
        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.EllipsisV, "##pixMenu", isContextOpen || Windows.Get<PixConfigWindow>().SelectedPix == pix, size: iconSize, tooltip: "Manage Pix")) {
            PixContextMenu = BuildContextMenu(pix);
            PixContextMenu.Open();
        }
        if(isContextOpen) PixContextMenu?.Draw(settingsPos + new Vector2(iconSize, 0));

        var textLeft = iconPos.X + iconSize + spacing;
        var textRight = playX - spacing;

        ImGui.PushClipRect(new Vector2(textLeft, rowMin.Y), new Vector2(textRight, rowMax.Y), true);
        var displayName = pix.GetDisplayName();
        var textSize = ImGui.CalcTextSize(displayName);
        var textY = rowMin.Y + ((rowHeight - textSize.Y) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(textLeft, textY));
        if(syncedPix == null) {
            var textCol = isCurrentTerritory ? UIShared.ItemHeader : UIShared.ItemInactive;
            ImGuiEx.StyledText(displayName, UIShared.NormalFontSize);
        } else {
            ImGuiEx.StyledText(displayName, UIShared.NormalFontSize, animationType: syncedPix.OwnerPixStyle?.AnimationType ?? default, colorA: syncedPix.OwnerPixStyle?.ColourA?.ToVector3(), colorB: syncedPix.OwnerPixStyle?.ColourB?.ToVector3(), glowA: syncedPix.OwnerPixStyle?.GlowA?.ToVector3(), glowB: syncedPix.OwnerPixStyle?.GlowB?.ToVector3());
        }
        ImGui.PopClipRect();

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));

        ImGui.PopID();
    }

    private ContextMenu BuildContextMenu(IPix? pix) {
        var items = new List<ContextMenuItem> {
            new ContextMenuButton("Pix Config", icon: FontAwesomeIcon.Cog,
                onClick: () => { Windows.Get<PixConfigWindow>().Toggle(pix); })
        };
        if(pix is not SyncedPix syncedPix) {
            items.Add(new ContextMenuButton("Copy Pix", icon: FontAwesomeIcon.Copy,
                onClick: () => { PixService.CopyPixToClipboard(pix); },
                tooltip: () => ("Copy Pix", "Copy this Pix to your clipboard to share with others.\nFor manual syncing, the receiver can copy the text & use the 'Paste Pix' button.\nNote: This does not copy any private browser data, only the Uri.")));
            items.Add(new ContextMenuButton("Remove Pix", icon: FontAwesomeIcon.Trash,
                onClick: () => {
                    if(ImGui.IsKeyDown(ImGuiKey.ModCtrl) && !PixService.IsSpawned(pix)) {
                        Windows.Get<PixConfigWindow>().Toggle(null);
                        PixService.DeleteLocalPix(pix);
                    }
                },
                isDisabled: () => PixService.IsSpawned(pix) || !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                tooltip: () => {
                    if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl) || PixService.IsSpawned(pix))
                        return ("Remove Pix", "Hold the Control key to confirm.\nThe Pix must also not be currently spawned.");
                    return ("Remove Pix", null);
                }));
        } else {
            items.Add(new ContextMenuButton("Copy PixId", icon: FontAwesomeIcon.Copy,
                onClick: () => { ImGui.SetClipboardText(syncedPix.Id); },
                tooltip: () => ("Copy PixId", "Copy the Id of this synced Pix to your clipboard to share with others.\nThe receiver can copy the Id & join via the Sync Search window.")));
            if(SyncService.IsConnectedAuth) {
                items.Add(new ContextMenuButton("Members", icon: FontAwesomeIcon.Users,
                onClick: () => {
                    Windows.Get<PixMembersWindow>().Toggle(syncedPix, syncedPix.SelfRank == PixRank.Owner);
                }));

                if(syncedPix.SelfRank == PixRank.Owner) {
                    items.Add(new ContextMenuButton("Unsync Pix", icon: FontAwesomeIcon.Unlink,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModCtrl)) {
                            SyncService.DeleteSyncedPix(syncedPix.Id);
                        }
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                            return ("Unsync Pix", "Remove the Pix from the Sync Service & restore it as a local Pix.\n\nHold the Control key to confirm.");
                        return ("Unsync Pix", null);
                    }));
                } else {
                    items.Add(new ContextMenuButton("Report", icon: FontAwesomeIcon.ExclamationTriangle,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModShift)) {
                            SyncService.ReportPix(syncedPix.Id);
                        }
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModShift),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModShift))
                            return ("Report Pix", "Report this Pix for service violation.\nFalse reports may have consequences.\n\nHold the Shift key to confirm.");
                        return ("Report Pix", null);
                    }));

                    items.Add(new ContextMenuButton("Leave Pix", icon: FontAwesomeIcon.Unlink,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModCtrl)) {
                            SyncService.UnsubscribePix(syncedPix.Id);
                        }
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                            return ("Leave Pix", "Hold the Control key to confirm.");
                        return ("Leave Pix", null);
                    }));
                }
            }
        }
        return new ContextMenu("pixContext", items, width: 120f, itemHeight: 26f);
    }
}
