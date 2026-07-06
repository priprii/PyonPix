using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Ui;

namespace PyonPix.Utility;

public static class UiUtil {
    public static unsafe uint GameWidth => FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager.Instance()->Resolution_Width;
    public static unsafe uint GameHeight => FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager.Instance()->Resolution_Height;
    public static Vector2 GameResolution => new Vector2(GameWidth, GameHeight);

    public static Vector4 RGBA(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public static Vector2 CalcTextSize(string text, float fontSize, bool globalScale = true) {
        return ImGui.CalcTextSize(text, false) * (fontSize / ImGui.GetFontSize()) * (globalScale ? ImGuiHelpers.GlobalScale : 1);
    }
    public static Vector2 CalcTextSize(IFontHandle font, string text, float? scale = null) {
        Vector2 size;
        if(scale != null) ImGui.SetWindowFontScale(scale.Value);
        using(font.Push()) {
            size = ImGui.CalcTextSize(text, false);
        }
        if(scale != null) ImGui.SetWindowFontScale(1f);
        return size;
    }

    public static Vector2 CalcIconTextSize(FontAwesomeIcon icon, string text, float? iconScale = null) {
        var padding = UIShared.IconTextPadding;
        var iconPadding = UIShared.IconTextPadding;
        var iconText = icon.ToIconString();
        var iconSize = CalcTextSize(UIShared.NormalIconFont, iconText, iconScale);
        var labelSize = ImGui.CalcTextSize(text);
        return new Vector2((padding * 2f) + iconSize.X + iconPadding + labelSize.X, MathF.Max(iconSize.Y, labelSize.Y) + (padding * 2f));
    }

    public static Vector2 AlignCenter(Vector2 min, Vector2 max, Vector2 size) {
        return new Vector2(min.X + ((max.X - min.X - size.X) * 0.5f), min.Y + ((max.Y - min.Y - size.Y) * 0.5f));
    }
    public static Vector2 AlignCenter(Vector2 min, Vector2 max, float size) => AlignCenter(min, max, new Vector2(size));

    public static bool IsRectHovered(Vector2 rMin, Vector2 rMax) => ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(rMin, rMax);
    public static bool IsRectClicked(Vector2 rMin, Vector2 rMax, ImGuiMouseButton button = ImGuiMouseButton.Left) => IsRectHovered(rMin, rMax) && ImGui.IsMouseReleased(button);

    public static FontAwesomeIcon GetIconForPixType(PixType type) {
        return type switch {
            PixType.Video => FontAwesomeIcon.Tv,
            PixType.Audio => FontAwesomeIcon.Music,
            PixType.Image => FontAwesomeIcon.Image,
            PixType.Game => FontAwesomeIcon.Gamepad,
            PixType.Light => FontAwesomeIcon.Lightbulb,
            _ => FontAwesomeIcon.Cube,
        };
    }

    public static void OpenDiscord() {
        var process = new ProcessStartInfo("https://discord.gg/3wBtUrVDJh") { UseShellExecute = true };
        Process.Start(process);
    }
    public static void OpenKofi() {
        var process = new ProcessStartInfo("https://ko-fi.com/primu") { UseShellExecute = true };
        Process.Start(process);
    }
}
