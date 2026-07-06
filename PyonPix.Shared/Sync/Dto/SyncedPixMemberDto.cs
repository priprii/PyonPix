using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync.Dto.Client;

namespace PyonPix.Shared.Sync.Dto;

public enum SyncedPixMemberState {
    Disconnected,
    Connected,
    Active
}

public sealed class SyncedPixMemberDto {
    public long CharacterId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public StyleDto? AliasStyle { get; set; }
    public PremiumStatus Premium { get; set; } = new(false, false);
    public PixRank Rank { get; set; }

    public DateTime JoinedTimestamp { get; set; }
    public DateTime LastJoinedTimestamp { get; set; }

    public SyncedPixMemberState State { get; set; }
}

public sealed class SyncedPixMemberRow {
    public long CharacterId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string AliasStyle { get; set; } = string.Empty;
    public bool IsSupporter { get; set; }
    public bool IsSubscriber { get; set; }
    public string Rank { get; set; } = "Member";
    public DateTime JoinedTimestamp { get; set; }
    public DateTime LastJoinedTimestamp { get; set; }
}
