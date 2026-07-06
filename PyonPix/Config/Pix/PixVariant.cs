using System;
using System.Text.Json.Serialization;
using PyonPix.Shared.Structs.Pix.Properties;

namespace PyonPix.Config.Pix;

[Serializable]
public class PixVariant {
    public bool Active = false;
    public bool IsSynced = false;
    public DateTime LastSeenUtc = DateTime.UtcNow;

    public bool PersistentCache = false;
    public bool SyncCookies = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrowserPixVariantOverrides? Browser = null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RendererPixVariantOverrides? Renderer = null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LightPixVariantOverrides? Light = null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioPixVariantOverrides? Audio = null;

    public BrowserPixVariantOverrides EnsureBrowser() => Browser ??= new();
    public RendererPixVariantOverrides EnsureRenderer() => Renderer ??= new();
    public LightPixVariantOverrides EnsureLight() => Light ??= new();
    public AudioPixVariantOverrides EnsureAudio() => Audio ??= new();

    public void PruneEmpty() {
        if(Browser?.HasAny != true) Browser = null;
        if(Renderer?.HasAny != true) Renderer = null;
        if(Light?.HasAny != true) Light = null;
        if(Audio?.HasAny != true) Audio = null;
    }
}
