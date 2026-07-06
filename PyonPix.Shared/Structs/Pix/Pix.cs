using PyonPix.Shared.Structs.Pix.Properties;

namespace PyonPix.Shared.Structs.Pix;

public class Pix {
    public int Version { get; set; }
    public InfoPixProperties Info { get; set; } = new();
    public BrowserPixProperties Browser { get; set; } = new();
    public TerritoryPixProperties Territory { get; set; } = new();
    public RendererPixProperties Renderer { get; set; } = new();
    public LightPixProperties Light { get; set; } = new();
    public AudioPixProperties Audio { get; set; } = new();
}

public class PixDto {
    public int Version { get; set; }
    public SyncedBrowserPixProperties Browser { get; set; } = new();
    public SyncedRendererPixProperties Renderer { get; set; } = new();
    public SyncedLightPixProperties Light { get; set; } = new();
    public SyncedAudioPixProperties Audio { get; set; } = new();
}
