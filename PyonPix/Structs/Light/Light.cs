using System.Numerics;

namespace PyonPix.Structs.Light;

public struct Light {
    public nint Address;
    public Vector3? ScreenAverage;

    // ring buffer for smoothing light transition
    public Vector3[] History; // colour samples
    public long[] HistoryTicks;
    public int HistoryCount;
    public int HistoryIndex;
    public long LastTimestamp;
}
