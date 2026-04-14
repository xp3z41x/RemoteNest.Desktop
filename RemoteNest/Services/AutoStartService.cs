using System.Diagnostics;
using Microsoft.Win32;

namespace RemoteNest.Services;

/// <summary>
/// Manages the "start with Windows" toggle via HKCU Run registry key.
/// HKCU is per-user — each Windows user independently opts in, even in per-machine installs.
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RemoteNest";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static void Enable()
    {
        try
        {
            var exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            // Quote the path in case it contains spaces (e.g. "C:\Program Files\RemoteNest\RemoteNest.exe").
            key?.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        catch
        {
            /* best effort — UI reads IsEnabled() to reflect actual state */
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            /* best effort */
        }
    }

    private static string? GetExecutablePath()
    {
        // MainModule.FileName returns the full path of the current .exe — works in
        // both per-user (AppData\Local\Programs) and per-machine (Program Files) installs.
        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
