namespace PyonPix.Shared.Structs.Pix;

public readonly struct PixUpdate(IPix pix, PixUpdateType type, PixUpdateOrigin origin, bool editFinished = true) {
    public IPix Pix { get; } = pix;
    public PixUpdateType Type { get; } = type;
    public PixUpdateOrigin Origin { get; } = origin;
    public bool EditFinished { get; } = editFinished;
}
