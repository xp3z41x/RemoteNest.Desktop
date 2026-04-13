using System.Globalization;
using System.IO;
using System.Text.Json;

namespace RemoteNest.Localization;

public static class LanguageManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RemoteNest", "settings.json");

    public static string[] SupportedLanguages => ["en", "pt-BR"];
    public static string[] SupportedLanguageNames => ["English", "Portugu\u00eas (Brasil)"];

    public static void Initialize()
    {
        var saved = LoadSavedLanguage();
        if (saved is not null)
            SetLanguage(saved);
    }

    public static void SetLanguage(string cultureCode)
    {
        var culture = new CultureInfo(cultureCode);
        TranslationSource.Instance.CurrentCulture = culture;
        SaveLanguage(cultureCode);
    }

    public static string GetCurrentLanguage()
    {
        var culture = TranslationSource.Instance.CurrentCulture;
        // Normalize to our supported codes
        if (culture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            return "pt-BR";
        return "en";
    }

    private static string? LoadSavedLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return settings?.GetValueOrDefault("language");
        }
        catch { return null; }
    }

    private static void SaveLanguage(string cultureCode)
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
            settings["language"] = cultureCode;
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
