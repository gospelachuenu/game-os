using System.Windows;
using System.Windows.Media;

namespace UI;

/// <summary>
/// Applies a DashboardTheme to the application's resource dictionary as a handful of
/// named brushes. Every color reference in the UI (rail highlight, focus ring, hero
/// glow, progress bar, ambient background) binds to these via DynamicResource instead
/// of a hardcoded color, so switching themes recolors the whole dashboard at once.
/// </summary>
public static class ThemeManager
{
    public const string AccentPrimaryKey = "Theme.AccentPrimaryBrush";
    public const string AccentSecondaryKey = "Theme.AccentSecondaryBrush";
    public const string AccentPrimaryColorKey = "Theme.AccentPrimaryColor";
    public const string AccentSecondaryColorKey = "Theme.AccentSecondaryColor";
    public const string AccentPrimaryTintKey = "Theme.AccentPrimaryTintBrush";
    public const string AccentPrimaryFaintTintKey = "Theme.AccentPrimaryFaintTintBrush";

    public static event Action<DashboardTheme>? ThemeChanged;

    public static DashboardTheme Current { get; private set; } = DashboardTheme.Violet;

    public static void Apply(DashboardTheme theme)
    {
        Current = theme;
        var resources = Application.Current.Resources;

        resources[AccentPrimaryColorKey] = theme.AccentPrimary;
        resources[AccentSecondaryColorKey] = theme.AccentSecondary;
        resources[AccentPrimaryKey] = new SolidColorBrush(theme.AccentPrimary);
        resources[AccentSecondaryKey] = new SolidColorBrush(theme.AccentSecondary);
        resources[AccentPrimaryTintKey] = new SolidColorBrush(theme.AccentPrimary) { Opacity = 0.25 };
        resources[AccentPrimaryFaintTintKey] = new SolidColorBrush(theme.AccentPrimary) { Opacity = 0.10 };

        ThemeChanged?.Invoke(theme);
    }
}
