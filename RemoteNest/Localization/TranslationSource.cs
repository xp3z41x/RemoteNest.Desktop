using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace RemoteNest.Localization;

public class TranslationSource : INotifyPropertyChanged
{
    public static TranslationSource Instance { get; } = new();

    private readonly ResourceManager _resourceManager = new("RemoteNest.Resources.Strings", typeof(TranslationSource).Assembly);

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    public string this[string key]
    {
        get
        {
            var result = _resourceManager.GetString(key, _currentCulture);
            return result ?? $"[{key}]";
        }
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value)) return;
            _currentCulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        }
    }

    /// <summary>Shorthand for C# code: TranslationSource.Get("Key")</summary>
    public static string Get(string key) => Instance[key];

    /// <summary>Shorthand with string.Format: TranslationSource.Format("Key", arg0, arg1)</summary>
    public static string Format(string key, params object[] args) => string.Format(Instance[key], args);

    public event PropertyChangedEventHandler? PropertyChanged;
}
