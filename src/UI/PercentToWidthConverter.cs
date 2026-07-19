using System.Globalization;
using System.Windows.Data;

namespace UI;

/// <summary>
/// Converts a 0-100 percent value into a pixel width against a fixed 240px track,
/// used for the continue-playing progress bar in the hero strip.
/// </summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    private const double TrackWidth = 240;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double d ? d : 0;
        return TrackWidth * Math.Clamp(percent, 0, 100) / 100.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
