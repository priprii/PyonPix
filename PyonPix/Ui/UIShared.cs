using System;
using System.Numerics;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Services;
using PyonPix.Utility;

namespace PyonPix.Ui;

public static class UIShared {
    internal static Configuration Config = null!;
    internal static IDalamudTextureWrap? GradientTexture;

    internal static IFontHandle HeaderFont = null!;
    internal static float HeaderFontSize = 28f;
    internal static IFontHandle NormalFont = null!;
    internal static float NormalFontSize = 16f;
    internal static IFontHandle SubFont = null!;
    internal static float SubFontSize = 14f;

    internal static IFontHandle NormalIconFont = null!;
    internal static float NormalIconSize => 16f * ImGuiHelpers.GlobalScale;
    internal static IFontHandle SubIconFont = null!;
    internal static float SubIconSize => 14f * ImGuiHelpers.GlobalScale;

    internal static float LineHeight => 26f * ImGuiHelpers.GlobalScale;

    internal static Vector4 AccentHovered = UiUtil.RGBA(150, 110, 190, 240);
    internal static Vector4 AccentActive = UiUtil.RGBA(180, 140, 220, 240);

    internal static float SeparatorSpacing => 6f * ImGuiHelpers.GlobalScale;
    internal static Vector4 Separator = UiUtil.RGBA(50, 42, 50, 220);

    internal static Vector4 Error = UiUtil.RGBA(240, 40, 40, 240);
    internal static Vector4 Warn = UiUtil.RGBA(240, 180, 40, 240);
    public static Vector4 Normal = UiUtil.RGBA(230, 230, 230, 240);
    public static Vector4 Dimmed = UiUtil.RGBA(200, 200, 200, 240);
    public static Vector4 Muted = UiUtil.RGBA(170, 170, 170, 240);

    internal static float WindowRounding = 6f * ImGuiHelpers.GlobalScale;
    internal static Vector4 WindowBgTint = UiUtil.RGBA(230, 150, 230, 250);
    internal static Vector4 WindowTitle = UiUtil.RGBA(45, 38, 45, 250);
    internal static Vector4 WindowBorder = UiUtil.RGBA(45, 38, 45, 250);
    internal static Vector4 BrowserTabFocused => WindowTitle;
    internal static Vector4 BrowserTabInactive = UiUtil.RGBA(180, 180, 180, 180);

    internal static Vector4 TitleBarBg = UiUtil.RGBA(0, 0, 0, 40);

    internal static Vector4 PixTerritoryBgNormal = Vector4.Zero;
    internal static Vector4 PixTerritoryBgHovered = UiUtil.RGBA(255, 255, 255, 15);
    internal static Vector4 PixTerritoryBgActive = UiUtil.RGBA(255, 255, 255, 40);
    internal static Vector4 PixTerritoryBgExpanded = UiUtil.RGBA(255, 255, 255, 10);
    internal static Vector4 PixTerritoryBgExpandedHovered = UiUtil.RGBA(100, 100, 100, 50);

    internal static Vector4 ItemBgHovered = UiUtil.RGBA(255, 255, 255, 10);
    internal static Vector4 ItemBgActive = UiUtil.RGBA(255, 255, 255, 15);
    internal static Vector4 ItemHeader = UiUtil.RGBA(240, 240, 240, 240);
    internal static Vector4 ItemSubText = UiUtil.RGBA(200, 200, 200, 240);
    internal static Vector4 ItemInactive = UiUtil.RGBA(150, 150, 150, 240);

    internal static Vector4 ContextMenuBg = UiUtil.RGBA(15, 15, 15, 250);
    internal static Vector4 ContextMenuBorder = UiUtil.RGBA(45, 38, 45, 230);
    internal static Vector4 ContextItemBgHovered = UiUtil.RGBA(255, 255, 255, 10);
    internal static Vector4 ContextItemBgActive = UiUtil.RGBA(255, 255, 255, 15);
    internal static Vector4 ContextItemTextNormal = UiUtil.RGBA(160, 160, 160, 240);
    internal static Vector4 ContextItemTextHovered = UiUtil.RGBA(235, 235, 235, 240);
    internal static Vector4 ContextItemTextActive => AccentActive;

