using System.Windows.Media;

namespace UI;

/// <summary>
/// Derives placeholder box-art gradients that are biased toward the active
/// DashboardTheme's accent hue, with a per-game hash only nudging hue/lightness within
/// a narrow band. This keeps tiles visually distinct from each other while reading as
/// part of the same palette as the ambient background — unlike fully independent
/// per-name hues, which clash badly against a strongly colored background.
/// </summary>
public static class TileArtGenerator
{
    private const double MaxHueDriftDegrees = 28;

    public static LinearGradientBrush GenerateGradient(string seed)
    {
        var theme = ThemeManager.Current;
        var baseHue = ToHsv(theme.AccentPrimary).Hue;

        var hash = 0;
        unchecked
        {
            foreach (var c in seed)
            {
                hash = hash * 31 + c;
            }
        }

        var normalizedHash = (Math.Abs(hash) % 1000) / 1000.0; // 0.0-1.0
        var hueDrift = (normalizedHash - 0.5) * 2 * MaxHueDriftDegrees;
        var hue = (baseHue + hueDrift + 360) % 360;

        var colorA = FromHsv(hue, 0.5, 0.28 + normalizedHash * 0.08);
        var colorB = FromHsv(hue, 0.7, 0.5 + normalizedHash * 0.1);

        return new LinearGradientBrush(colorA, colorB, angle: 150);
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta == 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60.0 % 2) - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
