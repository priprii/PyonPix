using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Lumina.Text;
using PyonPix.Interop;
using PyonPix.Shared.Sync.Dto.Client;
using PyonPix.Structs.Ui;
using PyonPix.Ui;
using PyonPix.Utility;
using SeString = Dalamud.Game.Text.SeStringHandling.SeString;
using Tooltip = PyonPix.Ui.Components.Tooltip;

namespace PyonPix.Extensions;

public static class ImGuiEx {
    private static readonly Dictionary<string, bool> WasUsingColorPicker4 = [];
    public static UIState ColorPicker4(string labelId, ref Vector4 value, float? size = null) {
        var res = UIState.None;
        var label = string.Empty;
        var id = labelId;
        using(ImRaii.PushId(labelId))
        if(labelId.Contains("##")) {
            var s = labelId.Split("##");
            label = s[0];
            id = s[1];
        }
        size ??= UIShared.NormalIconSize;
        if(ImGui.ColorButton($"##{id}", value, ImGuiColorEditFlags.NoInputs, new(size.Value))) {
            ImGui.OpenPopup(id);
        }
        if(!string.IsNullOrWhiteSpace(label)) {
            ImGui.SameLine();
            ImGui.Text(label);
        }

        if(ImGui.BeginPopup(id)) {
            ImGui.SetColorEditOptions(ImGuiColorEditFlags.DefaultOptions);
            var changed = ImGui.ColorPicker4(labelId, ref value, ImGuiColorEditFlags.DefaultOptions | ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoSidePreview);
            bool isActive = ImGui.IsItemActive(); // currently interacting with the item
            bool wasUsing = WasUsingColorPicker4.TryGetValue(id, out var w) && w;

            if(changed) {
                WasUsingColorPicker4[id] = true;
                res = UIState.Using;
            }
            if(!isActive && wasUsing) {
                WasUsingColorPicker4[id] = false;
                res = UIState.Ended;
            }

            ImGui.EndPopup();
        }

        return res;
    }

    private static readonly Dictionary<string, bool> WasUsingColorPicker3 = [];
    public static UIState ColorPicker3(string labelId, ref Vector3 value, float? size = null) {
        var res = UIState.None;
        var label = string.Empty;
        var id = labelId;
        using(ImRaii.PushId(labelId))
            if(labelId.Contains("##")) {
                var s = labelId.Split("##");
                label = s[0];
                id = s[1];
            }
        size ??= UIShared.NormalIconSize;
        if(ImGui.ColorButton($"##{id}", new(value, 1f), ImGuiColorEditFlags.NoInputs, new(size.Value))) {
            ImGui.OpenPopup(id);
        }
        if(!string.IsNullOrWhiteSpace(label)) {
            ImGui.SameLine();
            ImGui.Text(label);
        }

        if(ImGui.BeginPopup(id)) {
            ImGui.SetColorEditOptions(ImGuiColorEditFlags.DefaultOptions);
            var changed = ImGui.ColorPicker3(labelId, ref value, ImGuiColorEditFlags.DefaultOptions | ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoSidePreview);
            bool isActive = ImGui.IsItemActive(); // currently interacting with the item
            bool wasUsing = WasUsingColorPicker3.TryGetValue(id, out var w) && w;

            if(changed) {
                WasUsingColorPicker3[id] = true;
                res = UIState.Using;
            }
            if(!isActive && wasUsing) {
                WasUsingColorPicker3[id] = false;
                res = UIState.Ended;
            }

            ImGui.EndPopup();
        }

        return res;
    }

    public static void Separator(float width, float? spacing = null) {
        var s = spacing ?? UIShared.SeparatorSpacing;
        Separator(width, s, s);
    }
    public static void Separator(float width, float topSpacing, float botSpacing) {
        var draw = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        if(topSpacing != 0) ImGui.Dummy(new Vector2(0, topSpacing));
        draw.AddLine(cursorPos + new Vector2(0, topSpacing), cursorPos + new Vector2(width, topSpacing), ImGui.GetColorU32(UIShared.Separator));
        if(botSpacing != 0) ImGui.Dummy(new Vector2(0, botSpacing));
    }

    public static void SpacingY(float spacing) {
        ImGui.Dummy(new Vector2(0, spacing));
    }
    public static void SpacingX(float spacing, bool sameLinePrior = false, bool sameLineAfter = false) {
        if(sameLinePrior) ImGui.SameLine(0, 0);
        ImGui.Dummy(new Vector2(spacing, 0));
        if(sameLineAfter) ImGui.SameLine(0, 0);
    }

    private static readonly Dictionary<string, MouseLockState> DragLocked = [];
    private static readonly Dictionary<string, InteractionState> DragFocused = [];
    private static readonly Dictionary<string, bool> WasUsingDrag = [];
    public static UIState Drag<T>(string labelId, ref T value, float speed = 1f, T min = default, T max = default, int floatPrecision = 2, ImU8String format = default, bool disabled = false, float width = 0, float? height = null, float barHeightPercent = 0.1f, bool insetLabel = true, string? tooltip = null, string? tooltipSub = null) where T : unmanaged {
        width = width == 0 ? ImGui.GetContentRegionAvail().X : width;
        height = height == null ? UIShared.LineHeight : height == 0 ? ImGui.GetFrameHeight() : height;

        var isInt = typeof(T) == typeof(int);
        var isFloat = typeof(T) == typeof(float);
        var isUInt = typeof(T) == typeof(uint);
        var isShort = typeof(T) == typeof(short);

        float floatStep = 1f;
        string floatDisplayFormat = "0";
        if(isFloat) {
            floatPrecision = Math.Clamp(floatPrecision, 0, 6);
            floatStep = MathF.Pow(10f, -floatPrecision);
            floatDisplayFormat = floatPrecision == 0 ? "0" : "0." + new string('#', floatPrecision);
        }

        float curV, minV, maxV;
        if(isInt) {
            curV = Unsafe.As<T, int>(ref value);
            minV = Unsafe.As<T, int>(ref min);
            maxV = Unsafe.As<T, int>(ref max);
        } else if(isFloat) {
            curV = Unsafe.As<T, float>(ref value);
            minV = Unsafe.As<T, float>(ref min);
            maxV = Unsafe.As<T, float>(ref max);
        } else if(isUInt) {
            curV = Unsafe.As<T, uint>(ref value);
            minV = Unsafe.As<T, uint>(ref min);
            maxV = Unsafe.As<T, uint>(ref max);
        } else if(isShort) {
            curV = Unsafe.As<T, short>(ref value);
            minV = Unsafe.As<T, short>(ref min);
            maxV = Unsafe.As<T, short>(ref max);
        } else {
            curV = minV = maxV = 0f;
        }

        var res = UIState.None;
        var label = labelId;
        var id = labelId;
        using(ImRaii.PushId(labelId))
        if(labelId.Contains("##")) {
            var s = labelId.Split("##");
            label = s[0];
            id = s[1];
        }

        DragFocused.TryGetValue(id, out InteractionState state);
        DragLocked.TryGetValue(id, out MouseLockState lockState);

        var bgCol = disabled ? UIShared.InputBgDisabled : state.IsActive || state.IsInputActive ? UIShared.InputBgActive : state.IsHovered ? UIShared.InputBgHovered : UIShared.InputBgNormal;
        var textCol = disabled ? UIShared.InputTextDisabled : state.IsActive || state.IsInputActive ? UIShared.InputTextActive : state.IsHovered ? UIShared.InputTextHovered : UIShared.InputTextNormal;

        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(width);
        var changed = false;
        using(UIShared.NormalFont.Push()) {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, state.IsInputActive ? textCol : Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, UIShared.InputBgTextSelected);
            if(disabled) ImGui.BeginDisabled();

            if(isInt) {
                ref int vRef = ref Unsafe.As<T, int>(ref value);
                changed = ImGui.DragInt(insetLabel ? $"##{id}" : labelId, ref vRef, speed, Unsafe.As<T, int>(ref min), Unsafe.As<T, int>(ref max), format);
            } else if(isFloat) {
                ref float vRef = ref Unsafe.As<T, float>(ref value);
                changed = ImGui.DragFloat(insetLabel ? $"##{id}" : labelId, ref vRef, speed, Unsafe.As<T, float>(ref min), Unsafe.As<T, float>(ref max), format);
                if(changed) {
                    vRef = MathF.Round(vRef / floatStep) * floatStep;
                    if(maxV > minV) vRef = Math.Clamp(vRef, minV, maxV);
                    if(lockState.IsLocked == false) lockState.StoredDelta = 0f;
                }
            } else if(isUInt) {
                ref uint vRef = ref Unsafe.As<T, uint>(ref value);
                changed = ImGui.DragUInt(insetLabel ? $"##{id}" : labelId, ref vRef, speed, Unsafe.As<T, uint>(ref min), Unsafe.As<T, uint>(ref max), format);
            } else if(isShort) {
                ref short vRef = ref Unsafe.As<T, short>(ref value);
                changed = ImGui.DragShort(insetLabel ? $"##{id}" : labelId, ref vRef, speed, Unsafe.As<T, short>(ref min), Unsafe.As<T, short>(ref max), format);
            }

            if(disabled) ImGui.EndDisabled();
            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar();
        }
        var isHovered = ImGui.IsItemHovered();
        var isActive = ImGui.IsItemActive();
        var wasUsing = WasUsingDrag.TryGetValue(id, out var w) && w;
        var isInputActive = isHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        var isInputDeactivated = ImGui.IsItemDeactivated();
        var isPressed = isHovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var isReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        var lostFocus = !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        if(isPressed && !lockState.IsLocked && !state.IsInputActive) {
            if(Win32Interop.BeginDrag(out var xPos, out var yPos)) {
                lockState.X = xPos; lockState.Y = yPos;
                lockState.IsLocked = true;
                lockState.StoredDelta = 0f;
            }
        }

