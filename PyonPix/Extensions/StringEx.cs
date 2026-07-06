namespace PyonPix.Extensions;

public static class StringEx {
    public static string TruncateMiddle(this string text, int max) {
        if(string.IsNullOrEmpty(text) || text.Length <= max)
            return text;

        int keep = (max - 3) / 2;
        return text[..keep] + "..." + text[^keep..];
    }
}
