using System.Drawing;
using System.Numerics;

namespace PyonPix.Shared.Extensions;

public static class MathEx {
    public static Vector3 ToVector3(this Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    //public static Color ToColor(this Vector3 vector) => Color.FromArgb((int)(vector.X * 255), (int)(vector.Y * 255), (int)(vector.Z * 255));
    public static Color ToColor(this Vector3 v) => Color.FromArgb(v.X.ToByte(), v.Y.ToByte(), v.Z.ToByte());
    public static int ToByte(this float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
