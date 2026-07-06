using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Events;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Services.Game;
using PyonPix.Shared.Sync.Dto.Client;
using PyonPix.Shared.Utility;
using PyonPix.Structs.Ui;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class UserWindow : BaseWindow {
    private SyncService SyncService => Services.Get<SyncService>();
    private StateService StateService => Services.Get<StateService>();

    protected override bool ShowTitleBarSettingsButton => false;
    protected override WindowState State => Config.UI.User.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.User.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(380, 190);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.User.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.User.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnCloseClicked() => IsOpen = false;

    private string? LastAliasError = string.Empty;

    public UserWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"{Plugin.Name} User Config###{Plugin.Name}UserConfig", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(380, 380) * ImGuiHelpers.GlobalScale;

        SyncService.PremiumStatusChanged += (e) => {

        };
        SyncService.StateChanged += (connectionState, statusMessage, statusType) => {

        };

        SyncService.StyleUpdateResponse += (isSuccess) => {
            if(isSuccess) {
                StatusBar.Show($"Changes Applied", 2000, true, StatusType.Info);
            } else {
                LastAliasError = "Someone else stole that Alias.. qwq";
            }
        };
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(!IsOpen) return;
        var cId = StateService.LocalPlayerContentId;
        if(cId == 0) return;

        ImGui.BeginChild("##container", ImGui.GetContentRegionAvail());
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(cursorPos + WindowPadding);
        ImGui.BeginChild("##content", ImGui.GetContentRegionAvail());
        DrawProperties();
        ImGui.EndChild();
        ImGui.EndChild();

        if(!SyncService.IsConnectedAuth) {
            var syncStatus = SyncService.State != ConnectionState.Connected ? "Disconnected" : !SyncService.Client.IsAuthenticated ? "Authentication Required" : "Unavailable";
            StatusBar.Show($"Sync Service: {syncStatus}", 100, statusType: StatusType.Error);
        }
    }

    private void DrawProperties() {
        if(StateService.LocalPlayerContentId == 0) return;
        var disabled = !SyncService.IsConnectedAuth;
        var isSupporter = SyncService.Client.Premium.IsSupporter;
        var isSubscriber = SyncService.Client.Premium.IsSubscriber;
        var scale = ImGuiHelpers.GlobalScale;
        var region = ImGui.GetContentRegionAvail().X - WindowPadding.X;
        var itemWidth = region - IndentWidth;

        var cProps = Config.Sync.GetCurrentCharacterProperties(Config, StateService);

        ImGuiEx.StyledText("Client AuthKey");
        ImGui.Indent(IndentWidth);
        if(ImGuiEx.StyledInput("##secret", ref Config.Sync.SecretKey, "AuthKey..", SyncService.IsConnectedAuth, 32, itemWidth) == UIState.Ended) {
            Config.Save();
        }
        if(string.IsNullOrEmpty(Config.Sync.SecretKey) || SyncService.Client.IsSecretKeyInvalid) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText("AuthKey can be retrieved using the 'Data' option in #pyonpix on Discord", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
        }
        ImGui.Unindent(IndentWidth);

        ImGuiEx.Separator(region);

        ImGuiEx.StyledText("Alias Style: ");
        ImGui.SameLine();
        ImGuiEx.StyledText($"{(string.IsNullOrEmpty(cProps.Alias) ? "Sample Alias" : cProps.Alias.Trim())}##sampleAlias", animationType: cProps.AliasAnimationType, colorA: cProps.AliasColourA, colorB: cProps.AliasColourB, glowA: cProps.AliasGlowA, glowB: cProps.AliasGlowB);

        ImGui.Indent(IndentWidth);
        var changed = ImGuiEx.StyledInput("##alias", ref cProps.Alias, "Character Alias..", disabled, NameUtil.AliasMaxLength, itemWidth, tooltip: "Character Alias", 
            tooltipSub: "An alias to identify your current character as, visible to other connected users.\n\n" +
            "- Supporters can use an alias with reduced limits on use of special characters.\n" +
            "- Inappropriate alias may result in termination from the Sync Service.");
        if(changed != UIState.None){
            LastAliasError = null;
            if(!NameUtil.ValidateAlias(cProps.Alias, SyncService.Client.Premium, out var error)) {
                LastAliasError = error;
            }
        }
        if(!isSupporter) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText("Changing Alias requires 'Supporter' role on Pyon Discord", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
        } else if(!string.IsNullOrEmpty(LastAliasError)) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText(LastAliasError, animationType: AnimationType.Pulse, colorA: new Vector3(0.6f, 0, 0), colorB: new Vector3(1f, 0, 0), glowStrength: 0.1f, bgOpacity: 0.4f);
            }
        }

        ImGuiEx.EnumCombo("##aliasAnimType", "Animation Type: ", ref cProps.AliasAnimationType, ComboButtonDisplayType.Items, disabled, width: itemWidth);
        ImGuiEx.ColorPicker3("ColourA##acolA", ref cProps.AliasColourA);
        ImGui.SameLine();
        ImGuiEx.ColorPicker3("ColourB##acolB", ref cProps.AliasColourB);
        ImGui.SameLine();
        ImGuiEx.ColorPicker3("GlowA##aglowA", ref cProps.AliasGlowA);
        ImGui.SameLine();
        ImGuiEx.ColorPicker3("GlowB##aglowB", ref cProps.AliasGlowB);
        if(!isSubscriber) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText("Changing Alias Style requires 'Subscriber' role on Pyon Discord", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
        }
        ImGui.Unindent(IndentWidth);

        ImGuiEx.Separator(region);

        ImGuiEx.StyledText("Pix Style: ");
        ImGui.SameLine();
        ImGuiEx.StyledText("Preview Pix##samplePix", animationType: cProps.PixAnimationType, colorA: cProps.PixColourA, colorB: cProps.PixColourB, glowA: cProps.PixGlowA, glowB: cProps.PixGlowB);

        ImGui.Indent(IndentWidth);
        ImGuiEx.EnumCombo("##pixAnimType", "Animation Type: ", ref cProps.PixAnimationType, ComboButtonDisplayType.Items, disabled, width: itemWidth);
        ImGuiEx.ColorPicker3("ColourA##pcolA", ref cProps.PixColourA);
        ImGui.SameLine();
        ImGuiEx.ColorPicker3("ColourB##pcolB", ref cProps.PixColourB);
        ImGui.SameLine();
        ImGuiEx.ColorPicker3("GlowA##pglowA", ref cProps.PixGlowA);
        ImGui.SameLine();
        ImGuiEx.ColorPicker3("GlowB##pglowB", ref cProps.PixGlowB);
        if(!isSubscriber) {
            using(UIShared.SubFont.Push()) {
                ImGuiEx.StyledText("Changing Pix Style requires 'Subscriber' role on Pyon Discord", colorA: UIShared.AccentActive.AsVector3(), glowStrength: 0.1f, bgOpacity: 0.3f);
            }
        }
        ImGui.Unindent(IndentWidth);

        ImGuiEx.Separator(region);

        if(ImGuiEx.IconTextButton(FontAwesomeIcon.Upload, "Apply", "##apply", disabled || !isSupporter || !string.IsNullOrEmpty(LastAliasError), tooltip: "Apply Alias/Pix Style")) {
            if(cProps.Equals(SyncService.Client.Style, isSubscriber)) return;

            if(NameUtil.ValidateAlias(cProps.Alias, SyncService.Client.Premium, out var error)) {
                Config.Save();
                SyncService.SendStyleUpdate();
                StatusBar.Show($"Updating..", 1000, true, StatusType.Info);
            } else {
                LastAliasError = error;
            }
        }
    }
}
