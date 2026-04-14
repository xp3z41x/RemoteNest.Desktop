using System.IO;
using System.Linq;
using System.Text.Json;
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
/// Persists and applies the user's preferred color theme.
/// Mirrors the LanguageManager pattern: shares the same %APPDATA%\RemoteNest\settings.json file,
/// using a new "theme" key. The DarkBlue theme is driven by a merged ResourceDictionary
/// (Resources/DarkBlueTheme.xaml) that overrides ModernWpfUI surface brushes.
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RemoteNest", "settings.json");

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
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            var raw = settings?.GetValueOrDefault("theme");
            if (raw is null) return null;
            return Enum.TryParse<AppTheme>(raw, ignoreCase: true, out var parsed) ? parsed : null;
        }
        catch { return null; }
    }

    private static void Save(AppTheme theme)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var settings = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                    if (existing is not null) settings = existing;
                }
            }
            catch { /* start fresh */ }
            settings["theme"] = theme.ToString();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
