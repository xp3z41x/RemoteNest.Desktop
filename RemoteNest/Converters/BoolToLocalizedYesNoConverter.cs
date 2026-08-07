using System.Globalization;
using System.Windows.Data;
using RemoteNest.Localization;

namespace RemoteNest.Converters;

public class BoolToLocalizedYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TranslationSource.Get("Yes") : TranslationSource.Get("No");
        return TranslationSource.Get("No");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