        if(lockState.IsLocked) {
            if(isReleased || lostFocus || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
                Win32Interop.EndDrag();
                lockState.IsLocked = false;
            } else {
                if(Win32Interop.GetDragCursorPos(out var xPos, out var yPos)) {
                    int dx = xPos - lockState.X;
                    if(isFloat) {
                        lockState.StoredDelta += dx * speed;
                        if(MathF.Abs(lockState.StoredDelta) >= floatStep) {
                            int steps = (int)MathF.Truncate(lockState.StoredDelta / floatStep);
                            float deltaValue = steps * floatStep;
                            ref float vRef = ref Unsafe.As<T, float>(ref value);
                            vRef += deltaValue;
                            if(maxV > minV) vRef = Math.Clamp(vRef, minV, maxV);
                            vRef = MathF.Round(vRef / floatStep) * floatStep;
                            lockState.StoredDelta -= steps * floatStep;
                            changed = true;
                        }
                    } else {
                        lockState.StoredDelta += dx * speed;
                        if(MathF.Abs(lockState.StoredDelta) >= 1f) {
                            int change = (int)MathF.Truncate(lockState.StoredDelta);
                            lockState.StoredDelta -= change;
                            if(isInt) {
                                ref int vRef = ref Unsafe.As<T, int>(ref value);
                                int newVal = vRef + change;
                                newVal = maxV > minV ? Math.Clamp(newVal, (int)minV, (int)maxV) : Math.Max(newVal, (int)minV);
                                vRef = newVal;
                            } else if(isUInt) {
                                ref uint vRef = ref Unsafe.As<T, uint>(ref value);
                                uint newVal = (uint)((int)vRef + change);
                                newVal = maxV > minV ? Math.Clamp(newVal, (uint)minV, (uint)maxV) : Math.Max(newVal, (uint)minV);
                                vRef = newVal;
                            } else if(isShort) {
                                ref short vRef = ref Unsafe.As<T, short>(ref value);
                                short newVal = (short)((int)vRef + change);
                                newVal = maxV > minV ? Math.Clamp(newVal, (short)minV, (short)maxV) : Math.Max(newVal, (short)minV);
                                vRef = newVal;
                            }
                            changed = true;
                        }
                    }
                    Win32Interop.SetDragCursorPos(lockState.X, lockState.Y);
                }
            }
        } else {
            if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);
            if(isHovered && !state.IsInputActive) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        }

        if(changed) {
            WasUsingDrag[id] = true;
            res = UIState.Using;
        }
        if(!isActive && wasUsing && !lockState.IsLocked) {
            WasUsingDrag[id] = false;
            res = UIState.Ended;
        }

        var draw = ImGui.GetWindowDrawList();
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();

        float itemHeight = itemMax.Y - itemMin.Y;
        float drawHeight = height > 0f ? height.Value : itemHeight;
        float outerLabelWidth = insetLabel || string.IsNullOrEmpty(label) ? 0f : ImGui.CalcTextSize(label).X + ImGui.GetStyle().ItemInnerSpacing.X;
        var frameMin = new Vector2(itemMin.X, itemMin.Y);
        var frameMax = new Vector2(itemMax.X - outerLabelWidth, itemMin.Y + drawHeight);

        var textPadding = 6f * ImGuiHelpers.GlobalScale;
        var barHeight = drawHeight * barHeightPercent;
        draw.AddRectFilled(frameMin, frameMax, ImGui.GetColorU32(bgCol), UIShared.InputRounding, ImDrawFlags.RoundCornersTop);

        var barMin = new Vector2(frameMin.X, frameMax.Y - barHeight);
        var barMax = frameMax;
        if(Math.Abs(maxV - minV) > float.Epsilon) {
            float curFloat = isInt ? Unsafe.As<T, int>(ref value) : isUInt ? Unsafe.As<T, uint>(ref value) : isShort ? Unsafe.As<T, short>(ref value) : Unsafe.As<T, float>(ref value);
            float t = Math.Clamp((curFloat - minV) / (maxV - minV), 0.01f, 1f);
            var fgMax = new Vector2(barMin.X + (barMax.X - barMin.X) * t, barMax.Y);
            var fgCol = disabled ? UIShared.DragFgDisabled : state.IsActive || state.IsInputActive ? UIShared.DragFgActive : state.IsHovered ? UIShared.DragFgHovered : UIShared.DragFgNormal;
            draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(bgCol));
            draw.AddRectFilled(barMin, fgMax, ImGui.GetColorU32(fgCol));
        }

        if(!state.IsInputActive) {
            using(UIShared.NormalFont.Push()) {
                string vText;
                if(isFloat) {
                    float fv = Unsafe.As<T, float>(ref value);
                    vText = fv.ToString(floatDisplayFormat);
                } else {
                    vText = (isInt ? Unsafe.As<T, int>(ref value).ToString() : isUInt ? Unsafe.As<T, uint>(ref value).ToString() : Unsafe.As<T, short>(ref value).ToString())!;
                }

                var vTextSize = ImGui.CalcTextSize(vText);
                var textPos = UiUtil.AlignCenter(frameMin, new Vector2(frameMax.X, frameMax.Y), vTextSize);
                if(insetLabel) {
                    var lTextPos = new Vector2(frameMin.X + textPadding, textPos.Y);
                    draw.AddText(lTextPos, ImGui.GetColorU32(textCol), label);
                    var vTextPos = new Vector2(frameMax.X - vTextSize.X - textPadding, textPos.Y);
                    draw.AddText(vTextPos, ImGui.GetColorU32(textCol), vText);
                } else {
                    draw.AddText(textPos, ImGui.GetColorU32(textCol), vText);
                }
            }

            if(!insetLabel && !string.IsNullOrEmpty(label)) {
                using(UIShared.NormalFont.Push()) {
                    var lTextSize = ImGui.CalcTextSize($"{label}");
                    var lTextPos = UiUtil.AlignCenter(frameMin, frameMax, lTextSize);
                    lTextPos = new Vector2(frameMax.X + ImGui.GetStyle().ItemInnerSpacing.X, lTextPos.Y);
                    draw.AddText(lTextPos, ImGui.GetColorU32(textCol), label);
                }
            }
        }

        var extraHeight = drawHeight - itemHeight;
        if(extraHeight > 0f) {
            ImGui.Dummy(new Vector2(0f, extraHeight));
        }
        ImGui.EndGroup();

        DragFocused[id] = new InteractionState() { 
            IsHovered = isHovered, 
            IsActive = isActive, 
            IsInputActive = isInputActive ? true : isInputDeactivated ? false : state.IsInputActive,
            IsDragging = lockState.IsLocked
        };
        DragLocked[id] = lockState;

        return res;
    }

    private static readonly ConcurrentDictionary<string, InteractionState> StyledInputFocused = new();
    private static readonly Dictionary<string, bool> WasUsingInput = [];
    public static UIState StyledInput(
    ImU8String label,
    ref string text,
    string hint = "",
    bool disabled = false,
    int maxLength = 512,
    float width = 0,
    ImGuiInputTextFlags flags = ImGuiInputTextFlags.AutoSelectAll,
    string? tooltip = null,
    string? tooltipSub = null,
    Action? onEnter = null,
    FontAwesomeIcon buttonIcon = FontAwesomeIcon.None,
    Action? onButtonClick = null,
    FontAwesomeIcon labelIcon = FontAwesomeIcon.None,
    string[]? autoCompleteList = null,
    int autoCompleteMaxItemsDisplayed = 3,
    Action<string>? onAutoCompleteSelection = null
) {
        width = width == 0 ? ImGui.GetContentRegionAvail().X : width;

        var labelId = label.ToString();
        ImGui.PushID(label);

        StyledInputFocused.TryGetValue(labelId, out var state);

        var bgCol = disabled ? UIShared.InputBgDisabled : state.IsActive ? UIShared.InputBgActive : state.IsHovered ? UIShared.InputBgHovered : UIShared.InputBgNormal;
        var textCol = disabled ? UIShared.InputTextDisabled : state.IsActive ? UIShared.InputTextActive : state.IsHovered ? UIShared.InputTextHovered : UIShared.InputTextNormal;
        var textPadding = UIShared.InputPadding * ImGuiHelpers.GlobalScale;

        var showButton = buttonIcon != FontAwesomeIcon.None;
        var showLabelIcon = labelIcon != FontAwesomeIcon.None;

        Action? enterAction = onEnter;
        Action? buttonAction = onButtonClick;

        if(showButton) {
            if(enterAction == null && buttonAction != null) {
                enterAction = buttonAction;
            } else if(buttonAction == null && enterAction != null) {
                buttonAction = enterAction;
            }
        }

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UIShared.InputRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, textPadding);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, textCol);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, UIShared.InputTextHint);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, UIShared.InputBgTextSelected);

        var draw = ImGui.GetWindowDrawList();
        var outerMin = ImGui.GetCursorScreenPos();

        var frameHeight = ImGui.GetFrameHeight();

        var labelWidth = showLabelIcon ? frameHeight : 0f;
        var labelOffset = showLabelIcon ? frameHeight * 0.8f : 0f;
        var buttonWidth = showButton ? frameHeight : 0f;
        var inputWidth = MathF.Max(1f, width - labelOffset - buttonWidth);

        draw.AddRectFilled(outerMin, outerMin + new Vector2(width, frameHeight), ImGui.GetColorU32(bgCol), UIShared.InputRounding);

        if(showLabelIcon) {
            var labelIconCol = disabled ? UIShared.IconDisabled : UIShared.IconNormal;
            using(UIShared.NormalIconFont.Push()) {
                var iconStr = labelIcon.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                var iconMin = outerMin;
                var iconMax = outerMin + new Vector2(labelWidth, frameHeight);
                var iconPos = (iconMin + iconMax) * 0.5f - (iconSize * 0.5f);
                draw.AddText(iconPos, ImGui.GetColorU32(labelIconCol), iconStr);
            }
        }

        if(disabled) ImGui.BeginDisabled();

        ImGui.SetCursorScreenPos(outerMin + new Vector2(labelOffset, 0f));
        ImGui.SetNextItemWidth(inputWidth);

        var changed = ImGui.InputTextWithHint(label, hint, ref text, maxLength, flags);
        var focused = ImGui.IsItemFocused();
        var inputHovered = ImGui.IsItemHovered();
        var inputActive = ImGui.IsItemActive();
        var inputMin = ImGui.GetItemRectMin();
        var inputMax = ImGui.GetItemRectMax();

        bool buttonHovered = false;
        bool buttonActive = false;

        if(showButton) {
            ImGui.SameLine(0, 0);

            var buttonMin = ImGui.GetCursorScreenPos();
            var buttonMax = buttonMin + new Vector2(buttonWidth, frameHeight);

            var buttonClicked = ImGui.InvisibleButton($"{labelId}button", new Vector2(buttonWidth, frameHeight));
            buttonHovered = ImGui.IsItemHovered();
            buttonActive = ImGui.IsItemActive();

            var buttonBg = disabled
                ? UIShared.InputBgDisabled
                : buttonActive
                    ? UIShared.InputBgActive
                    : buttonHovered
                        ? UIShared.InputBgHovered
                        : bgCol;

            draw.AddRectFilled(
                buttonMin,
                buttonMax,
                ImGui.GetColorU32(buttonBg),
                UIShared.InputRounding);

            var iconCol = disabled
                ? UIShared.IconDisabled
                : buttonActive
                    ? UIShared.IconActive
                    : buttonHovered
                        ? UIShared.IconHovered
                        : UIShared.IconNormal;

            using(UIShared.NormalIconFont.Push()) {
                var iconStr = buttonIcon.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                var iconPos = (buttonMin + buttonMax) * 0.5f - (iconSize * 0.5f);
                draw.AddText(iconPos, ImGui.GetColorU32(iconCol), iconStr);
            }

            if(buttonClicked && !disabled) {
                buttonAction?.Invoke();
            }
        }

        var controlHovered = ImGui.IsMouseHoveringRect(outerMin, outerMin + new Vector2(width, frameHeight));

        if(tooltip != null && controlHovered) {
            Tooltip.Show(tooltip, tooltipSub);
        }

        var res = UIState.None;
        var wasUsing = WasUsingInput.TryGetValue(labelId, out var w) && w;

        if(changed) {
            WasUsingInput[labelId] = true;
            res = UIState.Using;
        }

        if(!inputActive && wasUsing) {
            WasUsingInput[labelId] = false;
            res = UIState.Ended;
        }

        if(enterAction != null && focused && ImGui.IsKeyPressed(ImGuiKey.Enter)) {
            enterAction.Invoke();
        }

        if(disabled) ImGui.EndDisabled();

        if(state.IsActive && autoCompleteList?.Length > 0) {
            var popupWidth = width;
            var popupHeight = MathF.Min(autoCompleteList.Length, autoCompleteMaxItemsDisplayed) * (inputMax.Y - inputMin.Y);
            var popupMin = new Vector2(inputMin.X, inputMax.Y);
            var popupMax = popupMin + new Vector2(popupWidth, popupHeight);

            draw.AddRectFilled(popupMin, popupMax, ImGui.GetColorU32(UIShared.ContextMenuBg), UIShared.InputRounding);

            for(int i = 0; i < autoCompleteList.Length && i < autoCompleteMaxItemsDisplayed; i++) {
                var s = autoCompleteList[i];
                var rowMin = popupMin + new Vector2(0, i * (inputMax.Y - inputMin.Y));
                var rowMax = rowMin + new Vector2(popupWidth, inputMax.Y - inputMin.Y);

                var rowHovered = UiUtil.IsRectHovered(rowMin, rowMax);
                if(rowHovered) {
                    inputActive = true;
                    draw.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(UIShared.ContextItemBgHovered), UIShared.InputRounding);
                }

                if(UiUtil.IsRectClicked(rowMin, rowMax, ImGuiMouseButton.Left)) {
                    onAutoCompleteSelection?.Invoke(s);
                }

                using(UIShared.SubFont.Push()) {
                    var col = rowHovered ? UIShared.ContextItemTextHovered : UIShared.ContextItemTextNormal;
                    draw.AddText(
                        new Vector2(rowMin.X + textPadding.X, rowMin.Y + ((rowMax.Y - rowMin.Y - ImGui.GetFontSize()) * 0.5f)),
                        ImGui.GetColorU32(col),
                        s
                    );
                }
            }
        }

        var newState = new InteractionState() {
            IsHovered = controlHovered || buttonHovered,
            IsActive = inputActive || buttonActive
        };
        StyledInputFocused[labelId] = newState;

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
        ImGui.PopID();

        return res;
    }

    private static Vector2 DrawComboButton(string id, string text, string valueText, float width, float height, bool disabled, out bool isHovered, out bool isActive, out bool isClicked, out bool isOpen) {
        isClicked = ImGui.InvisibleButton($"{text}{valueText}", new Vector2(width, height));
        isHovered = ImGui.IsItemHovered();
        isActive = ImGui.IsItemActive();
        isOpen = ImGui.IsPopupOpen(id);
        var itemMin = ImGui.GetItemRectMin();
        var draw = ImGui.GetWindowDrawList();

        var bgCol = disabled ? UIShared.InputBgDisabled : isActive || isOpen ? UIShared.InputBgActive : isHovered ? UIShared.InputBgHovered : UIShared.InputBgNormal;
        draw.AddRectFilled(itemMin, itemMin + new Vector2(width, height), ImGui.GetColorU32(bgCol), UIShared.InputRounding);

        var iconPadding = 6f * ImGuiHelpers.GlobalScale;
        Vector2 checkPos;
        Vector2 checkSize;
        using(UIShared.NormalIconFont.Push()) {
            var icon = isOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
            var checkCol = disabled ? UIShared.IconDisabled : isActive || isOpen ? UIShared.IconActive : isHovered ? UIShared.IconHovered : UIShared.IconNormal;
            var center = (itemMin + ImGui.GetItemRectMax()) * 0.5f;
            checkSize = ImGui.CalcTextSize(FontAwesomeIcon.CaretDown.ToIconString());
            checkPos = new Vector2(itemMin.X + iconPadding, center.Y - (checkSize.Y * 0.5f));
            draw.AddText(checkPos, ImGui.GetColorU32(checkCol), icon.ToIconString());
        }

        using(UIShared.NormalFont.Push()) {
            var textCol = disabled ? UIShared.IconLabelDisabled : isActive ? UIShared.IconLabelActive : isHovered ? UIShared.IconLabelHovered : UIShared.IconLabelNormal;
            draw.AddText(checkPos + new Vector2(checkSize.X + iconPadding, 0), ImGui.GetColorU32(textCol), $"{text}{valueText}");
        }

        return itemMin;
    }

    private static bool DrawComboPopup<TItem>(string id, Vector2 anchorPos, float width, float itemHeight, IReadOnlyList<TItem> items, int maxItemsDisplayed, bool closeOnSelection, Func<TItem, string> labelOf, Func<TItem, bool> isSelected, Func<TItem, bool> onClick, Action? drawHeader, Func<TItem, FontAwesomeIcon?>? prefixIcon = null) {
        if(items == null || items.Count == 0) return false;
        var totalItems = items.Count;
        var displayCount = Math.Min(totalItems, Math.Max(1, maxItemsDisplayed));
        var headerHeight = drawHeader != null ? itemHeight : 0f;
        var popupHeight = headerHeight + (itemHeight * displayCount);

        ImGui.SetNextWindowPos(anchorPos, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(width, popupHeight), ImGuiCond.Appearing);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, UIShared.InputRounding);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);

        var changed = false;
        if(ImGui.BeginPopup(id, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings)) {
            var draw = ImGui.GetWindowDrawList();

            draw.AddRectFilled(anchorPos, anchorPos + new Vector2(width, popupHeight), ImGui.GetColorU32(UIShared.ContextMenuBg), UIShared.InputRounding);
            draw.AddRect(anchorPos, anchorPos + new Vector2(width, popupHeight), ImGui.GetColorU32(UIShared.ContextMenuBorder), UIShared.InputRounding);

            if(headerHeight > 0f) {
                ImGui.BeginChild($"{id}header", new Vector2(width, headerHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                drawHeader?.Invoke();
                ImGui.EndChild();
            }

            var contentHeight = popupHeight - headerHeight;
            ImGui.BeginChild($"{id}content", new Vector2(width, contentHeight));
            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();
            draw.PushClipRect(clipMin, clipMax, true);
            for(int i = 0; i < totalItems; i++) {
                var item = items[i];

                ImGui.InvisibleButton($"{id}{i}dummy", new Vector2(width, itemHeight));
                var rowMin = ImGui.GetItemRectMin();
                var rowMax = ImGui.GetItemRectMax();
                var rowHovered = ImGui.IsItemHovered();
                var selected = isSelected(item);

                if(rowHovered || selected) {
                    draw.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(rowHovered ? UIShared.ContextItemBgHovered : UIShared.ContextItemBgActive), UIShared.InputRounding);
                }

                if(ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                    var selectedChanged = onClick(item);
                    changed = changed || selectedChanged;
                    if(closeOnSelection) ImGui.CloseCurrentPopup();
                }

                float labelX = rowMin.X + UIShared.ComboItemPadding;
                if(prefixIcon != null) {
                    var icon = prefixIcon(item);
                    if(icon != null) {
                        using(UIShared.SubIconFont.Push()) {
                            var iconStr = icon.Value.ToIconString();
                            var iconSize = ImGui.CalcTextSize(iconStr);
                            if(icon != FontAwesomeIcon.Square) {
                                var iconCol = selected ? UIShared.ContextItemTextActive : (rowHovered ? UIShared.ContextItemTextHovered : UIShared.ContextItemTextNormal);
                                draw.AddText(new Vector2(rowMin.X + UIShared.ComboItemPadding, rowMin.Y + ((rowMax.Y - rowMin.Y - iconSize.Y) * 0.5f)), ImGui.GetColorU32(iconCol), iconStr);
                            }
                            labelX += iconSize.X + UIShared.ComboItemPadding;
                        }
                    }
                }

                using(UIShared.SubFont.Push()) {
                    var col = rowHovered ? UIShared.ContextItemTextHovered : selected ? UIShared.ContextItemTextActive : UIShared.ContextItemTextNormal;
                    draw.AddText(new Vector2(labelX, rowMin.Y + ((rowMax.Y - rowMin.Y - ImGui.GetFontSize()) * 0.5f)), ImGui.GetColorU32(col), labelOf(item));
                }
            }
            draw.PopClipRect();
            ImGui.EndChild();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
        return changed;
    }

    private static string GetComboValueText(string valueText, ComboButtonDisplayType displayType) {
        switch(displayType) {
            case ComboButtonDisplayType.Label:
                return string.Empty;
            case ComboButtonDisplayType.SelectionCount:
                var itemCount = valueText.Split(',').Length;
                return $"{itemCount} Selected";
        }
        return valueText;
    }

    public static bool EnumCombo<T>(string id, string text, ref T value, ComboButtonDisplayType displayType = ComboButtonDisplayType.Label, bool disabled = false, string? tooltip = null, string? tooltipSub = null, int maxItemsDisplayed = 6, float width = 0, float? height = null, Action? drawHeader = null, T? ignoredValue = null) where T : struct, Enum {
        width = width == 0 ? ImGui.GetContentRegionAvail().X : width;
        height = height == null ? UIShared.LineHeight : height == 0 ? ImGui.GetFrameHeight() : height;

        ImGui.PushID(id);
        var valueText = GetComboValueText(value.ToString(), displayType);
        var itemMin = DrawComboButton(id, text, valueText, width, height.Value, disabled, out var isHovered, out var isActive, out var isClicked, out var isOpen);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);
        if(isClicked && !disabled) ImGui.OpenPopup(id);
        if(!isOpen) { ImGui.PopID(); return false; }

        var enumValues = ignoredValue == null ? Enum.GetValues<T>() : Enum.GetValues<T>().Where(x => !EqualityComparer<T>.Default.Equals(x, ignoredValue.Value)).ToArray();
        if(enumValues.Length == 0) { ImGui.PopID(); return false; }
        var localValue = value;

        bool changed = DrawComboPopup(id, new Vector2(itemMin.X, itemMin.Y + height.Value), width, height.Value,
            enumValues, maxItemsDisplayed, true,
            item => item.ToString(),
            item => EqualityComparer<T>.Default.Equals(localValue, item),
            item => { if(!EqualityComparer<T>.Default.Equals(localValue, item)) { localValue = item; return true; } return false; },
            drawHeader
        );
        if(changed) value = localValue;

        ImGui.PopID();
        return changed;
    }

    public static bool EnumFlagsCombo<T>(string id, string text, ref T value, ComboButtonDisplayType displayType = ComboButtonDisplayType.Label, bool disabled = false, string? tooltip = null, string? tooltipSub = null, int maxItemsDisplayed = 6, float width = 0, float? height = null, Action? drawHeader = null) where T : struct, Enum {
        width = width == 0 ? ImGui.GetContentRegionAvail().X : width;
        height = height == null ? UIShared.LineHeight : height == 0 ? ImGui.GetFrameHeight() : height;

        ImGui.PushID(id);
        var valueText = GetComboValueText(value.ToString(), displayType);
        var itemMin = DrawComboButton(id, text, valueText, width, height.Value, disabled, out var isHovered, out var isActive, out var isClicked, out var isOpen);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);
        if(isClicked && !disabled) ImGui.OpenPopup(id);
        if(!isOpen) { ImGui.PopID(); return false; }

        var enumValues = Enum.GetValues<T>();
        if(enumValues.Length == 0) { ImGui.PopID(); return false; }
        var localValue = value;
        var curBits = Convert.ToUInt64(localValue);
        var enumBits = enumValues.Select(v => (Value: v, Bits: Convert.ToUInt64(v))).Where(x => x.Bits == 0 || (x.Bits & (x.Bits - 1)) == 0).ToArray();

        Func<(T Value, ulong Bits), string> labelOf = item => item.Value.ToString();
        Func<(T Value, ulong Bits), bool> isSelected = item => {
            if(item.Bits == 0) return curBits == 0;
            return (curBits & item.Bits) == item.Bits;
        };
        Func<(T Value, ulong Bits), FontAwesomeIcon?> prefixIcon = item => {
            bool selected = item.Bits == 0 ? curBits == 0 : (curBits & item.Bits) == item.Bits;
            return selected ? FontAwesomeIcon.CheckSquare : FontAwesomeIcon.Square;
        };

        bool changed = DrawComboPopup(id, new Vector2(itemMin.X, itemMin.Y + height.Value), width, height.Value,
            enumBits, maxItemsDisplayed, false,
            labelOf,
            isSelected,
            item => {
                ulong newBits = item.Bits == 0 ? 0UL : curBits ^ item.Bits;
                var newVal = (T)Enum.ToObject(typeof(T), newBits);
                if(!EqualityComparer<T>.Default.Equals(localValue, newVal)) {
                    localValue = newVal;
                    curBits = newBits;
                    return true;
                }
                return false;
            },
            drawHeader,
            prefixIcon
        );
        if(changed) value = localValue;

        ImGui.PopID();
        return changed;
    }

    public static bool ListCombo<TKey>(string id, string text, string hintText, ref TKey selectedId, IEnumerable<(TKey id, string label)> items, ComboButtonDisplayType displayType = ComboButtonDisplayType.Items, bool disabled = false, string? tooltip = null, string? tooltipSub = null, int maxItemsDisplayed = 6, float width = 0, float? height = null, Action? drawHeader = null) where TKey : struct, IEquatable<TKey> {
        width = width == 0 ? ImGui.GetContentRegionAvail().X : width;
        height = height == null ? UIShared.LineHeight : height == 0 ? ImGui.GetFrameHeight() : height;

        ImGui.PushID(id);
        var list = items.ToList();
        string valueText = hintText;
        var localValue = selectedId;
        if(displayType == ComboButtonDisplayType.Items) {
            var label = list.FirstOrDefault(x => x.id.Equals(localValue)).label;
            if(label != null) valueText = GetComboValueText(label, displayType);
        } else {
            valueText = GetComboValueText(valueText, displayType);
        }
        var itemMin = DrawComboButton(id, text, valueText, width, height.Value, disabled, out var isHovered, out var isActive, out var isClicked, out var isOpen);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);
        if(isClicked && !disabled) ImGui.OpenPopup(id);
        if(!isOpen) { ImGui.PopID(); return false; }

        if(list.Count == 0) { ImGui.PopID(); return false; }

        bool changed = DrawComboPopup(id, new Vector2(itemMin.X, itemMin.Y + height.Value), width, height.Value,
            list, maxItemsDisplayed, true,
            item => item.label,
            item => EqualityComparer<TKey>.Default.Equals(localValue, item.id),
            item => { if(!EqualityComparer<TKey>.Default.Equals(localValue, item.id)) { localValue = item.id; return true; } return false; },
            drawHeader
        );
        if(changed) selectedId = localValue;

        ImGui.PopID();
        return changed;
    }

    private static float GetEffectiveWrapWidth(bool multiline, float? width, float? wrapWidth = float.MaxValue, float xPadding = 0) {
        if(!multiline) return wrapWidth ?? float.MaxValue;
        if(wrapWidth.HasValue && wrapWidth.Value != float.MaxValue) return wrapWidth.Value - (xPadding * 2);
        if(width.HasValue) return width.Value - (xPadding * 2);
        return ImGui.GetContentRegionAvail().X - (xPadding * 2);
    }

    public static void StyledText(ImU8String text, float? fontSize = null, float opacity = 0.8f, float bgOpacity = 0f, float bgRounding = 4f, float glowStrength = 0.2f, AnimationType animationType = AnimationType.Static, Vector3? colorA = null, Vector3? colorB = null, Vector3? glowA = null, Vector3? glowB = null, Vector3? bgColor = null, float? xPadding = null, float? yPadding = null, float? width = null, float? wrapWidth = float.MaxValue, bool multiline = false, string? tooltip = null, string? tooltipSub = null, Action? action = null, ImDrawListPtr? targetDrawList = null, Vector2? screenOffset = null, Vector2? clipMin = null, Vector2? clipMax = null) {
        xPadding ??= (bgOpacity > 0f ? UIShared.TextBgPadding.X : 0);
        yPadding ??= (bgOpacity > 0f ? UIShared.TextBgPadding.Y : 0);

        var drawMin = screenOffset ?? ImGui.GetCursorScreenPos();
        var effectiveWrapWidth = GetEffectiveWrapWidth(multiline, width, wrapWidth, xPadding!.Value);
        var textSize = ImGui.CalcTextSize(text, true, effectiveWrapWidth);
        var totalSize = new Vector2(textSize.X + (xPadding.Value * 2f), textSize.Y + (yPadding!.Value * 2f));
        var drawMax = drawMin + totalSize;
        if(targetDrawList == null) {
            if(action != null) ImGui.InvisibleButton($"##{Guid.NewGuid()}", totalSize); else ImGui.Dummy(totalSize);
        }

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub, drawMin, drawMax);
        if(action != null) {
            if(UiUtil.IsRectHovered(drawMin, drawMax)) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if(UiUtil.IsRectClicked(drawMin, drawMax)) {
                    action();
                }
            }
        }

        BuildStyledText(drawMin, totalSize, text, fontSize, opacity, bgOpacity, bgRounding, glowStrength, animationType, colorA, colorB, glowA, glowB, bgColor, xPadding.Value, yPadding.Value, effectiveWrapWidth, targetDrawList, screenOffset, clipMin, clipMax);
    }

    public static bool Container(ImU8String text, Dictionary<uint, bool> expandedStates, bool defaultExpanded = false, float? width = null, AnimationType animationType = AnimationType.Static, Vector3? colorA = null, Vector3? colorB = null, Vector3? glowA = null, Vector3? glowB = null) {
        var id = ImGui.GetID(text);
        ImGui.PushID(id);

        if(!expandedStates.TryGetValue(id, out var isExpanded)) {
            isExpanded = defaultExpanded;
            expandedStates[id] = isExpanded;
        }

        var padding = new Vector2(4f, 4f);
        var iconPadding = 4f;
        var rounding = 3f;
        var bgNormal = UIShared.IconTextBgNormal;
        var bgHovered = UIShared.IconTextBgHovered;
        var bgActive = UIShared.IconTextBgClicked;
        var fgNormal = UIShared.IconTextNormal;
        var fgHovered = UIShared.IconTextHovered;
        var fgActive = UIShared.IconTextActive;
        var icon = isExpanded ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
        var iconText = icon.ToIconString();

        var iconSize = UiUtil.CalcTextSize(UIShared.NormalIconFont, FontAwesomeIcon.CaretDown.ToIconString());
        var labelSize = ImGui.CalcTextSize(text);
        var iconRegion = new Vector2(padding.X + iconSize.X + iconPadding, MathF.Max(iconSize.Y, labelSize.Y) + padding.Y * 2f);
        var textRegion = new Vector2(padding.X + labelSize.X, MathF.Max(iconSize.Y, labelSize.Y) + padding.Y * 2f);
        var totalSize = new Vector2(width ?? (iconRegion.X + textRegion.X), iconRegion.Y);

        var clicked = ImGui.InvisibleButton("##btn", totalSize);
        if(clicked) {
            isExpanded = !isExpanded;
            expandedStates[id] = isExpanded;
        }
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();

        var bgCol = active ? bgActive : hovered ? bgHovered : bgNormal;
        var fgCol = active ? fgActive : hovered ? fgHovered : fgNormal;
        draw.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(bgCol), rounding);

        var contentHeight = MathF.Max(iconSize.Y, labelSize.Y);

        using(UIShared.NormalIconFont.Push()) {
            var iconPos = new Vector2(rectMin.X + padding.X, rectMin.Y + (totalSize.Y - iconSize.Y) * 0.5f);
            draw.AddText(iconPos, ImGui.GetColorU32(fgCol), iconText);
        }

        var textPos = new Vector2(rectMin.X + iconRegion.X, rectMin.Y + (totalSize.Y - labelSize.Y) * 0.5f);
        BuildStyledText(textPos, labelSize, text, animationType: animationType, colorA: colorA, colorB: colorB, glowA: glowA, glowB: glowB);

        ImGui.PopID();
        return isExpanded;
    }

    private static bool BuildStyledText(Vector2 drawPos, Vector2 size, ImU8String text, float? fontSize = null, float opacity = 0.8f, float bgOpacity = 0f, float bgRounding = 4f, float glowStrength = 0.2f, AnimationType animationType = AnimationType.Static, Vector3? colorA = null, Vector3? colorB = null, Vector3? glowA = null, Vector3? glowB = null, Vector3? bgColor = null, float xPadding = 0f, float yPadding = 0f, float? wrapWidth = float.MaxValue, ImDrawListPtr? targetDrawList = null, Vector2? screenOffset = null, Vector2? clipMin = null, Vector2? clipMax = null) {
        var cursorPos = ImGui.GetCursorScreenPos();
        var dl = targetDrawList ?? ImGui.GetWindowDrawList();
        Vector2 cMin;
        Vector2 cMax;
        if(clipMin == null || clipMax == null) {
            cMin = Vector2.Max(drawPos, ImGui.GetWindowPos());
            cMax = Vector2.Min(drawPos + size, ImGui.GetWindowPos() + ImGui.GetWindowSize());
        } else {
            cMin = clipMin.Value;
            cMax = clipMax.Value;
        }
        if(cMin.X >= cMax.X || cMin.Y >= cMax.Y) return false;
        dl.PushClipRect(cMin, cMax, true);
        ImGui.SetCursorScreenPos(cMin);
        var textPos = drawPos + new Vector2(xPadding, yPadding);

        try {
            if(text.IsEmpty) return false;

            colorA ??= Vector3.One;
            colorB ??= colorA;
            glowA ??= colorA;
            glowB ??= glowA;
            var colorSame = colorA == colorB;
            var glowSame = glowA == glowB;

            if(bgOpacity > 0f) {
                var bgCol = bgColor == null ? new Vector4(colorA.Value, opacity * bgOpacity) : new Vector4(bgColor.Value, bgOpacity);
                dl.AddRectFilled(cMin, cMax, ImGui.GetColorU32(bgCol), bgRounding * ImGuiHelpers.GlobalScale);
            }

            var t = text.ToString();
            var builder = new SeStringBuilder();
            if(animationType is not (AnimationType.RainbowWave or AnimationType.RainbowPulse) && colorSame && glowSame) {
                builder.PushColorRgba(new Vector4(colorA.Value, 1f));
                builder.PushEdgeColorRgba(new Vector4(glowA.Value, 1f));
                builder.Append(t);
                builder.PopEdgeColor();
                builder.PopColor();
            } else {
                var time = (float)ImGui.GetTime();
                var visibleIndex = 0;
                var visibleCount = t.Count(c => c != '\r' && c != '\n');
                foreach(var ch in t) {
                    if(ch == '\r' || ch == '\n') {
                        builder.Append(ch.ToString());
                        //visibleIndex = 0; // per line gradient
                        continue;
                    }

                    var posNorm = visibleCount > 1 ? (float)visibleIndex / (visibleCount - 1) : 0f;
                    float tColor;
                    float tGlow;
                    if(animationType == AnimationType.Wave) {
                        const float speed = 0.5f;
                        var shifted = posNorm + time * speed;
                        var p = shifted - MathF.Floor(shifted);
                        tColor = p <= 0.5f ? (p * 2f) : (1f - (p - 0.5f) * 2f);
                        tGlow = tColor;
                    } else if(animationType == AnimationType.Chase) {
                        const float speed = 0.6f;
                        var center = (time * speed) % 1f;
                        var dist = MathF.Abs(posNorm - center);
                        var cWidth = 0.15f;
                        var w = 1f - (dist / cWidth);
                        if(w < 0f) w = 0f; else if(w > 1f) w = 1f;
                        var smooth = w * w * (3f - 2f * w);
                        tColor = smooth;
                        tGlow = smooth;
                    } else if(animationType == AnimationType.Pulse) {
                        const float freq = 0.2f;
                        tColor = 0.5f * (1f + MathF.Sin(2f * MathF.PI * time * freq));
                        tGlow = tColor;
                    } else if(animationType == AnimationType.EasePulse) {
                        const float freq = 0.6f;
                        var raw = 0.5f * (1f + MathF.Sin(2f * MathF.PI * time * freq));
                        var eased = raw * raw * (3f - 2f * raw);
                        tColor = eased;
                        tGlow = eased;
                    } else if(animationType == AnimationType.RainbowWave) {
                        var hue = (posNorm + time * 0.15f) % 1f;
                        var rgbVec = HsvToRgbVec(hue, 0.9f, 0.95f);
                        builder.PushColorRgba(new Vector4(rgbVec, 1f));
                        builder.PushEdgeColorRgba(new Vector4(rgbVec * 0.25f, 1f));
                        builder.Append(ch.ToString());
                        builder.PopEdgeColor();
                        builder.PopColor();
                        visibleIndex++;
                        continue;
                    } else if(animationType == AnimationType.RainbowPulse) {
                        var hue = (time * 0.15f) % 1f;
                        var rgbVec = HsvToRgbVec(hue, 0.9f, 0.95f);
                        builder.PushColorRgba(new Vector4(rgbVec, 1f));
                        builder.PushEdgeColorRgba(new Vector4(rgbVec * 0.25f, 1f));
                        builder.Append(ch.ToString());
                        builder.PopEdgeColor();
                        builder.PopColor();
                        visibleIndex++;
                        continue;
                    } else {
                        tColor = posNorm;
                        tGlow = posNorm;
                    }

                    var tc = Math.Clamp(tColor, 0f, 1f);
                    var tg = Math.Clamp(tGlow, 0f, 1f);
                    var blendedColor = colorSame ? colorA.Value : Vector3.Lerp(colorA.Value, colorB.Value, tc);
                    var blendedGlow = glowSame ? glowA.Value : Vector3.Lerp(glowA.Value, glowB.Value, tg);
                    builder.PushColorRgba(new Vector4(blendedColor, 1f));
                    builder.PushEdgeColorRgba(new Vector4(blendedGlow, 1f));
                    builder.Append(ch.ToString());
                    builder.PopEdgeColor();
                    builder.PopColor();

                    visibleIndex++;
                }
            }

            var result = SeString.Parse(builder.GetViewAsSpan()).Encode();
            var dp = new SeStringDrawParams {
                Color = ImGui.GetColorU32(new Vector4(colorA.Value, 1f)),
                Opacity = opacity,
                Edge = glowStrength > 0,
                EdgeColor = ImGui.GetColorU32(new Vector4(glowA.Value, 1f)),
                EdgeStrength = glowStrength,
                Font = ImGui.GetFont(),
                FontSize = fontSize == null ? ImGui.GetFontSize() : fontSize * ImGuiHelpers.GlobalScale,
                WrapWidth = wrapWidth,
                TargetDrawList = dl,
                ScreenOffset = screenOffset ?? textPos
            };
            ImGuiHelpers.SeStringWrapped(result, dp);
        } finally {
            dl.PopClipRect();
            ImGui.SetCursorScreenPos(cursorPos);
        }
        return true;
    }

    private static Vector3 HsvToRgbVec(float h, float s, float v) {
        var (r, g, b) = HsvToRgb(h, s, v);
        return new Vector3(r, g, b);
    }

    private static (float, float, float) HsvToRgb(float h, float s, float v) {
        if(s <= 0f) return (v, v, v);
        h = (h % 1f + 1f) % 1f;
        float hh = h * 6f;
        int i = (int)MathF.Floor(hh);
        float ff = hh - i;
        float p = v * (1f - s);
        float q = v * (1f - (s * ff));
        float t = v * (1f - (s * (1f - ff)));
        return i switch {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }

    public static void IconLabel(FontAwesomeIcon icon, string id, string? tooltip = null, string? tooltipSub = null, float? size = null, float iconScale = 1.0f, Vector4? color = null, bool hover = true) {
        ImGui.PushID(id);

        if(size == null) {
            size = UIShared.LineHeight;
        } else if(size == 0) {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            size = ImGui.GetFrameHeight();
            ImGui.PopStyleVar();
        }

        ImGui.InvisibleButton(icon.ToIconString(), new(size.Value, size.Value));
        var hovered = hover && ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        ImGui.SetWindowFontScale(iconScale);
        using(UIShared.NormalIconFont.Push()) {
            var checkCol = color ?? (hovered ? UIShared.IconHovered : UIShared.IconNormal);
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
            var checkSize = ImGui.CalcTextSize(icon.ToIconString());
            var checkPos = center - (checkSize * 0.5f);
            draw.AddText(checkPos, ImGui.GetColorU32(checkCol), icon.ToIconString());
        }
        ImGui.SetWindowFontScale(1f);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);

        ImGui.PopID();
    }

    public static bool IconButton(FontAwesomeIcon icon, string id, bool disabled = false, string? tooltip = null, string? tooltipSub = null, float? size = null, float iconScale = 1.0f) {
        ImGui.PushID(id);

        if(size == null) {
            size = UIShared.NormalIconSize * iconScale;
        } else if(size == 0) {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            size = ImGui.GetFrameHeight();
            ImGui.PopStyleVar();
        }

        var clicked = ImGui.InvisibleButton(icon.ToIconString(), new(size.Value, size.Value));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var draw = ImGui.GetWindowDrawList();
        ImGui.SetWindowFontScale(iconScale);
        using(UIShared.NormalIconFont.Push()) {
            var iconCol = disabled ? UIShared.IconDisabled : active ? UIShared.IconActive : hovered ? UIShared.IconHovered : UIShared.IconNormal;
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
            var iconSize = ImGui.CalcTextSize(icon.ToIconString());
            var iconPos = center - (iconSize * 0.5f);
            draw.AddText(iconPos, ImGui.GetColorU32(iconCol), icon.ToIconString());
        }
        ImGui.SetWindowFontScale(1f);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);

        ImGui.PopID();

        return clicked && !disabled;
    }

    public static bool IconToggleButton(FontAwesomeIcon icon, string label, ref bool value, bool disabled = false, string? tooltip = null, string? tooltipSub = null, float? size = null, float iconScale = 1.0f, FontAwesomeIcon? toggledIcon = null) {
        ImGui.PushID(label);
        if(label.StartsWith("##")) label = string.Empty;
        if(label.Contains("##")) label = label.Split("##")[0];
        icon = value && toggledIcon.HasValue ? toggledIcon.Value : icon;
        var spacing = 2f * ImGuiHelpers.GlobalScale;

        if(size == null) {
            size = UIShared.NormalIconSize * iconScale;
        } else if(size == 0) {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            size = ImGui.GetFrameHeight();
            ImGui.PopStyleVar();
        }

        var clicked = ImGui.InvisibleButton(icon.ToIconString(), new(size.Value, size.Value));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var draw = ImGui.GetWindowDrawList();
        Vector2 iconPos;
        ImGui.SetWindowFontScale(iconScale);
        using(UIShared.NormalIconFont.Push()) {
            var iconCol = disabled ? UIShared.IconDisabled : active ? UIShared.IconActive : hovered ? UIShared.IconHovered : value ? UIShared.IconToggled : UIShared.IconNormal;
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
            var iconSize = ImGui.CalcTextSize(icon.ToIconString());
            iconPos = center - (iconSize * 0.5f);
            draw.AddText(iconPos, ImGui.GetColorU32(iconCol), icon.ToIconString());
        }
        ImGui.SetWindowFontScale(1f);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);

        if(!string.IsNullOrWhiteSpace(label)) {
            using(UIShared.NormalFont) {
                var textCol = disabled ? UIShared.IconLabelDisabled : active ? UIShared.IconLabelActive : hovered ? UIShared.IconLabelHovered : value ? UIShared.IconLabelToggled : UIShared.IconLabelNormal;
                ImGui.SameLine(0, spacing);
                var cursorPosX = ImGui.GetCursorScreenPos().X;
                ImGui.SetCursorScreenPos(new Vector2(cursorPosX, iconPos.Y));
                ImGui.TextColored(ImGui.GetColorU32(textCol), label);
            }
        }

        ImGui.PopID();

        var canUpdate = clicked && !disabled;
        if(canUpdate) value = !value;
        return canUpdate;
    }

    public static bool IconToggleButton(FontAwesomeIcon icon, string label, bool value, bool disabled = false, string? tooltip = null, string? tooltipSub = null, float? size = null, float iconScale = 1.0f, FontAwesomeIcon? toggledIcon = null) {
        return IconToggleButton(icon, label, ref value, disabled, tooltip, tooltipSub, size, iconScale, toggledIcon);
    }

    public static bool Checkbox(string label, ref bool value, bool disabled = false, string? tooltip = null, string? tooltipSub = null, float? size = null) {
        return IconToggleButton(FontAwesomeIcon.Square, label, ref value, disabled, tooltip, tooltipSub, size, 1.0f, FontAwesomeIcon.CheckSquare);
    }

    public static bool IconTextButton(FontAwesomeIcon icon, string text, string id, bool disabled = false, string? tooltip = null, string? tooltipSub = null, float? width = null, float? height = null, float iconScale = 0.8f) {
        ImGui.PushID(id);

        var padding = UIShared.IconTextPadding;
        var iconPadding = UIShared.IconTextPadding;

        var bgNormal = UIShared.IconTextBgNormal;
        var bgHovered = UIShared.IconTextBgHovered;
        var bgActive = UIShared.IconTextBgClicked;
        var bgDisabled = UIShared.IconTextBgActive;

        var fgNormal = UIShared.IconTextNormal;
        var fgHovered = UIShared.IconTextHovered;
        var fgActive = UIShared.IconTextActive;
        var fgDisabled = UIShared.IconTextDisabled;

        var iconText = icon.ToIconString();

        var iconSize = UiUtil.CalcTextSize(UIShared.NormalIconFont, iconText, iconScale);
        var labelSize = ImGui.CalcTextSize(text);
        var finalSize = UiUtil.CalcIconTextSize(icon, text, iconScale);
        if(width != null) finalSize.X = width.Value;
        if(height != null) finalSize.Y = height.Value;

        var clicked = ImGui.InvisibleButton("##btn", finalSize);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();

        var bgCol = disabled ? bgDisabled : active ? bgActive : hovered ? bgHovered : bgNormal;
        var fgCol = disabled ? fgDisabled : active ? fgActive : hovered ? fgHovered : fgNormal;

        draw.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(bgCol), UIShared.IconTextRounding);

        var contentHeight = MathF.Max(iconSize.Y, labelSize.Y);
        var contentY = rectMin.Y + (finalSize.Y - contentHeight) * 0.5f;

        ImGui.SetWindowFontScale(iconScale);
        using(UIShared.NormalIconFont.Push()) {
            var iconPos = new Vector2(rectMin.X + padding, rectMin.Y + (finalSize.Y - iconSize.Y) * 0.5f);
            draw.AddText(iconPos, ImGui.GetColorU32(fgCol), iconText);
        }
        ImGui.SetWindowFontScale(1f);

        var textPos = new Vector2(rectMin.X + padding + iconSize.X + iconPadding, rectMin.Y + (finalSize.Y - labelSize.Y) * 0.5f);
        draw.AddText(textPos, ImGui.GetColorU32(fgCol), text);

        if(tooltip != null) Tooltip.Show(tooltip, tooltipSub);

        ImGui.PopID();
        return clicked && !disabled;
    }

    public static bool AxisXDrag(string id, ref float value, float width, float speed = 0.001f) {
        return AxisDrag(id, ref value, new(0.8f, 0.2f, 0.2f, 0.7f), width, speed);
    }
    public static bool AxisYDrag(string id, ref float value, float width, float speed = 0.001f) {
        return AxisDrag(id, ref value, new(0.2f, 0.8f, 0.2f, 0.7f), width, speed);
    }
    public static bool AxisZDrag(string id, ref float value, float width, float speed = 0.001f) {
        return AxisDrag(id, ref value, new(0.2f, 0.4f, 1f, 0.7f), width, speed);
    }
    public static bool AxisDrag(string id, ref float value, Vector4 borderColor, float width, float speed = 0.001f) {
        ImGui.PushID(id);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, borderColor);
        //ImGui.PushStyleColor(ImGuiCol.Text, borderColor);

        ImGui.SetNextItemWidth(width);
        bool changed = ImGui.DragFloat(id, ref value, speed);

        ImGui.PopStyleColor(1);
        ImGui.PopStyleVar();
        ImGui.PopID();

        return changed;
    }
}
