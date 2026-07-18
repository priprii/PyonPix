using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Config;
using PyonPix.Extensions;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Services.Game;
using PyonPix.Shared.Extensions;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync.Dto;
using PyonPix.Ui.Components;
using PyonPix.Utility;

namespace PyonPix.Ui.Windows;

public class PixMembersWindow : BaseWindow {
    private SyncService SyncService => Services.Get<SyncService>();
    private StateService StateService => Services.Get<StateService>();

    protected override WindowState State => Config.UI.PixMembers.Collapsed ? WindowState.Collapsed : WindowState.Expanded;
    protected override Vector2 ExpandedSize => Config.UI.PixMembers.ExpandedSize;
    protected override Vector2 ExpandedMinSize => new Vector2(420, 190);
    protected override Vector2 ExpandedMaxSize => UiUtil.GameResolution;

    protected override void OnCollapsed(Vector2 windowSize) {
        Config.UI.PixMembers.ExpandedSize = windowSize;
        Config.Save();
    }
    protected override void SetState(WindowState newState) {
        if(State == newState) return;
        Config.UI.PixMembers.Collapsed = newState == WindowState.Collapsed;
        Config.Save();
    }
    protected override void OnCloseClicked() {
        SelectedPix = null;
        IsOpen = false;
    }

    public IPix? SelectedPix;
    private bool IsOwner = false;
    private List<SyncedPixMemberDto> Members = new();
    
    private long _selectedMemberCharacterId = -1;
    private ContextMenu? _memberContextMenu;

    public PixMembersWindow(Configuration config, IServiceContext services, IWindowContext windows) : base($"Pix Members###{Plugin.Name}PixMembers", config, services, windows) {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(420, 420) * ImGuiHelpers.GlobalScale;

        SyncService.PixMemberChangeRankSuccess += (dto) => {
            if(dto.PixId == SelectedPix?.Id) {
                // SyncedPixMembersResponse
            }
        };
        SyncService.PixMemberChangeRankFailed += (dto) => {
            if(dto.PixId == SelectedPix?.Id) {
            }
        };
        SyncService.PixMemberRemoveSuccess += (dto) => {
            if(dto.PixId == SelectedPix?.Id) {
                // SyncedPixMembersResponse
            }
        };
        SyncService.PixMemberRemoveFailed += (dto) => {
            if(dto.PixId == SelectedPix?.Id) {
            }
        };
        SyncService.PremiumStatusChanged += (dto) => {
        };
        SyncService.SyncedPixMembersUpdated += (dto) => {
            if(dto.PixId != SelectedPix?.Id) return;
            Members = dto.Members;
        };
        SyncService.StateChanged += (connectionState, statusMessage, statusType) => {
            if(connectionState == ConnectionState.Disconnected) Toggle(null, false);
        };
        SyncService.SyncedPixUnsubscribed += (pixId) => {
            if(SelectedPix != null && SelectedPix.Id == pixId) Toggle(null, false);
        };
        SyncService.SyncedPixDeleted += (pixId, local) => {
            if(SelectedPix != null && SelectedPix.Id == pixId) Toggle(null, false);
        };
    }

    public void Toggle(IPix? pix, bool isOwner) {
        Members.Clear();
        if(pix == null || pix == SelectedPix) {
            SelectedPix = null;
            IsOwner = false;
            IsOpen = false;
            return;
        }
        WindowName = $"{pix.Id} Members###{Plugin.Name}PixMembers";
        SelectedPix = pix;
        IsOwner = isOwner;
        IsOpen = true;

        _ = SyncService.RequestPixMembersAsync(pix.Id);
    }

    public override void Draw() => base.Draw();

