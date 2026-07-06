namespace PyonPix.Shared.Structs.Pix.Properties;

public class SyncPixProperties : ILocal<SyncedSyncPixProperties> {
    public bool IsSynced = false;
    public string? SyncedPixId;

    public string? SecretKey;
    public PixPrivacy Privacy = PixPrivacy.Private;
    public PixRank EditorRank = PixRank.Owner;
    public bool Nsfw = false;

    public SyncedSyncPixProperties ToSynced() {
        return new SyncedSyncPixProperties {
            SecretKey = Privacy == PixPrivacy.Private ? SecretKey : null,
            Privacy = Privacy,
            EditorRank = EditorRank,
            Nsfw = Nsfw
        };
    }
}

public class SyncedSyncPixProperties : ISynced<SyncPixProperties> {
    public string? SecretKey { get; set; }
    public PixPrivacy Privacy { get; set; }
    public PixRank EditorRank { get; set; }
    public bool Nsfw { get; set; }

    public void ApplyTo(SyncPixProperties target) {
        target.SecretKey = SecretKey;
        target.Privacy = Privacy;
        target.EditorRank = EditorRank;
        target.Nsfw = Nsfw;
    }
}
