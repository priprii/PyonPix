using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PyonPix.Utility;

namespace PyonPix.Ui.Components;

public static class Tooltip {
    private struct TooltipRequest {
        public string Content;
        public string? Subtext;
        public Vector2? RectMin;
        public Vector2? RectMax;
        public Vector2? AnchorPosition;
        public Vector2? FixedSize;
        public float FadeSeconds;
        public float MaxWidth;
        public double ObservedAt;
    }

    private enum TooltipState {
        Idle,
        WaitingDelay,
        FadingIn,
        Visible,
        FadingOut
    }

    private static TooltipRequest? PendingRequest = null;

    private static TooltipRequest? ActiveRequest = null;
    private static TooltipState State = TooltipState.Idle;
    private static double StateStartTime = 0.0;

    private static int LastFrameDrawn = -1;

    public static void Show(string content, string? subtext = null, Vector2? rectMin = null, Vector2? rectMax = null, Vector2? anchorPosition = null, Vector2? fixedSize = null, float fadeSeconds = 0.15f, float maxWidth = 512f) {
        var hovered = (rectMin.HasValue && rectMax.HasValue) ? UiUtil.IsRectHovered(rectMin.Value, rectMax.Value) : ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var anchored = anchorPosition.HasValue;
        if(!(hovered || anchored)) return;

        var now = ImGui.GetTime();
        var newReq = new TooltipRequest {
            Content = content ?? string.Empty,
            Subtext = string.IsNullOrEmpty(subtext) ? null : subtext,
            RectMin = rectMin,
            RectMax = rectMax,
            AnchorPosition = anchorPosition,
            FixedSize = fixedSize,
            FadeSeconds = Math.Max(0f, fadeSeconds),
            MaxWidth = maxWidth,
            ObservedAt = now
        };

        if(PendingRequest.HasValue && !TooltipRequestsDiffer(PendingRequest.Value, newReq)) {
            newReq.ObservedAt = PendingRequest.Value.ObservedAt;
        }

        PendingRequest = newReq;
    }

    public static void Draw() {
        var frame = ImGui.GetFrameCount();
        if(LastFrameDrawn == frame) return;
        LastFrameDrawn = frame;

        var now = ImGui.GetTime();

        var pending = PendingRequest;
        PendingRequest = null;

        if(pending.HasValue) {
            var req = pending.Value;

            if(ActiveRequest == null) {
                ActiveRequest = req;
                State = req.FadeSeconds > 0f ? TooltipState.FadingIn : TooltipState.Visible;
                StateStartTime = now;
            } else {
                if(TooltipRequestsDiffer(ActiveRequest.Value, req)) {
                    ActiveRequest = req;
                    State = req.FadeSeconds > 0f ? TooltipState.FadingIn : TooltipState.Visible;
                    StateStartTime = now;
                } else {
                    if(State == TooltipState.FadingOut) {
                        State = ActiveRequest.Value.FadeSeconds > 0f ? TooltipState.FadingIn : TooltipState.Visible;
                        StateStartTime = now;
                    }
                }
            }
        } else {
            if(ActiveRequest != null) {
                var fade = ActiveRequest.Value.FadeSeconds;
                if(fade > 0f) {
                    if(State != TooltipState.FadingOut) {
                        State = TooltipState.FadingOut;
                        StateStartTime = now;
                    }
                } else {
                    ActiveRequest = null;
                    State = TooltipState.Idle;
                    StateStartTime = 0.0;
                    return;
                }
            } else {
                return;
            }
        }

        if(State == TooltipState.WaitingDelay) {
            if(pending.HasValue) {
                var req = pending.Value;
                var start = req.ObservedAt;
                ActiveRequest = req;
                State = req.FadeSeconds > 0f ? TooltipState.FadingIn : TooltipState.Visible;
                StateStartTime = now;
            } else {
                if(ActiveRequest != null) {
                    var fade = ActiveRequest.Value.FadeSeconds;
                    if(fade > 0f) {
                        State = TooltipState.FadingOut;
                        StateStartTime = now;
                    } else {
                        ActiveRequest = null;
                        State = TooltipState.Idle;
                    }
                }
                return;
            }
        }

        float alpha = ComputeAlphaForState(now);
        if(alpha > 0f && ActiveRequest.HasValue) {
            DrawTooltip(ActiveRequest.Value, alpha);
        }

        if(State == TooltipState.FadingOut && ActiveRequest.HasValue) {
            var fade = ActiveRequest.Value.FadeSeconds;
            if(fade > 0f && (now - StateStartTime) >= fade) {
                ActiveRequest = null;
                State = TooltipState.Idle;
            }
        }
        if(State == TooltipState.FadingIn && ActiveRequest.HasValue) {
            var fade = ActiveRequest.Value.FadeSeconds;
            if(fade > 0f && (now - StateStartTime) >= fade) {
                State = TooltipState.Visible;
            }
        }
    }

    private static float ComputeAlphaForState(double now) {
        if(ActiveRequest == null) return 0f;
        var fade = ActiveRequest.Value.FadeSeconds;
        if(fade <= 0f) return (State == TooltipState.Visible || State == TooltipState.FadingIn) ? 1f : 0f;

        switch(State) {
            case TooltipState.FadingIn: {
                    var t = (float)((now - StateStartTime) / fade);
                    return Math.Clamp(t, 0f, 1f);
                }
            case TooltipState.Visible:
                return 1f;
            case TooltipState.FadingOut: {
                    var t = (float)((now - StateStartTime) / fade);
                    return Math.Clamp(1f - t, 0f, 1f);
                }
            default:
                return 0f;
        }
    }