    protected override void DrawContent() {
        if(SelectedPix == null) IsOpen = false;
        if(!IsOpen) return;

        ImGui.BeginChild("##pixMembers", ImGui.GetContentRegionAvail());

        foreach(var member in Members) {
            ImGui.PushID($"{member.CharacterId}");
            // Indicator
            var color = member.State switch {
                SyncedPixMemberState.Active => UiUtil.RGBA(0, 255, 0, 255),
                SyncedPixMemberState.Connected => UiUtil.RGBA(255, 165, 0, 255),
                _ => UiUtil.RGBA(255, 0, 0, 255)
            };
            var stateHint = member.State switch {
                SyncedPixMemberState.Active => "Active",
                SyncedPixMemberState.Connected => "Connected",
                _ => "Disconnected"
            };
            ImGuiEx.IconLabel(FontAwesomeIcon.Circle, $"##state_{member.CharacterId}", stateHint, color: color, size: UIShared.NormalIconSize, iconScale: 0.5f);
            ImGui.SameLine();

            // Rank
            var rankIcon = member.Rank switch {
                PixRank.Owner => FontAwesomeIcon.Crown,
                PixRank.CoOwner => FontAwesomeIcon.Crown,
                _ => FontAwesomeIcon.User
            };
            var rankColor = member.Rank switch {
                PixRank.Owner => UIShared.PixRankOwner,
                PixRank.CoOwner => UIShared.PixRankCoOwner,
                _ => UIShared.PixRankMember
            };
            var rankHint = member.Rank switch {
                PixRank.Owner => "Owner",
                PixRank.CoOwner => "Co-Owner",
                _ => "Member"
            };
            ImGuiEx.IconLabel(rankIcon, $"##rank_{member.CharacterId}", rankHint, color: rankColor, size: UIShared.NormalIconSize, iconScale: 0.8f);
            ImGui.SameLine();

            // Alias
            ImGuiEx.StyledText(member.Alias, UIShared.NormalFontSize, animationType: member.AliasStyle?.AnimationType ?? default, colorA: member.AliasStyle?.ColourA?.ToVector3(), colorB: member.AliasStyle?.ColourB?.ToVector3(), glowA: member.AliasStyle?.GlowA?.ToVector3(), glowB: member.AliasStyle?.GlowB?.ToVector3());

            // Context
            if(member.CharacterId != StateService.LocalPlayerContentId) {
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30f);
                if(ImGuiEx.IconButton(FontAwesomeIcon.EllipsisV, $"##member{member.CharacterId}")) {
                    _selectedMemberCharacterId = member.CharacterId;
                    _memberContextMenu = BuildMemberContextMenu(member);
                    _memberContextMenu.Open();
                }
                _memberContextMenu?.Draw();
            }
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private ContextMenu BuildMemberContextMenu(SyncedPixMemberDto member) {
        var items = new List<ContextMenuItem>();

        if(IsOwner) {
            /*
            items.Add(new ContextMenuButton("Promote to Owner", icon: FontAwesomeIcon.Crown,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModCtrl)) ChangeRank(member, PixRank.Owner);
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                            return ("Promote to Owner", "Hold the Control key to confirm.");
                        return ("Promote to Owner", null);
                    }));
            */

            if(member.Rank != PixRank.CoOwner) {
                items.Add(new ContextMenuButton("Promote to Co-Owner", icon: FontAwesomeIcon.Crown,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModCtrl)) ChangeRank(member, PixRank.CoOwner);
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                            return ("Promote to Co-Owner", "Hold the Control key to confirm.");
                        return ("Promote to Co-Owner", null);
                    }));
            } else {
                items.Add(new ContextMenuButton("Demote to Member", icon: FontAwesomeIcon.User,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModCtrl)) ChangeRank(member, PixRank.Member);
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                            return ("Demote to Member", "Hold the Control key to confirm.");
                        return ("Demote to Member", null);
                    }));
            }

            items.Add(new ContextMenuButton("Remove", icon: FontAwesomeIcon.Trash,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModCtrl)) RemoveMember(member);
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                            return ("Remove User", "Hold the Control key to confirm.");
                        return ("Remove User", null);
                    }));
        }

        items.Add(new ContextMenuButton("Report", icon: FontAwesomeIcon.ExclamationTriangle,
                    onClick: () => {
                        if(ImGui.IsKeyDown(ImGuiKey.ModShift)) SyncService.ReportUser(member.CharacterId);
                    },
                    isDisabled: () => !ImGui.IsKeyDown(ImGuiKey.ModShift),
                    tooltip: () => {
                        if(!ImGui.IsKeyDown(ImGuiKey.ModShift))
                            return ("Report User", "Report this User for service violation.\nFalse reports may have consequences.\n\nHold the Shift key to confirm.");
                        return ("Report User", null);
                    }));

        return new ContextMenu($"memberCtx{member.CharacterId}", items, width: 160f, itemHeight: 26f);
    }

    private void ChangeRank(SyncedPixMemberDto member, PixRank newRank) {
        SyncService.ChangePixMemberRank(SelectedPix.Id, member.CharacterId, newRank);
    }

    private void RemoveMember(SyncedPixMemberDto member) {
        SyncService.RemovePixMember(SelectedPix.Id, member.CharacterId);
    }
}
