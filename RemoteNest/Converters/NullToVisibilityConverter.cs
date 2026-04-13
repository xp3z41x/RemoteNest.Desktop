using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RemoteNest.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>When false (default): null/empty → Visible, non-null → Collapsed. When true: inverted.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNullOrEmpty = value is null || (value is string s && string.IsNullOrEmpty(s));

        if (Invert)
            return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;

        return isNullOrEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
