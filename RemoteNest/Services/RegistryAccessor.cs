using Microsoft.Win32;

namespace RemoteNest.Services;

/// <summary>Which registry hive a policy value lives in.</summary>
public enum RegistryHiveKind
{
    CurrentUser,
    LocalMachine
}

/// <summary>
/// Narrow registry surface used by <see cref="RdpWarningPolicyService"/>. The interface
/// exists so the apply/revert logic can be unit-tested against an in-memory fake — tests
/// must never touch the real hives.
/// </summary>
public interface IRegistryAccessor
{
    /// <summary>Returns the DWORD at the given location, or null when the key/value is absent.</summary>
    int? ReadDword(RegistryHiveKind hive, string path, string name);

    void WriteDword(RegistryHiveKind hive, string path, string name, int value);

    /// <summary>Deletes the value; a missing key or value is not an error.</summary>
    void DeleteValue(RegistryHiveKind hive, string path, string name);
}

/// <summary>Real registry implementation (64-bit view, matching the app's bitness).</summary>
public sealed class RegistryAccessor : IRegistryAccessor
{
    public static readonly RegistryAccessor Instance = new();

    public int? ReadDword(RegistryHiveKind hive, string path, string name)
    {
        using var key = BaseKey(hive).OpenSubKey(path, writable: false);
        var raw = key?.GetValue(name);
        return raw is int i ? i : null;
    }

    public void WriteDword(RegistryHiveKind hive, string path, string name, int value)
    {
        using var key = BaseKey(hive).CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Cannot open registry key {hive}\\{path}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(RegistryHiveKind hive, string path, string name)
    {
        using var key = BaseKey(hive).OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey BaseKey(RegistryHiveKind hive) => hive switch
    {
        RegistryHiveKind.LocalMachine => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        _ => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
    };
}
