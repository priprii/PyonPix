using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PyonPix.Events;
using PyonPix.Extensions;
using PyonPix.Utility;

namespace PyonPix.Ui.Components;

public class StatusBar {
    private string Text = string.Empty;
    private int DurationMs;
    private StatusType StatusType;
    private string? TooltipText;
    private string? TooltipSubText;
    private Action? ExpirationAction;

    private DateTime? VisibleTimestamp;
    private const float VerticalPadding = 4f;
    private const float HorizontalPadding = 6f;
    private const float FontSize = 13f;
    private const float BorderThickness = 1f;

    public float Height => (FontSize + (VerticalPadding * 2f)) * ImGuiHelpers.GlobalScale;

    public bool IsOverlay;

    public bool IsVisible {
        get {
            if(!field) return false;
            if(DurationMs > 0 && VisibleTimestamp.HasValue) {
                if((DateTime.UtcNow - VisibleTimestamp.Value).TotalMilliseconds >= DurationMs) {
                    field = false;
                    ExpirationAction?.Invoke();
                    return false;
                }
            }
            return true;
        }

        private set;
    }

    public void Show(string text, int durationMs = 0, bool overlay = false, StatusType statusType = StatusType.Info, string? tooltipText = null, string? tooltipSubtext = null, Action? expirationAction = null) {
        Text = text;
        DurationMs = durationMs;
        IsOverlay = overlay;
        VisibleTimestamp = DateTime.UtcNow;
        IsVisible = true;
        StatusType = statusType;
        TooltipText = tooltipText;
        TooltipSubText = tooltipSubtext;
        ExpirationAction = expirationAction;
    }

    public void Hide() {
        IsVisible = false;
    }

    public void Draw(Vector2 boundsMin, Vector2 boundsMax) {
        if(!IsVisible) return;

        var scale = ImGuiHelpers.GlobalScale;
        var height = Height;
        var barMin = new Vector2(boundsMin.X, boundsMax.Y - height);
        var barMax = boundsMax;
        var draw = IsOverlay ? ImGui.GetForegroundDrawList() : ImGui.GetWindowDrawList();

        draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(UIShared.TooltipBg), UIShared.WindowRounding, ImDrawFlags.RoundCornersBottom);

        var borderThickness = BorderThickness * scale;
        draw.AddLine(new Vector2(barMin.X, barMin.Y + borderThickness * 0.5f), new Vector2(barMax.X, barMin.Y + borderThickness * 0.5f), ImGui.GetColorU32(UIShared.TooltipBorder), borderThickness);

        var hPad = HorizontalPadding * scale;
        var vPad = VerticalPadding * scale;
        var textMin = new Vector2(barMin.X + hPad, barMin.Y + vPad);
        var textMax = new Vector2(barMax.X - hPad, barMax.Y - vPad);
        var textWidth = textMax.X - textMin.X;

        using(UIShared.NormalFont.Push()) {
            var col = StatusType switch {
                StatusType.Warn => UIShared.Warn,
                StatusType.Error => UIShared.Error,
                _ => UIShared.TooltipText
            };
            if(IsOverlay) {
                ImGuiEx.StyledText(Text, FontSize, colorA: col.AsVector3(), wrapWidth: textWidth, targetDrawList: draw, screenOffset: textMin, clipMin: textMin, clipMax: textMax);
            } else {
                ImGui.SetCursorScreenPos(textMin);
                ImGuiEx.StyledText(Text, FontSize, colorA: col.AsVector3(), wrapWidth: textWidth, clipMin: textMin, clipMax: textMax);
            }
        }

        if(TooltipText != null && UiUtil.IsRectHovered(barMin, barMax)) {
            Tooltip.Show(TooltipText, TooltipSubText, rectMin: barMin, rectMax: barMax);
        }
    }
}
