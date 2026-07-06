using System.Numerics;
using System.Text.Json.Serialization;
using PyonPix.Shared.Structs.Light;
using static PyonPix.Shared.Utility.MathUtil;

namespace PyonPix.Shared.Structs.Pix.Properties;

public class LightPixProperties : ILocal<SyncedLightPixProperties> {
    public bool Enabled = true;

    public LightFlags Flags = LightFlags.Reflections | LightFlags.DynamicShadows | LightFlags.CharacterShadows | LightFlags.ObjectShadows;
    public LightType LightType = LightType.PointLight;

    public Vector3 Position;
    public Quaternion Rotation;

    public Vector4 Colour = Vector4.One;
    public float Intensity = 1f;
    public float ScreenColourInfluence = 1f;
    public float InfluenceColourIntensity = 2f;
    public float InfluenceBrightnessIntensity = 1f;
    public float InfluenceGammaCurve = 0.5f;

    public float Range = 5f;
    public float LightAngle = 180f;

    public FalloffType FalloffType = FalloffType.Quadratic;
    public float FalloffAngle = 2f;
    public float FalloffPower = 0.3f;

    public float ShadowRange = 10f;
    public float ShadowNear = 0f;
    public float ShadowFar = 10f;

    public SyncedLightPixProperties ToSynced() {
        return new SyncedLightPixProperties {
            Enabled = Enabled,
            Flags = Flags,
            LightType = LightType,
            Position = Position.ToSynced(),
            Rotation = Rotation.ToSynced(),
            Colour = Colour.ToSynced(),
            Intensity = Intensity,
            ScreenColourInfluence = ScreenColourInfluence,
            InfluenceColourIntensity = InfluenceColourIntensity,
            InfluenceBrightnessIntensity = InfluenceBrightnessIntensity,
            InfluenceGammaCurve = InfluenceGammaCurve,
            Range = Range,
            LightAngle = LightAngle,
            FalloffType = FalloffType,
            FalloffAngle = FalloffAngle,
            FalloffPower = FalloffPower,
            ShadowRange = ShadowRange,
            ShadowNear = ShadowNear,
            ShadowFar = ShadowFar
        };
    }
}

public class SyncedLightPixProperties : ISynced<LightPixProperties> {
    public bool Enabled { get; set; }

    public LightFlags Flags { get; set; }
    public LightType LightType { get; set; }

    public SyncedVector3 Position { get; set; }
    public SyncedQuaternion Rotation { get; set; }

    public SyncedVector4 Colour { get; set; }
    public float Intensity { get; set; }
    public float ScreenColourInfluence { get; set; }
    public float InfluenceColourIntensity { get; set; }
    public float InfluenceBrightnessIntensity { get; set; }
    public float InfluenceGammaCurve { get; set; }

    public float Range { get; set; }
    public float LightAngle { get; set; }

    public FalloffType FalloffType { get; set; }
    public float FalloffAngle { get; set; }
    public float FalloffPower { get; set; }

    public float ShadowRange { get; set; }
    public float ShadowNear { get; set; }
    public float ShadowFar { get; set; }

    public void ApplyTo(LightPixProperties target) {
        target.Enabled = Enabled;
        target.Flags = Flags;
        target.LightType = LightType;
        target.Position = Position.ToLocal();
        target.Rotation = Rotation.ToLocal();
        target.Colour = Colour.ToLocal();
        target.Intensity = Intensity;
        target.ScreenColourInfluence = ScreenColourInfluence;
        target.InfluenceColourIntensity = InfluenceColourIntensity;
        target.InfluenceBrightnessIntensity = InfluenceBrightnessIntensity;
        target.InfluenceGammaCurve = InfluenceGammaCurve;
        target.Range = Range;
        target.LightAngle = LightAngle;
        target.FalloffType = FalloffType;
        target.FalloffAngle = FalloffAngle;
        target.FalloffPower = FalloffPower;
        target.ShadowRange = ShadowRange;
        target.ShadowNear = ShadowNear;
        target.ShadowFar = ShadowFar;
    }
}

[Serializable]
public class LightPixVariantOverrides {
    public bool? Enabled = null;

    public LightFlags? Flags = null;
    public LightType? LightType = null;

    public Vector3? Position = null;
    public Quaternion? Rotation = null;

    public Vector4? Colour = null;
    public float? Intensity = null;
    public float? ScreenColourInfluence = null;
    public float? InfluenceColourIntensity = null;
    public float? InfluenceBrightnessIntensity = null;
    public float? InfluenceGammaCurve = null;

    public float? Range = null;
    public float? LightAngle = null;

    public FalloffType? FalloffType = null;
    public float? FalloffAngle = null;
    public float? FalloffPower = null;

    public float? ShadowRange = null;
    public float? ShadowNear = null;
    public float? ShadowFar = null;

    [JsonIgnore]
    public bool HasAny =>
        Enabled.HasValue ||
        Flags.HasValue ||
        LightType.HasValue ||
        Position.HasValue ||
        Rotation.HasValue ||
        Colour.HasValue ||
        Intensity.HasValue ||
        ScreenColourInfluence.HasValue ||
        InfluenceColourIntensity.HasValue ||
        InfluenceBrightnessIntensity.HasValue ||
        InfluenceGammaCurve.HasValue ||
        Range.HasValue ||
        LightAngle.HasValue ||
        FalloffType.HasValue ||
        FalloffAngle.HasValue ||
        FalloffPower.HasValue ||
        ShadowRange.HasValue ||
        ShadowNear.HasValue ||
        ShadowFar.HasValue;

    public void ApplyTo(LightPixProperties target) {
        if(Enabled.HasValue) target.Enabled = Enabled.Value;
        if(Flags.HasValue) target.Flags = Flags.Value;
        if(LightType.HasValue) target.LightType = LightType.Value;
        if(Position.HasValue) target.Position = Position.Value;
        if(Rotation.HasValue) target.Rotation = Rotation.Value;
        if(Colour.HasValue) target.Colour = Colour.Value;
        if(Intensity.HasValue) target.Intensity = Intensity.Value;
        if(ScreenColourInfluence.HasValue) target.ScreenColourInfluence = ScreenColourInfluence.Value;
        if(InfluenceColourIntensity.HasValue) target.InfluenceColourIntensity = InfluenceColourIntensity.Value;
        if(InfluenceBrightnessIntensity.HasValue) target.InfluenceBrightnessIntensity = InfluenceBrightnessIntensity.Value;
        if(InfluenceGammaCurve.HasValue) target.InfluenceGammaCurve = InfluenceGammaCurve.Value;
        if(Range.HasValue) target.Range = Range.Value;
        if(LightAngle.HasValue) target.LightAngle = LightAngle.Value;
        if(FalloffType.HasValue) target.FalloffType = FalloffType.Value;
        if(FalloffAngle.HasValue) target.FalloffAngle = FalloffAngle.Value;
        if(FalloffPower.HasValue) target.FalloffPower = FalloffPower.Value;
        if(ShadowRange.HasValue) target.ShadowRange = ShadowRange.Value;
        if(ShadowNear.HasValue) target.ShadowNear = ShadowNear.Value;
        if(ShadowFar.HasValue) target.ShadowFar = ShadowFar.Value;
    }
}
