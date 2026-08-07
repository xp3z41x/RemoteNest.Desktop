using System.Globalization;
using System.Windows.Data;
using RemoteNest.Localization;

namespace RemoteNest.Converters;

public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt && dt != default)
        {
            var local = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
            return local.ToString("g", TranslationSource.Instance.CurrentCulture);
        }
        return TranslationSource.Get("Never");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
