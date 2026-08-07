using System.Text.Json;
using RemoteNest.Serialization;

namespace RemoteNest.Services;

/// <summary>
/// Single reader/writer for <c>%APPDATA%\RemoteNest\settings.json</c>. Loads the file
/// once and caches it, so the settings consumers on the startup path share one parse.
/// Uses source-generated JSON metadata (no reflection warm-up).
/// </summary>
public static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RemoteNest", "settings.json");

    private static readonly object Gate = new();
    private static Dictionary<string, string>? _cache;

    public static string? Get(string key)
    {
        lock (Gate)
        {
            return Load().GetValueOrDefault(key);
        }
    }

    public static void Set(string key, string value)
    {
        lock (Gate)
        {
            var settings = Load();
            settings[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(settings, AppJsonContext.Default.DictionaryStringString));
            }
            catch { /* best effort — settings loss is preferable to a crash */ }
        }
    }

    private static Dictionary<string, string> Load()
    {
        if (_cache is not null) return _cache;

        try
        {
            if (File.Exists(SettingsPath))
            {
                _cache = JsonSerializer.Deserialize(
                    File.ReadAllText(SettingsPath), AppJsonContext.Default.DictionaryStringString);
            }
        }
        catch { /* corrupted settings file — start fresh */ }

        return _cache ??= new Dictionary<string, string>();
    }
}
