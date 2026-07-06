using System.Text.Json.Serialization;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync.Dto.Client;

namespace PyonPix.Config.Pix;

public class SyncedPix : BasePix {
    public long OwnerId { get; set; }
    public string OwnerAlias { get; set; } = string.Empty;
    public StyleDto? OwnerAliasStyle { get; set; }
    public StyleDto? OwnerPixStyle { get; set; }
    public PixRank SelfRank { get; set; }

    [JsonIgnore]
    public PixDto SourcePix { get; set; } = new();

    [JsonIgnore]
    public bool CanSyncEdit => (int)SelfRank <= (int)Sync.EditorRank;
}
