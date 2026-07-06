using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Structs.Pix.Properties;
using PyonPix.Shared.Sync.Dto;

namespace PyonPix.Config.Pix;

public class BasePix : IPix {
    public int Version { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    public InfoPixProperties Info { get; set; } = new();
    public BrowserPixProperties Browser { get; set; } = new();
    public TerritoryPixProperties Territory { get; set; } = new();
    public RendererPixProperties Renderer { get; set; } = new();
    public LightPixProperties Light { get; set; } = new();
    public AudioPixProperties Audio { get; set; } = new();

    public SyncPixProperties Sync { get; set; } = new();

    public string GetDisplayName() => string.IsNullOrWhiteSpace(Info.Name) ? Id : Info.Name;

    public SyncedPixMetaDto GetSyncedMetaData() {
        return new SyncedPixMetaDto {
            Name = Info.Name,
            Description = Info.Description,
            PixType = Info.Type,
            Privacy = Sync.Privacy,
            EditorRank = Sync.EditorRank,
            SecretKey = Sync.SecretKey,
            Nsfw = Sync.Nsfw,
            Territory = Territory.ToSynced()
        };
    }
}
