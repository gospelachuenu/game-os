using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UI;

public sealed class DownloadingToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is UpdateItemState.Downloading ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
