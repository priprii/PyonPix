using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using PyonPix.Extensions;
using PyonPix.Utility;

namespace PyonPix.Ui.Components;

public abstract class ContextMenuItem {
    public bool IsVisible { get; set; } = true;
}

public class ContextMenuSeparator : ContextMenuItem { }

public class ContextMenuHeader : ContextMenuItem {
    public Func<string> Text;
    public FontAwesomeIcon? Icon;
    public ContextMenuHeader(Func<string> text, FontAwesomeIcon? icon = null) { Text = text; Icon = icon; }
    public ContextMenuHeader(string text, FontAwesomeIcon? icon = null) : this(() => text, icon) { }
}

public class ContextMenuSubText : ContextMenuItem {
    public Func<string> Text;
    public FontAwesomeIcon? Icon;
    public ContextMenuSubText(Func<string> text, FontAwesomeIcon? icon = null) { Text = text; Icon = icon; }
    public ContextMenuSubText(string text, FontAwesomeIcon? icon = null) : this(() => text, icon) { }
}

[Flags]
public enum ContextMenuTint {
    None = 0,
    Icon = 1,
    Text = 2,
    Both = Icon | Text
}

public class ContextMenuButton : ContextMenuItem {
    public Func<string> Text;
    public FontAwesomeIcon? Icon;
    public Action? OnClick;
    public bool CloseOnClick;
    public Func<bool>? IsActive;
    public Func<bool>? IsDisabled;
    public ContextMenuTint ActiveTint;
    public ContextMenuTint DisabledTint;
    public Func<(string, string?)?>? Tooltip;

    public ContextMenuButton(
        Func<string> text,
        Action? onClick = null,
        bool closeOnClick = true,
        FontAwesomeIcon? icon = null,
        Func<bool>? isActive = null,
        Func<bool>? isDisabled = null,
        ContextMenuTint activeTint = ContextMenuTint.Both,
        ContextMenuTint disabledTint = ContextMenuTint.Both,
        Func<(string, string?)?>? tooltip = null) {
        Text = text; OnClick = onClick; CloseOnClick = closeOnClick; Icon = icon;
        IsActive = isActive; IsDisabled = isDisabled;
        ActiveTint = activeTint; DisabledTint = disabledTint; Tooltip = tooltip;
    }

    public ContextMenuButton(
        string text,
        Action? onClick = null,
        bool closeOnClick = true,
        FontAwesomeIcon? icon = null,
        Func<bool>? isActive = null,
        Func<bool>? isDisabled = null,
        ContextMenuTint activeTint = ContextMenuTint.Both,
        ContextMenuTint disabledTint = ContextMenuTint.Both,
        Func<(string, string?)?>? tooltip = null)
        : this(() => text, onClick, closeOnClick, icon, isActive, isDisabled, activeTint, disabledTint, tooltip) { }
}

public class ContextMenuCheckbox : ContextMenuItem {
    public Func<string> Text;
    public Func<bool> GetValue;
    public Action<bool> SetValue;
    public bool CloseOnClick;
    public Func<bool>? IsDisabled;
    public ContextMenuTint DisabledTint;
    public Func<(string, string?)?>? Tooltip;

    public ContextMenuCheckbox(
        Func<string> text,
        Func<bool> getValue,
        Action<bool> setValue,
        bool closeOnClick = false,
        Func<bool>? isDisabled = null,
        ContextMenuTint disabledTint = ContextMenuTint.Both,
        Func<(string, string?)?>? tooltip = null) {
        Text = text; GetValue = getValue; SetValue = setValue; CloseOnClick = closeOnClick;
        IsDisabled = isDisabled; DisabledTint = disabledTint; Tooltip = tooltip;
    }

    public ContextMenuCheckbox(
        string text,
        Func<bool> getValue,
        Action<bool> setValue,
        bool closeOnClick = false,
        Func<bool>? isDisabled = null,
        ContextMenuTint disabledTint = ContextMenuTint.Both,
        Func<(string, string?)?>? tooltip = null)
        : this(() => text, getValue, setValue, closeOnClick, isDisabled, disabledTint, tooltip) { }
}

