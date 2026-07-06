using SharpDX.Direct3D11;

namespace PyonPix.Config.Global.Properties;

public enum DepthMode {
    Auto,
    First,
    Last
}

public enum FormatType {
    Auto = 0,
    R8G8B8A8_UNorm = 28,
    B8G8R8A8_UNorm = 87
}

public enum RenderMode {
    PreDraw,
    PostDraw
}

public enum ResourceBindingType {
    Auto,
    Bound,
    Unbound
}

public class RendererGlobalProperties {
    public FormatType Format = FormatType.Auto;
    public ResourceBindingType ResourceBindingType = ResourceBindingType.Auto;
    public DepthMode DepthMode = DepthMode.Auto;
    public RenderMode RenderMode = RenderMode.PreDraw;
    public bool UseShaderTarget = false;

    public bool IsBlendEnabled = true;
    public bool AlphaToCoverageEnable = false;
    public bool IndependentBlendEnable = false;
    public BlendOption SourceBlend = BlendOption.SourceAlpha;
    public BlendOption DestinationBlend = BlendOption.InverseSourceAlpha;
    public BlendOperation BlendOperation = BlendOperation.Add;
    public BlendOption SourceAlphaBlend = BlendOption.One;
    public BlendOption DestinationAlphaBlend = BlendOption.Zero;
    public BlendOperation AlphaBlendOperation = BlendOperation.Add;
    public ColorWriteMaskFlags RenderTargetWriteMask = ColorWriteMaskFlags.All;
}