    private static bool TooltipRequestsDiffer(in TooltipRequest a, in TooltipRequest b) {
        if(!string.Equals(a.Content, b.Content, StringComparison.Ordinal)) return true;
        if(!string.Equals(a.Subtext ?? string.Empty, b.Subtext ?? string.Empty, StringComparison.Ordinal)) return true;
        if(a.AnchorPosition.HasValue != b.AnchorPosition.HasValue) return true;
        if(a.AnchorPosition.HasValue && b.AnchorPosition.HasValue) {
            if(a.AnchorPosition.Value != b.AnchorPosition.Value) return true;
        }
        if(a.FixedSize.HasValue != b.FixedSize.HasValue) return true;
        if(a.FixedSize.HasValue && b.FixedSize.HasValue) {
            if(a.FixedSize.Value != b.FixedSize.Value) return true;
        }
        if(Math.Abs(a.MaxWidth - b.MaxWidth) > float.Epsilon) return true;
        if(Math.Abs(a.FadeSeconds - b.FadeSeconds) > float.Epsilon) return true;
        return false;
    }

    private static void DrawTooltip(in TooltipRequest req, float alpha) {
        var scale = ImGuiHelpers.GlobalScale;

        var padding = UIShared.TooltipPadding;
        var rounding = UIShared.TooltipRounding;
        var borderThickness = UIShared.TooltipBorderThickness;
        var contentFont = UIShared.NormalFont;
        var subFont = UIShared.SubFont;

        var display = ImGui.GetIO().DisplaySize;
        var maxW = MathF.Min(req.MaxWidth * scale, display.X * 0.85f);
        var available = MathF.Max(1f, maxW - (padding.X * 2f));

        Vector2 singleLineContent;
        Vector2 wrappedContent;
        Vector2 singleLineSub = Vector2.Zero;
        Vector2 wrappedSub = Vector2.Zero;

        using(contentFont.Push()) {
            singleLineContent = ImGui.CalcTextSize(req.Content, false, 100000f);
            wrappedContent = ImGui.CalcTextSize(req.Content, false, available);
        }

        if(!string.IsNullOrEmpty(req.Subtext)) {
            using(subFont.Push()) {
                singleLineSub = ImGui.CalcTextSize(req.Subtext!, false, 100000f);
                wrappedSub = ImGui.CalcTextSize(req.Subtext!, false, available);
            }
        }

        var contentNeedsWrap = singleLineContent.X > available;
        var subNeedsWrap = singleLineSub.X > available;

        var textWidth = contentNeedsWrap ? wrappedContent.X : singleLineContent.X;
        var subWidth = (!string.IsNullOrEmpty(req.Subtext)) ? (subNeedsWrap ? wrappedSub.X : singleLineSub.X) : 0f;

        var width = MathF.Max(textWidth, subWidth) + padding.X * 2f;
        if(req.FixedSize.HasValue) width = req.FixedSize.Value.X;
        width = MathF.Min(width, maxW);

        var height = (contentNeedsWrap ? wrappedContent.Y : singleLineContent.Y) + padding.Y * 2f;
        if(!string.IsNullOrEmpty(req.Subtext)) {
            height += (padding.Y * 2) + (subNeedsWrap ? wrappedSub.Y : singleLineSub.Y);
        }
        if(req.FixedSize.HasValue) height = req.FixedSize.Value.Y;


        var wrapWidth = 0f;
        if(contentNeedsWrap) {
            wrapWidth = MathF.Max(1f, width - padding.X * 2f);
        } else {
            wrapWidth = 0f;
        }

        Vector2 pos = req.AnchorPosition ?? (ImGui.GetMousePos() + new Vector2(12f * scale, 18f * scale));
        if(pos.X + width > display.X) pos.X = MathF.Max(4f * scale, display.X - width - (4f * scale));
        if(pos.Y + height > display.Y) pos.Y = MathF.Max(4f * scale, display.Y - height - (4f * scale));

        var draw = ImGui.GetForegroundDrawList();
        var rectMin = pos;
        var rectMax = pos + new Vector2(width, height);

        var bg = UIShared.TooltipBg;
        var border = UIShared.TooltipBorder;
        var contentCol = UIShared.TooltipText;
        var subtextCol = UIShared.TooltipSubText;
        var separatorCol = UIShared.TooltipSeparator;

        bg.W *= alpha; border.W *= alpha; contentCol.W *= alpha; subtextCol.W *= alpha; separatorCol.W *= alpha;

        draw.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(bg), rounding);
        draw.AddRect(rectMin, rectMax, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, borderThickness);

        var cursorY = rectMin.Y + padding.Y;
        var textX = rectMin.X + padding.X;

        using(contentFont.Push()) {
            if(wrapWidth <= 0f) {
                draw.AddText(new Vector2(textX, cursorY), ImGui.GetColorU32(contentCol), req.Content);
            } else {
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, cursorY), ImGui.GetColorU32(contentCol), req.Content, wrapWidth);
            }
            cursorY += (contentNeedsWrap ? wrappedContent.Y : singleLineContent.Y);
        }

        if(!string.IsNullOrEmpty(req.Subtext)) {
            cursorY += padding.Y;
            draw.AddLine(new Vector2(textX, cursorY), new Vector2(rectMax.X - padding.X, cursorY), ImGui.GetColorU32(separatorCol), MathF.Max(1f, 1f * scale));
            cursorY += padding.Y;
            using(subFont.Push()) {
                if(subNeedsWrap) {
                    draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, cursorY), ImGui.GetColorU32(subtextCol), req.Subtext!, MathF.Max(1f, width - padding.X * 2f));
                } else {
                    draw.AddText(new Vector2(textX, cursorY), ImGui.GetColorU32(subtextCol), req.Subtext!);
                }
            }
        }
    }
}
