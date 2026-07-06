using System.Numerics;
using System.Text.Json.Serialization;
using PyonPix.Shared.Structs.Browser;

namespace PyonPix.Shared.Structs.Pix.Properties;

public class BrowserPixProperties : ILocal<SyncedBrowserPixProperties> {
    public string Uri = string.Empty;
    public BrowserScaleMode ScaleMode = BrowserScaleMode.BrowserWindow;
    public uint CustomScaleWidth = 1920;
    public uint CustomScaleHeight = 1080;
    public bool GpuAcceleration = true;

    public Vector2 CustomScale => new Vector2(CustomScaleWidth, CustomScaleHeight);

    public SyncedBrowserPixProperties ToSynced() {
        return new SyncedBrowserPixProperties {
            Uri = Uri,
            ScaleMode = ScaleMode,
            CustomScaleWidth = CustomScaleWidth,
            CustomScaleHeight = CustomScaleHeight,
            GpuAcceleration = GpuAcceleration
        };
    }
}

public class SyncedBrowserPixProperties : ISynced<BrowserPixProperties> {
    public string Uri { get; set; } = string.Empty;
    public BrowserScaleMode ScaleMode { get; set; }
    public uint CustomScaleWidth { get; set; }
    public uint CustomScaleHeight { get; set; }
    public bool GpuAcceleration { get; set; }

    public void ApplyTo(BrowserPixProperties target) {
        target.Uri = Uri;
        target.ScaleMode = ScaleMode;
        target.CustomScaleWidth = CustomScaleWidth;
        target.CustomScaleHeight = CustomScaleHeight;
        target.GpuAcceleration = GpuAcceleration;
    }
}

[Serializable]
public class BrowserPixVariantOverrides {
    public BrowserScaleMode? ScaleMode = null;
    public uint? CustomScaleWidth = null;
    public uint? CustomScaleHeight = null;
    public bool? GpuAcceleration = null;

    [JsonIgnore]
    public bool HasAny =>
        ScaleMode.HasValue ||
        CustomScaleWidth.HasValue ||
        CustomScaleHeight.HasValue ||
        GpuAcceleration.HasValue;

    public void ApplyTo(BrowserPixProperties target) {
        if(ScaleMode.HasValue) target.ScaleMode = ScaleMode.Value;
        if(CustomScaleWidth.HasValue) target.CustomScaleWidth = CustomScaleWidth.Value;
        if(CustomScaleHeight.HasValue) target.CustomScaleHeight = CustomScaleHeight.Value;
        if(GpuAcceleration.HasValue) target.GpuAcceleration = GpuAcceleration.Value;
    }
}
