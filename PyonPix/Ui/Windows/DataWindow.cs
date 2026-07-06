using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Structs.Data;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class DataWindow : BaseWindow {
    private PixService PixService => Services.Get<PixService>();
    private DataService DataService => Services.Get<DataService>();

    protected override WindowState State => Config.UI.Data.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.Data.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(300, 150);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;

    public override void OnOpen() {
        base.OnOpen();
        _ = DataService.RefreshCacheAsync();
        Config.UI.Data.IsOpen = true;
        Config.Save();
    }
    public override void OnClose() {
        base.OnClose();
        Config.UI.Data.IsOpen = false;
        Config.Save();
    }

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.Data.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.Data.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnConfigClicked() => Windows.Get<ConfigWindow>().Toggle();
    protected override void OnCloseClicked() => IsOpen = false;

    private enum Tab { Cache, Cookies }
    private Tab ActiveTab = Tab.Cache;

    private float TabHeight => 28f * ImGuiHelpers.GlobalScale;
    private float RowHeight => 72f * ImGuiHelpers.GlobalScale;
    private float IconSize => 16f * ImGuiHelpers.GlobalScale;
    private float HorizontalPadding => 8f * ImGuiHelpers.GlobalScale;
    private float VerticalPadding => 8f * ImGuiHelpers.GlobalScale;
    private float Spacing => 6f * ImGuiHelpers.GlobalScale;

    public DataWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} Data Manager###{Plugin.Name}Data", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(420, 320) * ImGuiHelpers.GlobalScale;

        //DataService.OnUDFCacheUpdated += () => {
        //};

        DataService.OnUDFRemovalCompleted += (pixId, result) => {
            _ = DataService.RefreshCacheAsync();
        };
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(!IsOpen) return;

        DrawTabs();

        if(ActiveTab == Tab.Cache) {
            DrawCacheTab();
        } else {
            DrawCookiesTab();
        }
    }

    private void DrawTabs() {
        var draw = ImGui.GetWindowDrawList();
        Vector2 cursorPos = ImGui.GetCursorScreenPos();

        float contentWidth = ImGui.GetContentRegionAvail().X;
        float tabWidth = (contentWidth - (HorizontalPadding * 2f)) / 2f;

        // Storage tab
        var extMin = cursorPos + new Vector2(HorizontalPadding, 0);
        var extMax = cursorPos + new Vector2(tabWidth, TabHeight);
        if(DrawTab(extMin, extMax, "Cache", ActiveTab == Tab.Cache)) {
            ActiveTab = Tab.Cache;
        }

        // Cookies tab
        var browseMin = new Vector2(extMax.X + Spacing, extMin.Y);
        var browseMax = browseMin + new Vector2(tabWidth, TabHeight);
        if(DrawTab(browseMin, browseMax, "Cookies", ActiveTab == Tab.Cookies)) {
            ActiveTab = Tab.Cookies;
        }

        ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, TabHeight + Spacing));
    }

    private bool DrawTab(Vector2 min, Vector2 max, string text, bool active) {
        var draw = ImGui.GetWindowDrawList();

        var hovered = UiUtil.IsRectHovered(min, max);
        var clicked = UiUtil.IsRectClicked(min, max);

        var bgCol = active ? UIShared.TabBgActive : clicked ? UIShared.TabBgClicked : hovered ? UIShared.TabBgHovered : UIShared.TabBgNormal;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(bgCol), UIShared.TabRounding);

        var textCol = active ? UIShared.TabTextActive : clicked ? UIShared.TabTextClicked : hovered ? UIShared.TabTextHovered : UIShared.TabTextNormal;
        using(UIShared.NormalFont.Push()) {
            Vector2 textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = new Vector2(min.X + ((max.X - min.X) - textSize.X) * 0.5f, min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f);
            ImGui.SetCursorScreenPos(textPos);
            ImGuiEx.StyledText(text, colorA: textCol.AsVector3());
        }
        return clicked;
    }

    private void DrawCacheTab() {
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        float contentWidth = ImGui.GetContentRegionAvail().X;

        // Top
        ImGui.SetCursorScreenPos(cursorPos + new Vector2(HorizontalPadding, 0));
        var textSize = 0f;
        using(UIShared.SubFont.Push()) {
            var total = $"Total Cache: {GetTotalSizeString()}";
            textSize = ImGui.CalcTextSize(total).X;
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), ImGui.GetColorU32(UIShared.Muted), total);
        }

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X + HorizontalPadding + textSize + Spacing, cursorPos.Y));
        if(ImGuiEx.IconButton(FontAwesomeIcon.Sync, "##refresh", tooltip: "Refresh", size: IconSize)) {
            _ = DataService.RefreshCacheAsync();
        }

        ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, IconSize + Spacing));
        ImGui.BeginChild("##udfRows", new Vector2(contentWidth, ImGui.GetContentRegionAvail().Y));
        List<UDF> snapshot = DataService.GetUDFSnapshot();
        foreach(var item in snapshot) {
            DrawUDFRow(item);
        }
        ImGui.EndChild();
    }

    private void DrawUDFRow(UDF item) {
        ImGui.PushID(item.PixId);
        var p = PixService.GetPix(item.PixId);

        float width = ImGui.GetContentRegionAvail().X;
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        Vector2 rowMax = rowMin + new Vector2(width, RowHeight);
        Vector2 rowSize = rowMax - rowMin;

        // background
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
        if(hovered) {
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ItemBgHovered));
        }

        // persistent check
        float leftBlock = HorizontalPadding + IconSize + HorizontalPadding;
        float iconPadding = HorizontalPadding + (IconSize * 0.5f);
        Vector2 checkboxPos = new Vector2(rowMin.X + iconPadding, rowMin.Y + ((RowHeight - IconSize) * 0.5f));
        ImGui.SetCursorScreenPos(checkboxPos);

        bool persistent = item.PersistentCache;
        if(p != null && ImGuiEx.Checkbox("##togglePersist", ref persistent, tooltip: "Toggle Persistent Cache")) {
            DataService.SetPersistent(item.PixId, persistent);
        }

        // action buttons
        float actionRightX = rowMax.X - HorizontalPadding - IconSize;
        Vector2 actionPos = new Vector2(actionRightX, rowMin.Y + ((RowHeight - IconSize) * 0.5f));
        ImGui.SetCursorScreenPos(actionPos);

        bool isSpawned = PixService.IsSpawned(p);
        bool removeDisabled = isSpawned || item.IsRemoving;
        if(ImGuiEx.IconButton(FontAwesomeIcon.TrashAlt, "##remove", removeDisabled, "Clear Cache", item.IsRemoving ? "Processing.." : isSpawned ? "Unable to clear cache while spawned." : null, IconSize)) {
            DataService.RemoveUDF(item.PixId);
        }

        // Size text
        string sizeText = item.SizeBytes >= 0 ? FormatBytes(item.SizeBytes) : "Calculating...";
        using(UIShared.SubFont.Push()) {
            var vSize = UiUtil.CalcTextSize(sizeText, ImGui.GetFontSize(), false);
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - HorizontalPadding - vSize.X, actionPos.Y + IconSize + 4f));
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), ImGui.GetColorU32(UIShared.Muted), sizeText);
        }

        float textLeft = rowMin.X + leftBlock + (IconSize * 0.5f) + HorizontalPadding;
        float textRight = actionRightX - Spacing;
        ImGui.PushClipRect(new Vector2(textLeft, rowMin.Y), new Vector2(textRight, rowMax.Y), true);

        // Title
        Vector2 titlePos = new Vector2(textLeft, rowMin.Y + VerticalPadding);
        ImGui.SetCursorScreenPos(titlePos);
        if(!item.PixExists) {
            using(UIShared.NormalIconFont.Push()) {
                var icon = FontAwesomeIcon.ExclamationTriangle.ToIconString();
                var iconSize = ImGui.CalcTextSize(icon).X;
                ImGuiEx.StyledText(icon, colorA: UIShared.Error.AsVector3(), tooltip: "Pix Not Found", tooltipSub: "This data has no associated Pix, it can be safely removed.");
                titlePos = new Vector2(titlePos.X + iconSize + Spacing, titlePos.Y);
            }
        }
        using(UIShared.NormalFont.Push()) {
            ImGui.SetCursorScreenPos(titlePos);
            ImGuiEx.StyledText(item.PixId, colorA: item.PixExists ? UIShared.ItemHeader.AsVector3() : UIShared.Error.AsVector3());
        }

        // pix name
        if(!string.IsNullOrWhiteSpace(item.PixName)) {
            Vector2 descPos = new Vector2(titlePos.X, titlePos.Y + ImGui.GetFontSize() + (Spacing * 0.6f));
            using(UIShared.SubFont.Push()) {
                ImGui.SetCursorScreenPos(descPos);
                ImGuiEx.StyledText(item.PixName, colorA: UIShared.Dimmed.AsVector3());
            }
        }

        // status
        Vector2 metaPos = new Vector2(titlePos.X, titlePos.Y + (ImGui.GetFontSize() * 2f) + (Spacing * 1.2f));
        using(UIShared.SubFont.Push()) {
            string updated = item.LastWriteUtc.HasValue ? item.LastWriteUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Unknown";
            string status = item.IsRemoving ? " (Removing...)" : "";
            string meta = $"Updated: {updated}{status}";
            ImGui.SetCursorScreenPos(metaPos);
            ImGuiEx.StyledText(meta, colorA: UIShared.Dimmed.AsVector3());
        }

        ImGui.PopClipRect();

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y + Spacing));
        ImGui.PopID();
    }

    private string GetTotalSizeString() {
        var snapshot = DataService.GetUDFSnapshot();
        long total = snapshot.Where(x => x.SizeBytes > 0).Sum(x => x.SizeBytes);
        return FormatBytes(total);
    }

    private static string FormatBytes(long bytes) {
        if(bytes < 0) return "Unknown";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while(len >= 1024 && order < sizes.Length - 1) {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private void DrawCookiesTab() {
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X + HorizontalPadding, cursorPos.Y));
        ImGuiEx.StyledText("Nothing to see here for now :3");
    }
}
