using System.Diagnostics;
using System.Numerics;

namespace PyonPix.Shared.Utility;

public static class MathUtil {
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float TicksToSeconds(long ticks) => ticks / (float)Stopwatch.Frequency;

    public struct SyncedVector3 {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
    public static SyncedVector3 ToSynced(this Vector3 v) => new SyncedVector3 { X = v.X, Y = v.Y, Z = v.Z };
    public static Vector3 ToLocal(this SyncedVector3 v) => new Vector3 { X = v.X, Y = v.Y, Z = v.Z };

    public struct SyncedVector4 {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }
    public static SyncedVector4 ToSynced(this Vector4 v) => new SyncedVector4 { X = v.X, Y = v.Y, Z = v.Z, W = v.W };
    public static Vector4 ToLocal(this SyncedVector4 v) => new Vector4 { X = v.X, Y = v.Y, Z = v.Z, W = v.W };

    public struct SyncedQuaternion {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }
    public static SyncedQuaternion ToSynced(this Quaternion v) => new SyncedQuaternion { X = v.X, Y = v.Y, Z = v.Z, W = v.W };
    public static Quaternion ToLocal(this SyncedQuaternion v) => new Quaternion { X = v.X, Y = v.Y, Z = v.Z, W = v.W };
}
