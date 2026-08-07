using System.Windows.Data;
using System.Windows.Markup;

namespace RemoteNest.Localization;

[MarkupExtensionReturnType(typeof(BindingExpression))]
public class StrExtension : MarkupExtension
{
    public string Key { get; set; }

    public StrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = TranslationSource.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