// todo
public class ContextMenuSubmenu : ContextMenuItem {
    public Func<string> Text;
    public FontAwesomeIcon? Icon;
    public List<ContextMenuItem> SubItems;
    public Func<bool>? IsDisabled;
    public ContextMenuSubmenu(string text, List<ContextMenuItem> subItems, FontAwesomeIcon? icon = null, Func<bool>? isDisabled = null) {
        Text = () => text;
        Icon = icon;
        SubItems = subItems;
        IsDisabled = isDisabled;
    }
    public ContextMenuSubmenu(Func<string> text, List<ContextMenuItem> subItems, FontAwesomeIcon? icon = null, Func<bool>? isDisabled = null) {
        Text = text;
        Icon = icon;
        SubItems = subItems;
        IsDisabled = isDisabled;
    }
}

public class ContextMenuTab {
    public string Id { get; }
    public Func<string> Text;
    public FontAwesomeIcon? Icon;
    public List<ContextMenuItem> Items;

    public ContextMenuTab(string id, string text, List<ContextMenuItem> items, FontAwesomeIcon? icon = null) : this(id, () => text, items, icon) { }

    public ContextMenuTab(string id, Func<string> text, List<ContextMenuItem> items, FontAwesomeIcon? icon = null) {
        Id = id;
        Text = text;
        Items = items;
        Icon = icon;
    }
}

public class ContextMenu {
    public readonly string Id;

    private readonly List<ContextMenuItem>? Items;
    private readonly List<ContextMenuTab>? Tabs;

    public float Width;
    public float ItemHeight;
    public float SubTextHeight => ItemHeight * 0.5f;
    public float SeperatorHeight = 12f;
    public int MaxItemsDisplayed;
    public Action<int>? ActiveTabUpdated;

    public float TabHeight = 26f;

    private int ActiveTabIndex = 0;
    private Vector2 MousePos;

    public ContextMenu(string id, List<ContextMenuItem> items, float width = 140f, float itemHeight = 0f, int maxItemsDisplayed = 12) {
        Id = $"##ctx_{id}";
        Items = items;
        Tabs = null;
        Width = width;
        ItemHeight = itemHeight;
        MaxItemsDisplayed = maxItemsDisplayed;
    }

    public ContextMenu(string id, List<ContextMenuTab> tabs, int activeTabIndex = 0, float width = 140f, float itemHeight = 0f, int maxItemsDisplayed = 12, Action<int>? activeTabUpdated = null) {
        Id = $"##ctx_{id}";
        Items = null;
        Tabs = tabs;
        ActiveTabIndex = activeTabIndex;
        Width = width;
        ItemHeight = itemHeight;
        MaxItemsDisplayed = maxItemsDisplayed;
        ActiveTabUpdated = activeTabUpdated;
    }

    public void Open() { MousePos = ImGui.GetMousePos(); ImGui.OpenPopup(Id); }
    public void Open(string id) { if($"##ctx_{id}" == Id) { MousePos = ImGui.GetMousePos(); ImGui.OpenPopup(Id); } }
    public bool IsOpen() => ImGui.IsPopupOpen(Id);
    public bool IsOpen(string id) => $"##ctx_{id}" == Id && ImGui.IsPopupOpen(Id);

    public void Draw(string id, Vector2? anchorPos = null) {
        if($"##ctx_{id}" != Id) return;
        Draw(anchorPos ?? MousePos);
    }

