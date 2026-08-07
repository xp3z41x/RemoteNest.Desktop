using System.Globalization;
using RemoteNest.Services;

namespace RemoteNest.Localization;

/// <summary>
/// Resolves and applies the UI language. Order: explicit user choice saved in
/// settings.json → Windows display language (any pt-* maps to pt-BR) → English.
/// Auto-detection is not persisted, so changing the OS language changes the app
/// language until the user picks one explicitly.
/// </summary>
public static class LanguageManager
{
    private const string SettingsKey = "language";

    public static string[] SupportedLanguages => ["en", "pt-BR"];
    public static string[] SupportedLanguageNames => ["English", "Português (Brasil)"];

    public static void Initialize()
    {
        var saved = SettingsStore.Get(SettingsKey);
        var code = ResolveSupported(saved ?? CultureInfo.CurrentUICulture.Name);
        ApplyCulture(code);
    }

    /// <summary>Explicit user choice — applies and persists.</summary>
    public static void SetLanguage(string cultureCode)
    {
        var code = ResolveSupported(cultureCode);
        ApplyCulture(code);
        SettingsStore.Set(SettingsKey, code);
    }

    public static string GetCurrentLanguage() =>
        ResolveSupported(TranslationSource.Instance.CurrentCulture.Name);

    /// <summary>Maps any culture name onto a supported language, falling back to English.</summary>
    private static string ResolveSupported(string cultureName) =>
        cultureName.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en";

    private static void ApplyCulture(string code)
    {
        var culture = new CultureInfo(code);
        // Cover both the XAML binding source and any direct CurrentUICulture consumers.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        TranslationSource.Instance.CurrentCulture = culture;
    }
}