    internal static Vector4 ToolBarSeparator => Separator;

    internal static float TabRounding = 2f * ImGuiHelpers.GlobalScale;
    internal static Vector4 TabBg = UiUtil.RGBA(20, 20, 20, 100);
    internal static Vector4 TabBgNormal = UiUtil.RGBA(20, 20, 20, 50);
    internal static Vector4 TabBgHovered = UiUtil.RGBA(100, 100, 100, 50);
    internal static Vector4 TabBgClicked = UiUtil.RGBA(255, 255, 255, 40);
    internal static Vector4 TabBgActive = UiUtil.RGBA(100, 100, 100, 25);
    internal static Vector4 TabTextNormal = UiUtil.RGBA(140, 140, 140, 240);
    internal static Vector4 TabTextHovered = UiUtil.RGBA(255, 255, 255, 240);
    internal static Vector4 TabTextClicked = UiUtil.RGBA(255, 255, 255, 240);
    internal static Vector4 TabTextActive = UiUtil.RGBA(240, 240, 240, 240);

    internal static float InputRounding = 4f * ImGuiHelpers.GlobalScale;
    internal static Vector2 InputPadding = new(10f, 5f);
    internal static Vector4 InputBgNormal = UiUtil.RGBA(60, 60, 60, 50);
    internal static Vector4 InputBgHovered = UiUtil.RGBA(100, 100, 100, 50);
    internal static Vector4 InputBgActive = UiUtil.RGBA(140, 140, 140, 50);
    internal static Vector4 InputBgDisabled = UiUtil.RGBA(0, 0, 0, 80);
    internal static Vector4 InputTextNormal = UiUtil.RGBA(200, 200, 200, 240);
    internal static Vector4 InputTextHovered = UiUtil.RGBA(225, 225, 225, 240);
    internal static Vector4 InputTextActive = UiUtil.RGBA(245, 245, 245, 240);
    internal static Vector4 InputTextDisabled = UiUtil.RGBA(60, 60, 60, 240);
    internal static Vector4 InputTextHint = UiUtil.RGBA(160, 160, 160, 240);
    internal static Vector4 InputBgTextSelected = UiUtil.RGBA(100, 100, 100, 200);

    internal static float ComboItemPadding => 4f * ImGuiHelpers.GlobalScale;

    internal static Vector4 DragFgNormal => AccentActive;
    internal static Vector4 DragFgHovered => AccentHovered;
    internal static Vector4 DragFgActive => AccentActive;
    internal static Vector4 DragFgDisabled => InputTextDisabled;

    internal static Vector4 IconNormal = UiUtil.RGBA(200, 200, 200, 240);
    internal static Vector4 IconHovered => AccentHovered;
    internal static Vector4 IconActive => AccentActive;
    internal static Vector4 IconDisabled = UiUtil.RGBA(60, 60, 60, 240);
    internal static Vector4 IconToggled => AccentActive;
    internal static Vector4 IconLabelNormal = UiUtil.RGBA(200, 200, 200, 240);
    internal static Vector4 IconLabelHovered = UiUtil.RGBA(225, 225, 225, 240);
    internal static Vector4 IconLabelActive = UiUtil.RGBA(245, 245, 245, 240);
    internal static Vector4 IconLabelDisabled = UiUtil.RGBA(60, 60, 60, 240);
    internal static Vector4 IconLabelToggled = UiUtil.RGBA(245, 245, 245, 240);

    internal static float IconTextRounding = 4f * ImGuiHelpers.GlobalScale;
    internal static float IconTextPadding = 6f;
    internal static Vector4 IconTextNormal = UiUtil.RGBA(200, 200, 200, 240);
    internal static Vector4 IconTextHovered => AccentHovered;
    internal static Vector4 IconTextActive => AccentActive;
    internal static Vector4 IconTextDisabled = UiUtil.RGBA(60, 60, 60, 240);
    internal static Vector4 IconTextBgNormal = UiUtil.RGBA(70, 70, 70, 50);
    internal static Vector4 IconTextBgHovered = UiUtil.RGBA(100, 100, 100, 50);
    internal static Vector4 IconTextBgClicked = UiUtil.RGBA(255, 255, 255, 40);
    internal static Vector4 IconTextBgActive = UiUtil.RGBA(100, 100, 100, 25);

