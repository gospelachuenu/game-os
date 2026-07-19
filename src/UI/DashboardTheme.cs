using System.Windows.Media;

namespace UI;

public sealed class DashboardTheme
{
    public required string Name { get; init; }
    public required Color AccentPrimary { get; init; }
    public required Color AccentSecondary { get; init; }

    public static readonly DashboardTheme Violet = new()
    {
        Name = "Violet",
        AccentPrimary = Color.FromRgb(0x8F, 0x6B, 0xFF),
        AccentSecondary = Color.FromRgb(0x4A, 0x3C, 0xE0),
    };

    public static readonly DashboardTheme OceanBlue = new()
    {
        Name = "Ocean Blue",
        AccentPrimary = Color.FromRgb(0x2E, 0xC8, 0xFF),
        AccentSecondary = Color.FromRgb(0x1A, 0x5C, 0xE8),
    };

    public static readonly DashboardTheme Emerald = new()
    {
        Name = "Emerald",
        AccentPrimary = Color.FromRgb(0x3B, 0xE0, 0x9A),
        AccentSecondary = Color.FromRgb(0x1A, 0x8A, 0x5C),
    };

    public static readonly DashboardTheme Ember = new()
    {
        Name = "Ember",
        AccentPrimary = Color.FromRgb(0xFF, 0x7A, 0x4A),
        AccentSecondary = Color.FromRgb(0xE0, 0x3C, 0x5C),
    };

    /// <summary>Matched to the ambient background video's palette (yellow-green ribbon highlight / deep green field).</summary>
    public static readonly DashboardTheme MeadowGreen = new()
    {
        Name = "Meadow Green",
        AccentPrimary = Color.FromRgb(0xA8, 0xE0, 0x3B),
        AccentSecondary = Color.FromRgb(0x2E, 0x8A, 0x2A),
    };

    public static readonly IReadOnlyList<DashboardTheme> All = new[]
    {
        MeadowGreen, Violet, OceanBlue, Emerald, Ember,
    };
}
