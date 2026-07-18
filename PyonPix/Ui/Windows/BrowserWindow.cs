using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Utility;
using PyonPix.Config;
using PyonPix.Config.Pix;
using PyonPix.Events;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Structs.Browser;
using PyonPix.Ui.Components;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class BrowserWindow : BaseWindow {
    private PixService PixService => Services.Get<PixService>();
    private BrowserService BrowserService => Services.Get<BrowserService>();
    private PixInputService PixInputService => Services.Get<PixInputService>();
    private SyncService SyncService => Services.Get<SyncService>();

    protected override WindowState State => Config.UI.Browser.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.Browser.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(300, 150);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;

    public override void OnOpen() {
        base.OnOpen();
        BrowserService.IsHidden = IsHidden;
        Config.UI.Browser.IsOpen = true;
        Config.Save();
    }
    public override void OnClose() {
        base.OnClose();
        BrowserService.IsHidden = true;
        Config.UI.Browser.IsOpen = false;
        Config.Save();
    }

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.Browser.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.Browser.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
        BrowserService.IsHidden = IsHidden;
    }
    protected override void OnConfigClicked() {
        Windows.Get<ConfigWindow>().Toggle();
    }
    protected override void OnCloseClicked() {
        IsOpen = false;
        OnCloseUserInteraction();
    }
    public void OnCloseUserInteraction() {
        if(BrowserService.State == BrowserState.Stopped || BrowserService.State == BrowserState.Stopping) return;
        // todo: Provide browser exit behaviour?
    }
    private float ToolBarHeight => 26f * ImGuiHelpers.GlobalScale;
    private float TitleToolbarHeight => CollapsedHeight + ToolBarHeight;
    private float ButtonSize => ToolBarHeight;
    private float Spacing => 2f * ImGuiHelpers.GlobalScale;

    private readonly ContextMenu ConfigContextMenu;

    public BrowserWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} Browser###{Plugin.Name}Browser", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(960, 540) * ImGuiHelpers.GlobalScale;

        ConfigContextMenu = new ContextMenu("browserConfig", [
            new ContextMenuButton("Pix Config", icon: FontAwesomeIcon.Cog, onClick: () => { Windows.Get<PixConfigWindow>().Toggle(PixService.GetPix(BrowserService.FocusedTab?.PixId)); }),
            new ContextMenuButton("Extension Manager", icon: FontAwesomeIcon.PuzzlePiece, onClick: () => { Windows.Get<ExtensionsWindow>().Toggle(); }),
            new ContextMenuButton("Data Manager", icon: FontAwesomeIcon.Folder, onClick: () => { Windows.Get<DataWindow>().Toggle(); })
        ], width: 140f, itemHeight: 26f);

        PixService.PixSpawned += (p, isUserAction) => {
            if(isUserAction) return;
            var onEnter = Config.Global.Browser.TerritorySpawnBehaviour;
            if(onEnter.HasFlag(SpawnBehaviour.Expand)) {
                IsOpen = true;
                SetState(WindowState.Expanded);
            } else if(onEnter.HasFlag(SpawnBehaviour.Show)) {
                IsOpen = true;
            }
        };
        PixService.PixDespawned += (p, isUserAction) => {
            if(isUserAction) return;
            var onExit = Config.Global.Browser.TerritoryDespawnBehaviour;
            if(onExit.HasFlag(DespawnBehaviour.Collapse)) {
                IsOpen = true;
                SetState(WindowState.Collapsed);
            } else if(onExit.HasFlag(DespawnBehaviour.Hide)) {
                IsOpen = false;
            }
        };

        BrowserService.OnStatusUpdate += (e) => {
            if(e.StatusType == StatusType.None) {
                StatusBar.Hide();
            } else {
                StatusBar.Show(e.Status, e.DisplayTime, e.Overlay);
            }
        };
    }

    public override void Draw() => base.Draw();

    protected override void DrawTitleBarText(float leftCursor, float rightCursor) {
        if(!IsOpen) return;

        var draw = ImGui.GetWindowDrawList();
        float tabsStartX = leftCursor + (4 * ImGuiHelpers.GlobalScale);
        float tabsEndX = rightCursor - (4 * ImGuiHelpers.GlobalScale);
        float tabsAvailWidth = Math.Max(0, tabsEndX - tabsStartX);
        Vector2 tabY0 = new Vector2(tabsStartX, HeaderMin.Y);
        ImGui.SetCursorScreenPos(tabY0);

        float tabPaddingX = 4f * ImGuiHelpers.GlobalScale;
        float tabSpacing = 10f * ImGuiHelpers.GlobalScale;
        float maxTabWidth = 140f * ImGuiHelpers.GlobalScale;

        var tabs = BrowserService.Tabs.Values.ToList();
        float usedWidth = 0;
        int visibleCount = 0;
        for(int i = 0; i < tabs.Count; i++) {
            var t = tabs[i];
            var isFocusedTab = BrowserService.FocusedTab == t;
            float favIconSize = t.FavIcon == null ? 0 : 16f * ImGuiHelpers.GlobalScale;

            string titleSnippet = t.GetTitle().Truncate(10, string.Empty)!;
            float approxLabelWidth = UiUtil.CalcTextSize(titleSnippet, 14f).X;
            float tabWidth = favIconSize + (2 * tabPaddingX) + approxLabelWidth + (16f * ImGuiHelpers.GlobalScale); // close button maybe
            tabWidth = MathF.Min(maxTabWidth, tabWidth);

            if(i > 0) {
                ImGui.SameLine(usedWidth == 0 ? 0 : 0, 0); // come back to this
            }
            ImGui.PushID(t.PixId);
            var tabRectMin = ImGui.GetCursorScreenPos();
            var tabRectMax = tabRectMin + new Vector2(tabWidth, TitleBarFrameHeight);

            // favicon
            if(t.FavIcon != null) {
                ImGui.SetCursorScreenPos(new Vector2(tabRectMin.X + tabPaddingX, tabRectMin.Y + (TitleBarFrameHeight - favIconSize) * 0.5f));
                ImGui.Image(t.FavIcon.Handle, new Vector2(favIconSize, favIconSize), tintCol: !isFocusedTab ? new Vector4(0.7f, 0.7f, 0.7f, 0.7f) : Vector4.One);
            }

            // title
            ImGui.SetCursorScreenPos(new Vector2(tabRectMin.X + tabPaddingX + favIconSize + 6f * ImGuiHelpers.GlobalScale, tabRectMin.Y + (TitleBarFrameHeight - ImGui.CalcTextSize(titleSnippet).Y) * 0.5f));
            ImGuiEx.StyledText(titleSnippet, colorA: isFocusedTab ? UIShared.BrowserTabFocused.AsVector3() : UIShared.BrowserTabInactive.AsVector3());

            // hit test
            ImGui.SetCursorScreenPos(tabRectMin);
            ImGui.InvisibleButton("##tabHit", new Vector2(tabWidth, TitleBarFrameHeight)); // removed close button spacing  - (16f * ImGuiHelpers.GlobalScale)
            if(!isFocusedTab) {
                if(ImGui.IsItemHovered()) {
                    draw.AddRectFilled(tabRectMin, new Vector2(tabRectMax.X, tabRectMax.Y), ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.20f)), 2f);
                }
                if(ImGui.IsItemClicked()) {
                    BrowserService.FocusTab(t.PixId, true);
                }
            } else {
                draw.AddRectFilled(tabRectMin, new Vector2(tabRectMax.X, tabRectMax.Y), ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.10f)), 2f);
            }
            // close button
            //ImGui.SameLine();
            //ImGui.SetCursorScreenPos(new Vector2(tabRectMax.X - (16f * ImGuiHelpers.GlobalScale) - tabPaddingX, tabRectMin.Y + (TitleBarFrameHeight - (12f * ImGuiHelpers.GlobalScale)) * 0.5f));
            //if(ImGuiEx.IconTextButton(FontAwesomeIcon.Times, "##tabclose", 12f * ImGuiHelpers.GlobalScale, 0.7f)) {
            //}

            ImGui.PopID();

            usedWidth += tabWidth + tabSpacing;
            visibleCount++;
        }

        // no tabs
        if(visibleCount == 0) {
            base.DrawTitleBarText(leftCursor, rightCursor);
        }
    }

    private Vector2 PreviousSize;
    protected override void DrawContent() {
        if(!IsOpen) return;

        DrawToolbar();

        var currentSize = IsCollapsed ? Config.UI.Browser.ExpandedSize : ImGui.GetWindowSize();
        var sizeChanged = currentSize != PreviousSize;
        var mouseDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        BrowserService.DetermineResizeState(sizeChanged, mouseDragging);
        PreviousSize = currentSize;

        var size = IsCollapsed ? currentSize - new Vector2(0, TitleToolbarHeight) : ImGui.GetContentRegionAvail();
        if(BrowserService.Draw(ImGui.GetCursorScreenPos(), IsCollapsed ? size : ImGui.GetContentRegionAvail())) {
            if(!IsHidden) PixInputService.HandleImGuiPresentationMouseInput();
        } else {
            WindowName = $"{Plugin.Name}###{Plugin.Name}Browser";
            PixInputService.ClearImGuiPresentationFocus();
        }
    }
    
    private void DrawToolbar() {
        var scale = ImGuiHelpers.GlobalScale;
        var draw = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var contentSize = ImGui.GetContentRegionAvail();

        ImGui.BeginChild("##toolbar", new Vector2(0, ToolBarHeight + 1f), false, ImGuiWindowFlags.NoScrollbar);

        var toolBarMin = new Vector2(cursorPos.X, cursorPos.Y + 1f);
        var toolBarMax = cursorPos + new Vector2(contentSize.X, ToolBarHeight + 1f);
        draw.AddRectFilled(toolBarMin, toolBarMax, ImGui.GetColorU32(UIShared.TitleBarBg));

        var sepTop = cursorPos;
        draw.AddLine(sepTop, new Vector2(sepTop.X + contentSize.X, sepTop.Y), ImGui.GetColorU32(UIShared.ToolBarSeparator), MathF.Max(1f, 1f * scale));
        var sepBottom = new Vector2(sepTop.X, sepTop.Y + ToolBarHeight + 1f);
        draw.AddLine(sepBottom, new Vector2(sepBottom.X + contentSize.X, sepBottom.Y), ImGui.GetColorU32(UIShared.ToolBarSeparator), MathF.Max(1f, 1f * scale));

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, sepTop.Y + 1f));
        if(ImGuiEx.IconButton(FontAwesomeIcon.AngleLeft, "##navBack", !BrowserService.CanGoBack, size: ButtonSize, tooltip: "Back", tooltipSub: "Right-click for history")) BrowserService.NavBack();
        if(ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && BrowserService.FocusedTab?.History.Count > 0) {
            ImGui.OpenPopup("##historyContext");
        }
        ImGui.SameLine(0, Spacing);
        if(ImGuiEx.IconButton(FontAwesomeIcon.AngleRight, "##navForward", !BrowserService.CanGoForward, size: ButtonSize, tooltip: "Forward", tooltipSub: "Right-click for history")) BrowserService.NavForward();
        if(ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && BrowserService.FocusedTab?.History.Count > 0) {
            ImGui.OpenPopup("##historyContext");
        }
        DrawHistoryContextMenu(toolBarMin, toolBarMax, new ((selectedIndex) => { BrowserService.NavHistory(selectedIndex); }));

        ImGui.SameLine(0, Spacing);
        if(BrowserService.CanCancel) {
            if(ImGuiEx.IconButton(FontAwesomeIcon.Times, "##navCancel", size: ButtonSize, tooltip: "Abort")) BrowserService.NavCancel();
        } else {
            if(ImGuiEx.IconButton(FontAwesomeIcon.Redo, "##navReload", !BrowserService.CanReload, size: ButtonSize, tooltip: "Reload")) BrowserService.NavReload();
        }
        ImGui.SameLine(0, Spacing);
        if(ImGuiEx.IconButton(FontAwesomeIcon.Home, "##navHome", !BrowserService.CanNavigate, size: ButtonSize, tooltip: "Home")) BrowserService.NavHome();

        var isSynced = SyncService.IsConnectedAuth && PixService.GetPix(BrowserService.FocusedTab?.PixId) is SyncedPix;
        int rightIcons = isSynced ? 4 : 3;

        ImGui.SameLine(0, Spacing);
        var inputWidth = ImGui.GetContentRegionAvail().X - (ToolBarHeight * rightIcons) - (Spacing * rightIcons);
        ImGuiEx.StyledInput("##uriInput", ref BrowserService.PresentationUri, "Search Google or enter a URI", disabled: !BrowserService.CanNavigate, maxLength: ushort.MaxValue, width: inputWidth, onEnter: () => { BrowserService.Navigate(BrowserService.PresentationUri); });

        ImGui.SameLine(0, Spacing);
        if(ImGuiEx.IconButton(FontAwesomeIcon.ArrowRight, "##navSubmit", !BrowserService.CanNavigate, size: ButtonSize, tooltip: "Submit")) BrowserService.Navigate(BrowserService.PresentationUri);

        if(isSynced) {
            ImGui.SameLine(0, Spacing);
            if(ImGuiEx.IconButton(FontAwesomeIcon.Sync, "##sync", !BrowserService.CanNavigate, size: ButtonSize, tooltip: "Resync")) SyncService.SyncMediaState(BrowserService.FocusedTab!.PixId, null);
        }

        ImGui.SameLine(0, Spacing);
        if(ImGuiEx.IconButton(FontAwesomeIcon.Expand, "##theatreMode", !BrowserService.CanNavigate, size: ButtonSize, tooltip: "Toggle Theatre Mode")) BrowserService.ToggleTheatreMode();

        ImGui.SameLine(0, Spacing);
        if(ImGuiEx.IconButton(FontAwesomeIcon.EllipsisV, "##configMenu", size: ButtonSize, tooltip: "Settings")) {
            ConfigContextMenu.Open();
        }
        ConfigContextMenu.Draw(new Vector2(toolBarMax.X - (140f * ImGuiHelpers.GlobalScale), toolBarMax.Y + (1f * ImGuiHelpers.GlobalScale)));

        ImGui.EndChild();
    }

    private void DrawHistoryContextMenu(Vector2 anchorMin, Vector2 anchorMax, Action<int>? onItemSelected) {
        if(BrowserService.FocusedTab == null) return;
        if(!ImGui.IsPopupOpen("##historyContext")) return;

        var scale = ImGuiHelpers.GlobalScale;
        var desiredWidth = 200f * scale;
        float itemPadding = 4f * scale;
        float itemSpacingY = 2f * scale;
        float itemHeight = ImGui.GetFrameHeight();
        int maxVisibleItems = 10;
        int totalItemCount = BrowserService.FocusedTab.History.Count;
        if(totalItemCount == 0) return;

        var anchorPos = new Vector2(anchorMin.X, anchorMax.Y + (1f * scale));

        int currentIndex = BrowserService.FocusedTab.CurrentNavigationIndex;
        int maxItems = maxVisibleItems;
        int start = currentIndex - (maxItems / 2);
        int end = start + maxItems;
        if(start < 0) {
            start = 0;
            end = Math.Min(maxItems, totalItemCount);
        } else if(end > totalItemCount) {
            end = totalItemCount;
            start = Math.Max(0, end - maxItems);
        }

        int displayCount = Math.Min(totalItemCount, end - start);
        if(displayCount <= 0) return;

        float popupHeight = itemHeight * displayCount;

        ImGui.SetNextWindowPos(anchorPos, ImGuiCond.Appearing, new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(desiredWidth, popupHeight), ImGuiCond.Appearing);

        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);

        if(ImGui.BeginPopup("##historyContext", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings)) {
            ImGui.BeginChild("##historyList", new Vector2(desiredWidth, popupHeight), false, ImGuiWindowFlags.NoScrollbar);

            var draw = ImGui.GetWindowDrawList();

            draw.AddRectFilled(anchorPos, anchorPos + new Vector2(desiredWidth, popupHeight), ImGui.GetColorU32(UIShared.ContextMenuBg), UIShared.InputRounding);
            draw.AddRect(anchorPos, anchorPos + new Vector2(desiredWidth, popupHeight), ImGui.GetColorU32(UIShared.ContextMenuBorder), UIShared.InputRounding);

            for(int displayIndex = 0; displayIndex < displayCount; displayIndex++) {
                int actualIndex = end - 1 - displayIndex;
                var isCurrent = actualIndex == currentIndex;
                var h = BrowserService.FocusedTab.History[actualIndex];

                var rowMin = anchorPos + new Vector2(0f, displayIndex * itemHeight);
                var rowMax = rowMin + new Vector2(desiredWidth, itemHeight);

                var rowHovered = UiUtil.IsRectHovered(rowMin, rowMax);
                if(rowHovered || isCurrent) {
                    draw.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(rowHovered ? UIShared.ContextItemBgHovered : UIShared.ContextItemBgActive), UIShared.InputRounding);
                }

                if(UiUtil.IsRectClicked(rowMin, rowMax, ImGuiMouseButton.Left)) {
                    onItemSelected?.Invoke(actualIndex);
                    ImGui.CloseCurrentPopup();
                }

                using(UIShared.SubFont.Push()) {
                    var col = rowHovered ? UIShared.ContextItemTextHovered : isCurrent ? UIShared.ContextItemTextActive : UIShared.ContextItemTextNormal;
                    draw.AddText(new Vector2(rowMin.X + itemPadding, rowMin.Y + ((rowMax.Y - rowMin.Y - ImGui.GetFontSize()) * 0.5f)), ImGui.GetColorU32(col), h.GetDisplayTitle());
                }

                if(string.IsNullOrWhiteSpace(h.Title)) {
                    Tooltip.Show(h.Uri.TruncateMiddle(60), rectMin: rowMin, rectMax: rowMax);
                } else {
                    Tooltip.Show(h.GetDisplayTitle(), h.Uri.TruncateMiddle(60), rectMin: rowMin, rectMax: rowMax);
                }
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(2);
    }
}
