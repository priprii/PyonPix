using System.Numerics;
using System.Text.Json.Serialization;
using PyonPix.Shared.Structs.Renderer;
using static PyonPix.Shared.Utility.MathUtil;

namespace PyonPix.Shared.Structs.Pix.Properties;

public class RendererPixProperties : ILocal<SyncedRendererPixProperties> {
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public Vector4 ScreenTint = Vector4.One;
    public Vector4 EdgeColour = new(0.01f, 0.01f, 0.01f, 1);
    public Vector4 BackColour = new(0.01f, 0.01f, 0.01f, 1);

    public float BorderWidthH = 0f;
    public float BorderWidthV = 0f;
    public Vector4 BorderColour = new(0.01f, 0.01f, 0.01f, 1);
    public BorderMode BorderMode = BorderMode.Padding;

    public float BorderFeather = 2f;
    public float EdgeFeather = 0f;

    public bool Depth = true;
    public float DepthOffset = 0.1f;
    public DepthComparison DepthComparison = DepthComparison.LessEqual;
    public CullMode CullMode = CullMode.Back;

    public SyncedRendererPixProperties ToSynced() {
        return new SyncedRendererPixProperties {
            Position = Position.ToSynced(),
            Rotation = Rotation.ToSynced(),
            Scale = Scale.ToSynced(),
            ScreenTint = ScreenTint.ToSynced(),
            EdgeColour = EdgeColour.ToSynced(),
            BackColour = BackColour.ToSynced(),
            BorderWidthH = BorderWidthH,
            BorderWidthV = BorderWidthV,
            BorderColour = BorderColour.ToSynced(),
            BorderMode = BorderMode,
            BorderFeather = BorderFeather,
            EdgeFeather = EdgeFeather,
            Depth = Depth,
            DepthOffset = DepthOffset,
            DepthComparison = DepthComparison,
            CullMode = CullMode
        };
    }
}

public class SyncedRendererPixProperties : ISynced<RendererPixProperties> {
    public SyncedVector3 Position { get; set; }
    public SyncedQuaternion Rotation { get; set; }
    public SyncedVector3 Scale { get; set; }

    public SyncedVector4 ScreenTint { get; set; }
    public SyncedVector4 EdgeColour { get; set; }
    public SyncedVector4 BackColour { get; set; }

    public float BorderWidthH { get; set; }
    public float BorderWidthV { get; set; }
    public SyncedVector4 BorderColour { get; set; }
    public BorderMode BorderMode { get; set; }

    public float BorderFeather { get; set; }
    public float EdgeFeather { get; set; }

    public bool Depth { get; set; }
    public float DepthOffset { get; set; }
    public DepthComparison DepthComparison { get; set; }
    public CullMode CullMode { get; set; }

    public void ApplyTo(RendererPixProperties target) {
        target.Position = Position.ToLocal();
        target.Rotation = Rotation.ToLocal();
        target.Scale = Scale.ToLocal();
        target.ScreenTint = ScreenTint.ToLocal();
        target.EdgeColour = EdgeColour.ToLocal();
        target.BackColour = BackColour.ToLocal();
        target.BorderWidthH = BorderWidthH;
        target.BorderWidthV = BorderWidthV;
        target.BorderColour = BorderColour.ToLocal();
        target.BorderMode = BorderMode;
        target.BorderFeather = BorderFeather;
        target.EdgeFeather = EdgeFeather;
        target.Depth = Depth;
        target.DepthOffset = DepthOffset;
        target.DepthComparison = DepthComparison;
        target.CullMode = CullMode;
    }
}

[Serializable]
public class RendererPixVariantOverrides {
    public Vector3? Position = null;
    public Quaternion? Rotation = null;
    public Vector3? Scale = null;

    public Vector4? ScreenTint = null;
    public Vector4? EdgeColour = null;
    public Vector4? BackColour = null;

    public float? BorderWidthH = null;
    public float? BorderWidthV = null;
    public Vector4? BorderColour = null;
    public BorderMode? BorderMode = null;

    public float? BorderFeather = null;
    public float? EdgeFeather = null;

    public bool? Depth = null;
    public float? DepthOffset = null;
    public DepthComparison? DepthComparison = null;
    public CullMode? CullMode = null;

    [JsonIgnore]
    public bool HasAny =>
        Position.HasValue ||
        Rotation.HasValue ||
        Scale.HasValue ||
        ScreenTint.HasValue ||
        EdgeColour.HasValue ||
        BackColour.HasValue ||
        BorderWidthH.HasValue ||
        BorderWidthV.HasValue ||
        BorderColour.HasValue ||
        BorderMode.HasValue ||
        BorderFeather.HasValue ||
        EdgeFeather.HasValue ||
        Depth.HasValue ||
        DepthOffset.HasValue ||
        DepthComparison.HasValue ||
        CullMode.HasValue;

    public void ApplyTo(RendererPixProperties target) {
        if(Position.HasValue) target.Position = Position.Value;
        if(Rotation.HasValue) target.Rotation = Rotation.Value;
        if(Scale.HasValue) target.Scale = Scale.Value;
        if(ScreenTint.HasValue) target.ScreenTint = ScreenTint.Value;
        if(EdgeColour.HasValue) target.EdgeColour = EdgeColour.Value;
        if(BackColour.HasValue) target.BackColour = BackColour.Value;
        if(BorderWidthH.HasValue) target.BorderWidthH = BorderWidthH.Value;
        if(BorderWidthV.HasValue) target.BorderWidthV = BorderWidthV.Value;
        if(BorderColour.HasValue) target.BorderColour = BorderColour.Value;
        if(BorderMode.HasValue) target.BorderMode = BorderMode.Value;
        if(BorderFeather.HasValue) target.BorderFeather = BorderFeather.Value;
        if(EdgeFeather.HasValue) target.EdgeFeather = EdgeFeather.Value;
        if(Depth.HasValue) target.Depth = Depth.Value;
        if(DepthOffset.HasValue) target.DepthOffset = DepthOffset.Value;
        if(DepthComparison.HasValue) target.DepthComparison = DepthComparison.Value;
        if(CullMode.HasValue) target.CullMode = CullMode.Value;
    }
}
