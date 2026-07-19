using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UI;

/// <summary>
/// Converts a 0-100 percent into a star GridLength equal to that percent, for the
/// "filled" column of a full-width progress bar (paired with
/// PercentToRemainingStarConverter on the "remaining" column). Unlike the fixed-track
/// PercentToWidthConverter, this lets the bar stretch to whatever width its container
/// has instead of a hardcoded pixel track.
/// </summary>
public sealed class PercentToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return new GridLength(percent, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The complement of PercentToStarConverter — (100 - percent) as a star GridLength for the unfilled remainder.</summary>
public sealed class PercentToRemainingStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return new GridLength(100 - percent, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
