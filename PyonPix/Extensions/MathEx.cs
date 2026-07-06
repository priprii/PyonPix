using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using PyonPix.Shared.Structs.Renderer;

namespace PyonPix.Extensions;

public static class MathEx {
    public static float DegToRad(this float degrees) => degrees * (MathF.PI / 180f);
    public static float RadToDeg(this float radians) => radians * (180f / MathF.PI);

    public static Vector3 QuaternionToEulerDeg(this Quaternion q) {
        q = Quaternion.Normalize(q);

        float yaw = MathF.Atan2(2f * ((q.W * q.Y) + (q.X * q.Z)), 1f - (2f * ((q.Y * q.Y) + (q.X * q.X))));
        float sinp = 2f * ((q.W * q.X) - (q.Z * q.Y));
        float pitch = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);
        float roll = MathF.Atan2(2f * ((q.W * q.Z) + (q.Y * q.X)), 1f - (2f * ((q.Z * q.Z) + (q.X * q.X))));

        return new Vector3(pitch.RadToDeg(), yaw.RadToDeg(), roll.RadToDeg());
    }

    public static nint ToLParam(this Vector2 value) => ((int)value.Y << 16) | ((int)value.X & 0xFFFF);

    public static LUID ToLUID(this long value) => new LUID {
        LowPart = (uint)(value & 0xFFFFFFFF),
        HighPart = (int)(value >> 32)
    };

    public static uint ToU32(this Vector4 value) => ImGui.GetColorU32(value);
    public static Vector4 ToVector4(this uint value) => ImGui.ColorConvertU32ToFloat4(value);
}
