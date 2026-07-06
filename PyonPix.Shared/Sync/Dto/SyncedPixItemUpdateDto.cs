using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Structs.Pix.Properties;

namespace PyonPix.Shared.Sync.Dto;

public abstract class BaseSyncedPixUpdate(string pixId) {
    public string PixId { get; set; } = pixId;
    public abstract PixUpdateType UpdateType { get; }
}

public class SyncedPixUpdate(string pixId, SyncedInfoPixProperties? info, SyncedBrowserPixProperties? browser, SyncedRendererPixProperties? renderer, SyncedLightPixProperties? light, SyncedAudioPixProperties? audio, SyncedSyncPixProperties? sync) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.All;

    public SyncedInfoPixProperties? Info { get; set; } = info;
    public SyncedBrowserPixProperties? Browser { get; set; } = browser;
    public SyncedRendererPixProperties? Renderer { get; set; } = renderer;
    public SyncedLightPixProperties? Light { get; set; } = light;
    public SyncedAudioPixProperties? Audio { get; set; } = audio;
    public SyncedSyncPixProperties? Sync { get; set; } = sync;
}
public class SyncedPixUpdateUri(string pixId, string uri) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.Uri;

    public string Uri { get; set; } = uri;
}
public class SyncedPixUpdateInfoProperties(string pixId, SyncedInfoPixProperties info) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.InfoProperties;

    public SyncedInfoPixProperties Info { get; set; } = info;
}
public class SyncedPixUpdateBrowserProperties(string pixId, SyncedBrowserPixProperties browser) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.BrowserProperties;

    public SyncedBrowserPixProperties Browser { get; set; } = browser;
}
public class SyncedPixUpdateRendererProperties(string pixId, SyncedRendererPixProperties renderer) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.RendererProperties;

    public SyncedRendererPixProperties Renderer { get; set; } = renderer;
}
public class SyncedPixUpdateLightProperties(string pixId, SyncedLightPixProperties light) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.LightProperties;

    public SyncedLightPixProperties Light { get; set; } = light;
}
public class SyncedPixUpdateAudioProperties(string pixId, SyncedAudioPixProperties audio) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.AudioProperties;

    public SyncedAudioPixProperties Audio { get; set; } = audio;
}
public class SyncedPixUpdateSyncProperties(string pixId, SyncedSyncPixProperties sync) : BaseSyncedPixUpdate(pixId) {
    public override PixUpdateType UpdateType => PixUpdateType.SyncProperties;

    public SyncedSyncPixProperties Sync { get; set; } = sync;
}
