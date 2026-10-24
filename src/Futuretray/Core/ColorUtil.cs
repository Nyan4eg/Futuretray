using System.Windows.Media;

namespace Futuretray.Core;

public static class ColorUtil
{
    public static readonly string[] Palette =
    {
        "accent", "#6D5DFC", "#3B82F6", "#0EA5E9", "#14B8A6", "#22C55E",
        "#F59E0B", "#F97316", "#EF4444", "#EC4899", "#A855F7", "#64748B",
    };

    public static Color Resolve(string? value) =>
        value == null || value == "accent" || !TryParse(value, out var c) ? ThemeManager.AccentColor : c;

    public static bool TryParse(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (!v.StartsWith('#')) v = "#" + v;
        if (v.Length is not (7 or 9)) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(v);
            color.A = 255;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Color Mix(Color a, Color b, double k) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * k),
        (byte)(a.R + (b.R - a.R) * k),
        (byte)(a.G + (b.G - a.G) * k),
        (byte)(a.B + (b.B - a.B) * k));

    public static Color Lighten(Color c, double k) => Mix(c, Colors.White, k);
    public static Color Darken(Color c, double k) => Mix(c, Colors.Black, k);
    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
