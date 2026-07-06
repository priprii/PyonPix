using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Structs.Browser;
using PyonPix.Structs.Ui;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class ExtensionsWindow : BaseWindow {
    private ExtensionsService ExtensionsService => Services.Get<ExtensionsService>();

    protected override WindowState State => Config.UI.Extensions.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.Extensions.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(300, 150);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;

    public override void OnOpen() {
        base.OnOpen();
        ExtensionsService.ResolveUnknownExtensions();
        if(Config.Global.Browser.CheckUpdateExtensions) {
            _ = ExtensionsService.CheckUpdateAllAsync(Config.Global.Browser.AutoUpdateExtensions);
        }
        Config.UI.Extensions.IsOpen = true;
        Config.Save();
    }
    public override void OnClose() {
        base.OnClose();
        Config.UI.Extensions.IsOpen = false;
        Config.Save();
    }

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.Extensions.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.Extensions.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnConfigClicked() => Windows.Get<ConfigWindow>().Toggle();
    protected override void OnCloseClicked() => IsOpen = false;

    private enum ExtensionTab { Extensions, Browse }
    private ExtensionTab ActiveTab = ExtensionTab.Extensions;

    private string SearchText = string.Empty;
    private string[] SearchAutoCompleteResults = [];
    private List<ExtensionProductDetails> SearchResults = [];

    private float TabHeight => 28f * ImGuiHelpers.GlobalScale;
    private float ResultRowHeight => 72f * ImGuiHelpers.GlobalScale;
    private float IconSize => 16f * ImGuiHelpers.GlobalScale;
    private float HorizontalPadding => 8f * ImGuiHelpers.GlobalScale;
    private float VerticalPadding => 8f * ImGuiHelpers.GlobalScale;
    private float Spacing => 6f * ImGuiHelpers.GlobalScale;

    public ExtensionsWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} Extension Manager###{Plugin.Name}Extensions", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(420, 320) * ImGuiHelpers.GlobalScale;

        ExtensionsService.OnAutoCompleteResult += (result) => {
            SearchAutoCompleteResults = result;
        };
        ExtensionsService.OnSearchResult += (result) => {
            SearchResults = result;
        };
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(!IsOpen) return;

        DrawTabs();

        if(ActiveTab == ExtensionTab.Extensions) {
            DrawExtensionsTab();
        } else {
            DrawBrowseTab();
        }
    }

    private void DrawTabs() {
        var draw = ImGui.GetWindowDrawList();
        Vector2 cursorPos = ImGui.GetCursorScreenPos();

        float contentWidth = ImGui.GetContentRegionAvail().X;
        float tabWidth = (contentWidth - (HorizontalPadding * 2f)) / 2f;

        // Extensions tab
        var extMin = cursorPos + new Vector2(HorizontalPadding, 0);
        var extMax = cursorPos + new Vector2(tabWidth, TabHeight);
        if(DrawTab(extMin, extMax, "Extensions", ActiveTab == ExtensionTab.Extensions)) {
            ActiveTab = ExtensionTab.Extensions;
        }

        // Browse tab
        var browseMin = new Vector2(extMax.X + Spacing, extMin.Y);
        var browseMax = browseMin + new Vector2(tabWidth, TabHeight);
        if(DrawTab(browseMin, browseMax, "Browse", ActiveTab == ExtensionTab.Browse)) {
            ActiveTab = ExtensionTab.Browse;
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

    private void DrawExtensionsTab() {
        var draw = ImGui.GetWindowDrawList();
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        float contentWidth = ImGui.GetContentRegionAvail().X;

        var checkPos = cursorPos + new Vector2(HorizontalPadding, 0);
        ImGui.SetCursorScreenPos(checkPos);
        if(ImGuiEx.Checkbox("Auto Check##autoCheck", ref Config.Global.Browser.CheckUpdateExtensions, false, "Automatically check for updates.")) {
            Config.Save();
        }
        
        if(Config.Global.Browser.CheckUpdateExtensions) {
            ImGui.SameLine();
            if(ImGuiEx.Checkbox("Auto Update##autoUpdate", ref Config.Global.Browser.AutoUpdateExtensions, false, "Automatically install updates after checking.")) {
                Config.Save();
            }
        }

        if(Config.Extensions.Count > 0) {
            ImGui.SameLine();
            if(ImGuiEx.IconButton(FontAwesomeIcon.ArrowsSpin, "##checkUpdate", ExtensionsService.IsOperating, "Check for updates now.")) {
                Task.Run(async () => { _ = ExtensionsService.CheckUpdateAllAsync(Config.Global.Browser.AutoUpdateExtensions); });
            }
        }

        ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, IconSize + Spacing));
        ImGui.BeginChild("##extensionRows", new Vector2(contentWidth, ImGui.GetContentRegionAvail().Y));
        foreach(var item in Config.Extensions) {
            if(!item.Value.IsDownloaded) continue;
            DrawExtensionRow(item.Value);
        }
        ImGui.EndChild();
    }

    private void DrawExtensionRow(Extension item) {
        if(item.CrxId == null) return;
        string crxId = item.CrxId;
        string name = item.Name ?? crxId;
        string shortDesc = item.Description ?? "";
        string version = item.Version ?? "";
        string developer = item.Developer ?? "";

        var isUpdateAvailable = item.IsUpdateAvailable;
        var isInstalled = item.IsInstalled;
        var isEnabled = item.IsEnabled;

        ImGui.PushID(crxId);

        float width = ImGui.GetContentRegionAvail().X;
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        Vector2 rowMax = rowMin + new Vector2(width, ResultRowHeight);
        Vector2 rowSize = rowMax - rowMin;

        // background
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
        if(hovered) {
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ItemBgHovered));
        }

        // Icon
        float iconPadding = HorizontalPadding + (IconSize * 0.5f);
        Vector2 iconPos = new Vector2(rowMin.X + iconPadding, rowMin.Y + ((ResultRowHeight - IconSize) * 0.5f));
        if(isInstalled) {
            ImGui.SetCursorScreenPos(iconPos);
            if(ImGuiEx.Checkbox("##toggle", ref isEnabled, ExtensionsService.IsOperating, "Toggle Extension")) {
                Task.Run(() => {
                    if(isEnabled) {
                        ExtensionsService.EnableExtension(crxId);
                    } else {
                        ExtensionsService.DisableExtension(crxId);
                    }
                });
            }
        }

        // Action Buttons
        float actionRightX = rowMax.X - HorizontalPadding - IconSize;
        Vector2 actionPos = new Vector2(actionRightX, rowMin.Y + ((ResultRowHeight - IconSize) * 0.5f));

        ImGui.SetCursorScreenPos(actionPos);
        if(!isInstalled && ImGuiEx.IconButton(FontAwesomeIcon.TrashAlt, "##remove", ExtensionsService.IsOperating, "Remove Extension", size: IconSize)) {
            Task.Run(() => { ExtensionsService.RemoveExtension(crxId); });
        } else if(isInstalled && ImGuiEx.IconButton(FontAwesomeIcon.Unlink, "##uninstall", ExtensionsService.IsOperating, "Uninstall Extension", size: IconSize)) {
            Task.Run(() => { ExtensionsService.UninstallExtension(crxId); });
        }

        actionRightX -= IconSize + HorizontalPadding;
        actionPos = new Vector2(actionRightX, actionPos.Y);
        ImGui.SetCursorScreenPos(actionPos);
        if(isUpdateAvailable && ImGuiEx.IconButton(FontAwesomeIcon.Repeat, "##update", ExtensionsService.IsOperating, "Update Extension", size: IconSize)) {
            Task.Run(async () => { _ = ExtensionsService.UpdateAsync(crxId).ConfigureAwait(false); });
        } else if(!isUpdateAvailable && !isInstalled && ImGuiEx.IconButton(FontAwesomeIcon.Link, "##install", ExtensionsService.IsOperating, "Install Extension", size: IconSize)) {
            Task.Run(() => { ExtensionsService.InstallExtension(crxId); });
        }

        // Version
        using(UIShared.SubFont.Push()) {
            var versionText = $"v{version}";
            var vSize = UiUtil.CalcTextSize(versionText, ImGui.GetFontSize(), false);
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - HorizontalPadding - vSize.X, actionPos.Y + IconSize + 4f));
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), ImGui.GetColorU32(UIShared.Muted), versionText);
        }

        // Text Region
        float textLeft = rowMin.X + (iconPadding * 2) + IconSize;
        float textRight = actionRightX - Spacing;
        ImGui.PushClipRect(new Vector2(textLeft, rowMin.Y), new Vector2(textRight, rowMax.Y), true);

        // Title
        Vector2 titlePos = new Vector2(textLeft, rowMin.Y + VerticalPadding);
        using(UIShared.NormalFont.Push()) {
            ImGui.SetCursorScreenPos(titlePos);
            ImGuiEx.StyledText(name, colorA: UIShared.ItemHeader.AsVector3());
        }

        // desc
        Vector2 descPos = new Vector2(titlePos.X, titlePos.Y + ImGui.GetFontSize() + Spacing);
        using(UIShared.SubFont.Push()) {
            ImGui.SetCursorScreenPos(descPos);
            ImGuiEx.StyledText(shortDesc, colorA: UIShared.Dimmed.AsVector3());
        }

        // crxId
        Vector2 idPos = new Vector2(titlePos.X, descPos.Y + ImGui.GetFontSize() + (Spacing * 0.4f));
        using(UIShared.SubFont.Push()) {
            ImGui.SetCursorScreenPos(idPos);
            ImGuiEx.StyledText(crxId, colorA: UIShared.Dimmed.AsVector3());
        }

        ImGui.PopClipRect();

        ImGui.SetCursorScreenPos(new(rowMin.X, rowMax.Y + Spacing));
        ImGui.PopID();
    }

    private void DrawBrowseTab() {
        Vector2 cursorPos = ImGui.GetCursorScreenPos();

        float contentWidth = ImGui.GetContentRegionAvail().X;

        float inputWidth = contentWidth - (HorizontalPadding * 2f);
        ImGui.SetCursorScreenPos(cursorPos + new Vector2(HorizontalPadding, 0));
        var state = ImGuiEx.StyledInput("##search", ref SearchText, "Search for Extension..", maxLength: 256, width: inputWidth,
            onEnter: new(() => { _ = SubmitSearchAndClearAsync(SearchText); }),
            autoCompleteList: SearchAutoCompleteResults, onAutoCompleteSelection: (result) => { _ = SubmitSearchAndClearAsync(result); });
        if(state == UIState.Using) {
            _ = ExtensionsService.AutoCompleteAsync(SearchText);
        }

        ImGui.BeginChild("##searchRows", new Vector2(contentWidth, ImGui.GetContentRegionAvail().Y));

        foreach(var item in SearchResults) {
            DrawSearchResultRow(item);
        }

        ImGui.EndChild();
    }

    private void DrawSearchResultRow(ExtensionProductDetails item) {
        if(item.CrxId == null) return;
        string crxId = item.CrxId;
        string name = item.Name ?? crxId;
        string shortDesc = item.ShortDescription ?? "";
        string version = item.Version ?? "";
        string developer = item.DeveloperName ?? "";
        float rating = 0f;
        try { rating = Convert.ToSingle(item.Rating ?? 0f); } catch { rating = 0f; }
        long ratingCount = 0;
        try { ratingCount = Convert.ToInt64(item.RatingCount ?? 0); } catch { ratingCount = 0; }
        long installCount = 0;
        try { installCount = Convert.ToInt64(item.InstallCount ?? 0); } catch { installCount = 0; }

        Config.Extensions.TryGetValue(crxId, out var cfgExt);
        var isDownloaded = cfgExt?.IsDownloaded ?? false;
        var isInstalled = cfgExt?.IsInstalled ?? false;
        var isUpdateAvailable = isDownloaded && !string.Equals(cfgExt?.Version, version, StringComparison.OrdinalIgnoreCase);
        var isEnabled = cfgExt?.IsEnabled ?? false;

        ImGui.PushID(crxId);

        float width = ImGui.GetContentRegionAvail().X;
        Vector2 rowMin = ImGui.GetCursorScreenPos();
        Vector2 rowMax = rowMin + new Vector2(width, ResultRowHeight);
        Vector2 rowSize = rowMax - rowMin;

        // background
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
        if(hovered) {
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ItemBgHovered));
        }

        // Icon
        float iconPadding = HorizontalPadding + (IconSize * 0.5f);
        Vector2 iconPos = new Vector2(rowMin.X + iconPadding, rowMin.Y + ((ResultRowHeight - IconSize) * 0.5f));
        if(isInstalled) {
            ImGui.SetCursorScreenPos(iconPos);
            if(ImGuiEx.Checkbox("##toggle", ref isEnabled, ExtensionsService.IsOperating, tooltip: "Toggle Extension")) {
                Task.Run(() => {
                    if(isEnabled) {
                        ExtensionsService.EnableExtension(crxId);
                    } else {
                        ExtensionsService.DisableExtension(crxId);
                    }
                });
            }
        }

        // Action Buttons
        float actionRightX = rowMax.X - HorizontalPadding - IconSize;
        Vector2 actionPos = new Vector2(actionRightX, rowMin.Y + ((ResultRowHeight - IconSize) * 0.5f));

        ImGui.SetCursorScreenPos(actionPos);
        if(!isDownloaded && ImGuiEx.IconButton(FontAwesomeIcon.Download, "##download", ExtensionsService.IsOperating, "Download Extension", size: IconSize)) {
            Task.Run(async () => { _ = ExtensionsService.DownloadAndExtractCrxAsync(crxId).ConfigureAwait(false); });
        } else if(isDownloaded && !isInstalled && ImGuiEx.IconButton(FontAwesomeIcon.TrashAlt, "##remove", ExtensionsService.IsOperating, "Remove Extension", size: IconSize)) {
            Task.Run(() => { ExtensionsService.RemoveExtension(crxId); });
        } else if(isInstalled && ImGuiEx.IconButton(FontAwesomeIcon.Unlink, "##uninstall", ExtensionsService.IsOperating, "Uninstall Extension", size: IconSize)) {
            Task.Run(() => { ExtensionsService.UninstallExtension(crxId); });
        }

        actionRightX -= IconSize + HorizontalPadding;
        actionPos = new Vector2(actionRightX, actionPos.Y);
        if(isUpdateAvailable || (isDownloaded && !isInstalled)) {
            ImGui.SetCursorScreenPos(actionPos);
            if(isUpdateAvailable && ImGuiEx.IconButton(FontAwesomeIcon.Repeat, "##update", ExtensionsService.IsOperating, "Update Extension", size: IconSize)) {
                Task.Run(async () => { _ = ExtensionsService.UpdateAsync(crxId).ConfigureAwait(false); });
            } else if(isDownloaded && !isInstalled && ImGuiEx.IconButton(FontAwesomeIcon.Link, "##install", ExtensionsService.IsOperating, "Install Extension", size: IconSize)) {
                Task.Run(() => { ExtensionsService.InstallExtension(crxId); });
            }
        }

        // Version
        using(UIShared.SubFont.Push()) {
            var versionText = $"v{version}";
            var vSize = UiUtil.CalcTextSize(versionText, ImGui.GetFontSize(), false);
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - HorizontalPadding - vSize.X, actionPos.Y + IconSize + 4f));
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetCursorScreenPos(), ImGui.GetColorU32(UIShared.Muted), versionText);
        }

        float textLeft = rowMin.X + (iconPadding * 2) + IconSize;
        float textRight = actionRightX - Spacing;
        ImGui.PushClipRect(new Vector2(textLeft, rowMin.Y), new Vector2(textRight, rowMax.Y), true);

        // Title
        Vector2 titlePos = new Vector2(textLeft, rowMin.Y + VerticalPadding);
        using(UIShared.NormalFont.Push()) {
            ImGui.SetCursorScreenPos(titlePos);
            ImGuiEx.StyledText(name, colorA: UIShared.ItemHeader.AsVector3());
        }

        // desc
        Vector2 descPos = new Vector2(titlePos.X, titlePos.Y + ImGui.GetFontSize() + (Spacing * 0.6f));
        using(UIShared.SubFont.Push()) {
            ImGui.SetCursorScreenPos(descPos);
            ImGuiEx.StyledText(shortDesc, colorA: UIShared.Dimmed.AsVector3());
        }

        // Rating
        Vector2 ratingPos = new Vector2(titlePos.X, descPos.Y + ImGui.GetFontSize() + (Spacing * 0.6f));
        var starsSize = DrawRatingStars(ratingPos, rating);
        Vector2 countsPos = new Vector2(ratingPos.X + starsSize.X + (4f * ImGuiHelpers.GlobalScale), ratingPos.Y);
        string ratingCountText = $"({ratingCount.ToString("N0", CultureInfo.CurrentCulture)})";
        string installCountText = $"Users: {installCount.ToString("N0", CultureInfo.CurrentCulture)}";
        using(UIShared.SubFont.Push()) {
            ImGui.SetCursorScreenPos(countsPos);
            ImGuiEx.StyledText($"{ratingCountText} {installCountText}", colorA: UIShared.Muted.AsVector3());
        }

        ImGui.PopClipRect();

        ImGui.SetCursorScreenPos(new(rowMin.X, rowMax.Y + Spacing));
        ImGui.PopID();
    }

    private Vector2 DrawRatingStars(Vector2 pos, float rating) {
        var draw = ImGui.GetWindowDrawList();
        float totalWidth = 0;
        float starSize = 12f * ImGuiHelpers.GlobalScale;
        float gap = 2f * ImGuiHelpers.GlobalScale;
        int full = (int)Math.Floor(rating);
        float frac = rating - full;
        using(UIShared.NormalIconFont.Push()) {
            for(int i = 0; i < 5; i++) {
                Vector2 p = new Vector2(pos.X + (i * (starSize + gap)), pos.Y);
                if(i < full) {
                    draw.AddText(ImGui.GetFont(), starSize, p, ImGui.GetColorU32(UiUtil.RGBA(255, 200, 40, 255)), FontAwesomeIcon.Star.ToIconString());
                } else if(i == full && frac >= 0.5f) {
                    draw.AddText(ImGui.GetFont(), starSize, p, ImGui.GetColorU32(UiUtil.RGBA(255, 200, 40, 255)), FontAwesomeIcon.StarHalfAlt.ToIconString());
                } else {
                    draw.AddText(ImGui.GetFont(), starSize, p, ImGui.GetColorU32(UIShared.Muted), FontAwesomeIcon.Star.ToIconString());
                }
                totalWidth = p.X - pos.X;
            }
        }

        var totalSize = new Vector2(totalWidth + starSize, starSize);
        return totalSize;
    }

    private async Task SubmitSearchAndClearAsync(string query) {
        if(string.IsNullOrWhiteSpace(query)) return;
        SearchText = string.Empty;
        SearchAutoCompleteResults = [];
        _ = ExtensionsService.SearchAsync(query).ConfigureAwait(false);
    }
}
