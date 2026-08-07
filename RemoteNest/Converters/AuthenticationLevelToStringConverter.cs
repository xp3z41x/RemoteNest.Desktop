using System.Globalization;
using System.Windows.Data;
using RemoteNest.Localization;

namespace RemoteNest.Converters;

/// <summary>Renders an `authentication level` value (0–3) as its localized description.</summary>
public class AuthenticationLevelToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            0 => TranslationSource.Get("AuthLevelConnectAlways"),
            1 => TranslationSource.Get("AuthLevelRefuse"),
            2 => TranslationSource.Get("AuthLevelWarn"),
            3 => TranslationSource.Get("AuthLevelUnspecified"),
            _ => string.Empty
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
