namespace PyonPix.Shared.Sync.Dto.Client;

public enum AnimationType {
    Static,
    Wave,
    Chase,
    Pulse,
    EasePulse,
    RainbowWave,
    RainbowPulse
}

public sealed class StyleDto {
    public string? ColourA { get; set; } = "#FFFFFF";
    public string? ColourB { get; set; }
    public string? GlowA { get; set; } = "#FFFFFF";
    public string? GlowB { get; set; }
    public AnimationType AnimationType { get; set; } = AnimationType.Static;
}
