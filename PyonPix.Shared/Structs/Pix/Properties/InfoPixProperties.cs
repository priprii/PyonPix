namespace PyonPix.Shared.Structs.Pix.Properties;

public class InfoPixProperties : ILocal<SyncedInfoPixProperties> {
    public string Name = string.Empty;
    public string Description = string.Empty;
    public PixType Type = PixType.Video;

    public SyncedInfoPixProperties ToSynced() {
        return new SyncedInfoPixProperties {
            Name = Name,
            Description = Description,
            Type = Type
        };
    }
}

public class SyncedInfoPixProperties : ISynced<InfoPixProperties> {
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PixType Type { get; set; }

    public void ApplyTo(InfoPixProperties target) {
        target.Name = Name;
        target.Description = Description;
        target.Type = Type;
    }
}
