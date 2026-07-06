using System.Drawing;
using System.Numerics;

namespace PyonPix.Shared.Extensions;

public static class StringEx {
    public static Vector3 ToVector3(this string s) {
        if(string.IsNullOrWhiteSpace(s)) return Vector3.One;
        try {
            var c = ColorTranslator.FromHtml(s);
            return c.ToVector3();
        } catch {
            return Vector3.One;
        }
    }
}