    public void Draw(Vector2? anchorPos = null) {
        if(!ImGui.IsPopupOpen(Id)) return;
        if((Items == null || Items.Count == 0) && (Tabs == null || Tabs.Count == 0)) return;

        var scale = ImGuiHelpers.GlobalScale;
        var width = Width * scale;
        var padding = 6f * scale;

        var hasTabs = Tabs != null && Tabs.Count > 0;
        var tabHeight = hasTabs ? TabHeight * scale : 0f;

        var activeItems = hasTabs
            ? Tabs![Math.Clamp(ActiveTabIndex, 0, Tabs.Count - 1)].Items.Where(x => x.IsVisible).ToList()
            : Items!.Where(x => x.IsVisible).ToList();

        var contentItems = activeItems.Count(x => x is not (ContextMenuSeparator or ContextMenuHeader or ContextMenuSubText));
        var displayCount = Math.Min(contentItems, Math.Max(1, MaxItemsDisplayed));
        var contentHeight = (GetNonContentHeight(activeItems) + (displayCount * ItemHeight)) * scale;
        var popupHeight = tabHeight + contentHeight;
        if(anchorPos == null) anchorPos = MousePos;

        ImGui.SetNextWindowPos(anchorPos.Value, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(width, popupHeight), ImGuiCond.Appearing);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, UIShared.InputRounding);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        
        if(ImGui.BeginPopup(Id, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings)) {
            var draw = ImGui.GetWindowDrawList();
            var popupMin = anchorPos.Value;
            var popupMax = popupMin + new Vector2(width, popupHeight);

            draw.AddRectFilled(popupMin, popupMax, ImGui.GetColorU32(UIShared.ContextMenuBg), UIShared.InputRounding);
            draw.AddRect(popupMin, popupMax, ImGui.GetColorU32(UIShared.ContextMenuBorder), UIShared.InputRounding);

            if(hasTabs) DrawTabBar(draw, width, tabHeight, padding);

            ImGui.BeginChild($"{Id}content", new Vector2(width, contentHeight));

            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();
            draw.PushClipRect(clipMin, clipMax, true);
            for(int i = 0; i < activeItems.Count; i++) {
                var item = activeItems[i];
                var itemHeight = item is ContextMenuSeparator ? SeperatorHeight * scale : item is ContextMenuSubText ? SubTextHeight * scale : ItemHeight > 0f ? ItemHeight * scale : ImGui.GetFrameHeight();

                ImGui.InvisibleButton($"{Id}{i}dummy", new Vector2(width, itemHeight));

                switch(item) {
                    case ContextMenuSeparator:
                        DrawSeparatorItem(draw, scale);
                        break;
                    case ContextMenuHeader h:
                        DrawLabelRow(draw, h.Text(), h.Icon, padding, AnyInteractiveItemHasIcon(activeItems), UIShared.NormalFont, UIShared.WindowTitle);
                        break;
                    case ContextMenuSubText s:
                        DrawLabelRow(draw, s.Text(), s.Icon, padding, AnyInteractiveItemHasIcon(activeItems), UIShared.SubFont, UIShared.ItemSubText);
                        break;
                    case ContextMenuButton b: {
                        bool disabled = b.IsDisabled?.Invoke() ?? false;
                        bool active = !disabled && (b.IsActive?.Invoke() ?? false);
                        DrawInteractiveRow(
                            draw, b.Text(), b.Icon, padding, AnyInteractiveItemHasIcon(activeItems),
                            active, disabled, b.ActiveTint, b.DisabledTint, b.Tooltip,
                            onClick: () => {
                                b.OnClick?.Invoke();
                                if(b.CloseOnClick) ImGui.CloseCurrentPopup();
                            });
                        break;
                    }
                    case ContextMenuCheckbox cb: {
                        bool disabled = cb.IsDisabled?.Invoke() ?? false;
                        bool active = !disabled && cb.GetValue();
                        var cbIcon = active ? FontAwesomeIcon.CheckSquare : FontAwesomeIcon.Square;
                        DrawInteractiveRow(
                            draw, cb.Text(), cbIcon, padding, true,
                            active, disabled, ContextMenuTint.Icon, cb.DisabledTint, cb.Tooltip,
                            onClick: () => {
                                cb.SetValue(!cb.GetValue());
                                if(cb.CloseOnClick) ImGui.CloseCurrentPopup();
                            });
                        break;
                    }
                    case ContextMenuSubmenu submenu: {
                        bool disabled = submenu.IsDisabled?.Invoke() ?? false;
                        bool hovered = ImGui.IsItemHovered();
                        DrawLabelRow(draw, submenu.Text(), submenu.Icon, padding, AnyInteractiveItemHasIcon(activeItems), UIShared.SubFont, UIShared.ItemSubText);

                        if(hovered && !disabled) {
                            var subAnchor = new Vector2(ImGui.GetItemRectMax().X, ImGui.GetItemRectMin().Y);
                            var subMenu = new ContextMenu($"{Id}_sub_{i}", submenu.SubItems, Width, ItemHeight, MaxItemsDisplayed);
                            subMenu.Draw(subAnchor);
                        }
                        break;
                    }
                }
            }
            draw.PopClipRect();
            ImGui.EndChild();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private void DrawTabBar(ImDrawListPtr draw, float width, float tabHeight, float padding) {
        var tabCount = Tabs!.Count;
        if(tabCount <= 0) return;

        var rowMin = ImGui.GetCursorScreenPos();
        var rowMax = rowMin + new Vector2(width, tabHeight);

        float tabWidth = width / tabCount;

        for(int i = 0; i < tabCount; i++) {
            var tab = Tabs[i];
            var tabMin = rowMin + new Vector2(tabWidth * i, 0f);
            var tabMax = tabMin + new Vector2(tabWidth, tabHeight);

            ImGui.SetCursorScreenPos(tabMin);
            ImGui.InvisibleButton($"{Id}##tab_{i}", new Vector2(tabWidth, tabHeight));

            bool hovered = ImGui.IsItemHovered();
            bool active = i == ActiveTabIndex;

            if(ImGui.IsItemClicked()) {
                if(i != ActiveTabIndex) {
                    ActiveTabIndex = i;
                    ActiveTabUpdated?.Invoke(ActiveTabIndex);
                }
            }

            var fill = active ? UIShared.ContextItemBgActive : hovered ? UIShared.ContextItemBgHovered : Vector4.Zero;
            if(fill != Vector4.Zero) {
                var roundFlags = ImDrawFlags.RoundCornersNone;
                if(i == 0) roundFlags |= ImDrawFlags.RoundCornersTopLeft;
                if(i == tabCount - 1) roundFlags |= ImDrawFlags.RoundCornersTopRight;

                draw.AddRectFilled(tabMin, tabMax, ImGui.GetColorU32(fill), UIShared.InputRounding, roundFlags);
            }

            var lineCol = active ? UIShared.ContextItemTextHovered : UIShared.ContextItemTextNormal;
            using(UIShared.SubFont.Push()) {
                var text = tab.Text();
                var textSize = ImGui.CalcTextSize(text);
                var iconSize = tab.Icon.HasValue && tab.Icon.Value != FontAwesomeIcon.None ? ImGui.CalcTextSize(tab.Icon.Value.ToIconString()) : Vector2.Zero;

                float totalW = textSize.X + (iconSize.X > 0f ? iconSize.X + (padding * 0.5f) : 0f);
                float startX = tabMin.X + ((tabWidth - totalW) * 0.5f);
                float centerY = tabMin.Y + ((tabHeight - textSize.Y) * 0.5f);

                if(tab.Icon.HasValue && tab.Icon.Value != FontAwesomeIcon.None) {
                    using(UIShared.NormalIconFont.Push()) {
                        var iconStr = tab.Icon.Value.ToIconString();
                        draw.AddText(new Vector2(startX, tabMin.Y + ((tabHeight - iconSize.Y) * 0.5f)), ImGui.GetColorU32(lineCol), iconStr);
                    }
                    startX += iconSize.X + (padding * 0.5f);
                }

                draw.AddText(new Vector2(startX, centerY), ImGui.GetColorU32(lineCol), text);
            }
        }

        var scale = ImGuiHelpers.GlobalScale;
        draw.AddLine(new Vector2(rowMin.X, rowMax.Y), new Vector2(rowMin.X + width, rowMax.Y), ImGui.GetColorU32(UIShared.Separator), 1f * scale);
    }

    private static void DrawSeparatorItem(ImDrawListPtr draw, float scale) {
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        var width = rowMax.X - rowMin.X;
        var height = rowMax.Y - rowMin.Y;
        float lineY = rowMin.Y + height * 0.5f;
        float pad = 6f * scale;
        draw.AddLine(new Vector2(rowMin.X + pad, lineY), new Vector2(rowMin.X + width - pad, lineY), ImGui.GetColorU32(UIShared.Separator), 1f * scale);
    }

    private static void DrawLabelRow(ImDrawListPtr draw, string text, FontAwesomeIcon? icon, float padding, bool globalAnyIcon, IFontHandle fontHandle, Vector4 textCol) {
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();

        float textX = icon != null && icon != FontAwesomeIcon.None ? rowMin.X + UiUtil.CalcTextSize(UIShared.SubIconFont, FontAwesomeIcon.Cog.ToIconString()).X + (padding * 2) : rowMin.X + padding;
        if(icon != null && icon != FontAwesomeIcon.None) {
            using(UIShared.SubIconFont.Push()) {
                var iconStr = icon.Value.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                float iconX = rowMin.X + padding;
                float iconY = rowMin.Y + ((rowMax.Y - rowMin.Y - iconSize.Y) * 0.5f);
                draw.AddText(new Vector2(iconX, iconY), ImGui.GetColorU32(textCol), iconStr);
            }
        }

        using(fontHandle.Push()) {
            float textY = rowMin.Y + ((rowMax.Y - rowMin.Y - ImGui.GetFontSize()) * 0.5f);
            ImGuiEx.StyledText(text, glowStrength: 0.2f, colorA: textCol.AsVector3(), targetDrawList: draw, screenOffset: new Vector2(textX, textY));
        }
    }

    private static void DrawInteractiveRow(ImDrawListPtr draw, string text, FontAwesomeIcon? icon, float padding, bool globalAnyIcon, bool active, bool disabled, ContextMenuTint activeTint, ContextMenuTint disabledTint, Func<(string, string?)?>? tooltipFunc, Action onClick) {
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();

        bool mouseOver = ImGui.IsItemHovered();
        bool hovered = mouseOver && !disabled;

        if(hovered)
            draw.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ContextItemBgHovered), UIShared.InputRounding);
        else if(active)
            draw.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ContextItemBgActive), UIShared.InputRounding);

        Vector4 textCol = ResolveColor(hovered, active, disabled, activeTint, disabledTint, ContextMenuTint.Text);
        Vector4 iconCol = ResolveColor(hovered, active, disabled, activeTint, disabledTint, ContextMenuTint.Icon);

        float textX = icon != null && icon != FontAwesomeIcon.None ? rowMin.X + UiUtil.CalcTextSize(UIShared.SubIconFont, FontAwesomeIcon.Cog.ToIconString()).X + (padding * 2) : rowMin.X + padding;
        if(icon != null && icon != FontAwesomeIcon.None) {
            using(UIShared.SubIconFont.Push()) {
                var iconStr = icon.Value.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                float iconX = rowMin.X + padding;
                float iconY = rowMin.Y + ((rowMax.Y - rowMin.Y - iconSize.Y) * 0.5f);
                draw.AddText(new Vector2(iconX, iconY), ImGui.GetColorU32(iconCol), iconStr);
            }
        }

        using(UIShared.SubFont.Push()) {
            float textY = rowMin.Y + ((rowMax.Y - rowMin.Y - ImGui.GetFontSize()) * 0.5f);
            ImGuiEx.StyledText(text, glowStrength: 0f, colorA: textCol.AsVector3(), targetDrawList: draw, screenOffset: new Vector2(textX, textY));
        }

        if(!disabled && UiUtil.IsRectClicked(rowMin, rowMax))
            onClick();

        if(mouseOver && tooltipFunc != null) {
            var t = tooltipFunc();
            if(t != null) Tooltip.Show(t.Value.Item1, t.Value.Item2, rectMin: rowMin, rectMax: rowMax);
        }
    }

    private float GetNonContentHeight(List<ContextMenuItem> items) {
        float totalHeight = 0f;
        foreach(var item in items) {
            if(item is ContextMenuSeparator) { totalHeight += SeperatorHeight; continue; }
            if(item is ContextMenuHeader) { totalHeight += ItemHeight; continue; }
            if(item is ContextMenuSubText) { totalHeight += SubTextHeight; continue; }
        }
        return totalHeight;
    }

    private static Vector4 ResolveColor(bool hovered, bool active, bool disabled, ContextMenuTint activeTint, ContextMenuTint disabledTint, ContextMenuTint channel) {
        if(disabled && disabledTint.HasFlag(channel))
            return UIShared.ContextItemTextNormal with { W = UIShared.ContextItemTextNormal.W * 0.4f };
        if(hovered) return UIShared.ContextItemTextHovered;
        if(active && activeTint.HasFlag(channel)) return UIShared.ContextItemTextActive;
        return UIShared.ContextItemTextNormal;
    }

    private static bool AnyInteractiveItemHasIcon(List<ContextMenuItem> items) {
        foreach(var item in items) {
            if(item is ContextMenuButton b && b.Icon != null) return true;
            if(item is ContextMenuCheckbox) return true;
        }
        return false;
    }
}