    internal static Vector2 TextBgPadding = new Vector2(4f, 2f) * ImGuiHelpers.GlobalScale;

    internal static float TooltipRounding = 2f * ImGuiHelpers.GlobalScale;
    internal static float TooltipBorderThickness = 1f * ImGuiHelpers.GlobalScale;
    internal static Vector2 TooltipPadding = new(4f * ImGuiHelpers.GlobalScale);
    internal static Vector4 TooltipBg = UiUtil.RGBA(22, 16, 22, 245);
    internal static Vector4 TooltipBorder = UiUtil.RGBA(45, 38, 45, 245);
    internal static Vector4 TooltipText = UiUtil.RGBA(225, 225, 225, 220);
    internal static Vector4 TooltipSubText = UiUtil.RGBA(200, 200, 200, 220);
    internal static Vector4 TooltipSeparator => Separator;

    internal static Vector4 ScrollbarBg = Vector4.Zero;
    internal static Vector4 ScrollbarGrabNormal = UiUtil.RGBA(50, 50, 50, 200);
    internal static Vector4 ScrollbarGrabHovered = UiUtil.RGBA(70, 70, 70, 200);
    internal static Vector4 ScrollbarGrabActive = UiUtil.RGBA(60, 60, 60, 200);

    internal static Vector4 PixTypeLocal = UiUtil.RGBA(200, 200, 200, 255);
    internal static Vector4 PixTypeSynced => AccentActive;
    internal static Vector4 PixRankOwner => AccentActive;
    internal static Vector4 PixRankCoOwner = UiUtil.RGBA(230, 230, 230, 255);
    internal static Vector4 PixRankMember = UiUtil.RGBA(200, 200, 200, 255);

    public static void Initialize(Configuration config, IServiceContext services) {
        Config = config;
        Update();

        var ui = services.PluginInterface.UiBuilder;
        HeaderFont = ui.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.MiedingerMid, HeaderFontSize));
        NormalFont = ui.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, NormalFontSize));
        SubFont = ui.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, SubFontSize));

        NormalIconFont = ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePx = NormalFontSize })));
        SubIconFont = ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePx = SubFontSize })));

        GradientTexture = CreateGradientTexture(services);
    }

    public static void Update() {
        var uiProps = Config.Global.General;
        WindowBgTint = uiProps.AccentBg;
        WindowTitle = uiProps.AccentTitle;
        AccentHovered = uiProps.AccentHovered;
        AccentActive = uiProps.AccentActive;
    }

    private static IDalamudTextureWrap CreateGradientTexture(IServiceContext services) {
        const int width = 4;
        const int height = 512;
        var pixels = new byte[width * height * 4];

        int[,] bayer4 = {
            { 0,  8,  2, 10 },
            { 12, 4, 14,  6 },
            { 3, 11,  1,  9 },
            { 15, 7, 13,  5 }
        };

        Vector4 top = UiUtil.RGBA(25, 25, 25, 255);
        Vector4 bottom = new Vector4(top.X * 0.35f, top.Y * 0.35f, top.Z * 0.35f, 1f);

        for(int y = 0; y < height; y++) {
            float t = y / (float)(height - 1);
            Vector4 baseCol = Vector4.Lerp(top, bottom, t);

            for(int x = 0; x < width; x++) {
                float dither = (bayer4[y % 4, x % 4] / 16f - 0.5f) * 0.01f;

                Vector4 col = baseCol;
                col.X = Math.Clamp(col.X + dither, 0f, 1f);
                col.Y = Math.Clamp(col.Y + dither, 0f, 1f);
                col.Z = Math.Clamp(col.Z + dither, 0f, 1f);

                int i = (y * width + x) * 4;
                pixels[i + 0] = (byte)(col.X * 255);
                pixels[i + 1] = (byte)(col.Y * 255);
                pixels[i + 2] = (byte)(col.Z * 255);
                pixels[i + 3] = 255;
            }
        }

        return services.TextureProvider.CreateFromRaw(RawImageSpecification.Rgba32(width, height), pixels);
    }

    public static void Dispose() {
        GradientTexture?.Dispose();
    }
}
