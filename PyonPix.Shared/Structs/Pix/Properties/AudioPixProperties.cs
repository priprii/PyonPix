using System.Text.Json.Serialization;

namespace PyonPix.Shared.Structs.Pix.Properties;

public class AudioPixProperties : ILocal<SyncedAudioPixProperties> {
    public bool SpatialEnabled = true;
    public float Volume = 1f;
    public float FalloffMaxDistance = 25f;
    public float FalloffStrength = 4f;

    public SyncedAudioPixProperties ToSynced() {
        return new SyncedAudioPixProperties {
            SpatialEnabled = SpatialEnabled,
            Volume = Volume,
            FalloffMaxDistance = FalloffMaxDistance,
            FalloffStrength = FalloffStrength
        };
    }
}

public class SyncedAudioPixProperties : ISynced<AudioPixProperties> {
    public bool SpatialEnabled { get; set; }
    public float Volume { get; set; }
    public float FalloffMaxDistance { get; set; }
    public float FalloffStrength { get; set; }

    public void ApplyTo(AudioPixProperties target) {
        target.SpatialEnabled = SpatialEnabled;
        target.Volume = Volume;
        target.FalloffMaxDistance = FalloffMaxDistance;
        target.FalloffStrength = FalloffStrength;
    }
}

[Serializable]
public class AudioPixVariantOverrides {
    public bool? SpatialEnabled = null;
    public float? Volume = null;
    public float? FalloffMaxDistance = null;
    public float? FalloffStrength = null;

    [JsonIgnore]
    public bool HasAny =>
        SpatialEnabled.HasValue ||
        Volume.HasValue ||
        FalloffMaxDistance.HasValue ||
        FalloffStrength.HasValue;

    public void ApplyTo(AudioPixProperties target) {
        if(SpatialEnabled.HasValue) target.SpatialEnabled = SpatialEnabled.Value;
        if(Volume.HasValue) target.Volume = Volume.Value;
        if(FalloffMaxDistance.HasValue) target.FalloffMaxDistance = FalloffMaxDistance.Value;
        if(FalloffStrength.HasValue) target.FalloffStrength = FalloffStrength.Value;
    }
}
