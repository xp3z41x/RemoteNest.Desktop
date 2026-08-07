using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RemoteNest.Services;

public enum AppTheme
{
    System,
    Light,
    DarkBlue,
    Dark
}

/// <summary>
/// Persists and applies the user's preferred color theme via SettingsStore ("theme" key).
/// The DarkBlue theme is driven by a merged ResourceDictionary
/// (Resources/DarkBlueTheme.xaml) that overrides ModernWpfUI surface brushes.
/// </summary>
public static class ThemeManager
{
    private const string SettingsKey = "theme";

    private static readonly Uri DarkBlueDictUri =
        new("pack://application:,,,/Resources/DarkBlueTheme.xaml", UriKind.Absolute);

    public static AppTheme[] Supported =>
        new[] { AppTheme.System, AppTheme.Light, AppTheme.DarkBlue, AppTheme.Dark };

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public static void Initialize()
    {
        var saved = LoadSaved();
        Apply(saved ?? AppTheme.System, persist: false);
    }

    public static void Apply(AppTheme theme, bool persist = true)
    {
        CurrentTheme = theme;
        var tm = ModernWpf.ThemeManager.Current;

        // Always strip the DarkBlue dictionary first — Apply() may be flipping between themes.
        RemoveDarkBlueDict();

        switch (theme)
        {
            case AppTheme.Light:
                tm.ApplicationTheme = ModernWpf.ApplicationTheme.Light;
                tm.AccentColor = Color.FromRgb(0x00, 0x7A, 0x93);
                break;
            case AppTheme.DarkBlue:
                tm.ApplicationTheme = ModernWpf.ApplicationTheme.Dark;
                tm.AccentColor = Color.FromRgb(0x4F, 0xC3, 0xF7);
                MergeDarkBlueDict();
                break;
            case AppTheme.Dark:
                tm.ApplicationTheme = ModernWpf.ApplicationTheme.Dark;
                tm.AccentColor = null;
                break;
            case AppTheme.System:
            default:
                tm.ApplicationTheme = null; // follows Windows
                tm.AccentColor = null;
                break;
        }

        // The acrylic tint is theme-derived — refresh it whenever the palette changes.
        AcrylicHelper.ReapplyAll();

        if (persist) Save(theme);
    }

    private static void MergeDarkBlueDict()
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Resources.MergedDictionaries.Any(d => d.Source == DarkBlueDictUri)) return;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = DarkBlueDictUri });
    }

    private static void RemoveDarkBlueDict()
    {
        var app = Application.Current;
        if (app is null) return;
        var existing = app.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == DarkBlueDictUri);
        if (existing is not null) app.Resources.MergedDictionaries.Remove(existing);
    }

    private static AppTheme? LoadSaved()
    {
        var raw = SettingsStore.Get(SettingsKey);
        if (raw is null) return null;
        return Enum.TryParse<AppTheme>(raw, ignoreCase: true, out var parsed) ? parsed : null;
    }

    private static void Save(AppTheme theme) => SettingsStore.Set(SettingsKey, theme.ToString());
}
