using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using RemoteNest.Localization;
using RemoteNest.Models;

namespace RemoteNest.Services;

/// <summary>
/// Launches mstsc.exe with a per-session temp .rdp file under %LOCALAPPDATA%\RemoteNest\temp
/// and injects credentials via cmdkey using ProcessStartInfo.ArgumentList for proper escaping.
/// Schedules cleanup of the temp file + credential after the connection window settles.
/// </summary>
public class RdpLauncherService : IRdpLauncherService, IDisposable
{
    private static readonly string TempDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "RemoteNest", "temp");

    private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(30);

    // Liberal hostname/IP pattern — allows DNS, IPv4, IPv6 in brackets. Rejects shell metacharacters.
    private static readonly Regex HostPattern = new(
        @"^(\[[0-9a-fA-F:]+\]|[A-Za-z0-9._-]+)$",
        RegexOptions.Compiled);

    private readonly ConcurrentDictionary<Guid, PendingLaunch> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    public RdpLauncherService()
    {
        Directory.CreateDirectory(TempDir);

        // Drain pending credentials if the process exits before cleanup tasks fire.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public Task LaunchAsync(ConnectionProfile profile, string? plainPassword = null)
    {
        if (!HostPattern.IsMatch(profile.Host))
        {
            throw new InvalidOperationException(
                TranslationSource.Format("InvalidHost", profile.Host));
        }

        var connectionTarget = profile.Port == 3389
            ? profile.Host
            : $"{profile.Host}:{profile.Port}";

        // cmdkey uses TERMSRV/<host> (no port) as the target for mstsc lookups.
        var credentialTarget = $"TERMSRV/{profile.Host}";

        // Restricted Admin / Remote Guard never delegate credentials, and /prompt asks
        // the user explicitly — cmdkey injection would be useless or counterproductive.
        var hasCredential = !string.IsNullOrEmpty(plainPassword)
                            && !string.IsNullOrEmpty(profile.Username)
                            && !profile.RestrictedAdmin
                            && !profile.RemoteGuard
                            && !profile.PromptForCredentials;

        if (hasCredential)
        {
            var user = string.IsNullOrEmpty(profile.Domain)
                ? profile.Username
                : $"{profile.Domain}\\{profile.Username}";

            InjectCredential(credentialTarget, user, plainPassword!);
        }

        // Per-session temp .rdp file — eliminates the Default.rdp overwrite dance
        // and prevents OneDrive from syncing profile settings to the cloud.
        var rdpPath = Path.Combine(TempDir, $"session-{Guid.NewGuid():N}.rdp");
        var rdpContent = RdpFileBuilder.Build(profile, connectionTarget, hasCredential);
        File.WriteAllText(rdpPath, rdpContent, Encoding.Unicode); // mstsc prefers UTF-16 LE

        Process? mstsc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                UseShellExecute = true
            };
            foreach (var arg in BuildLaunchArguments(profile, rdpPath))
                psi.ArgumentList.Add(arg);

            mstsc = Process.Start(psi);
            if (mstsc is null)
                throw new InvalidOperationException(TranslationSource.Get("FailedStartMstsc"));
        }
        catch
        {
            // Immediate cleanup on launch failure.
            TryDelete(rdpPath);
            if (hasCredential) TryRemoveCredential(credentialTarget);
            throw;
        }

        var pending = new PendingLaunch
        {
            RdpFile = rdpPath,
            CredentialTarget = hasCredential ? credentialTarget : null
        };
        var key = Guid.NewGuid();
        _pending[key] = pending;

        // Schedule cleanup — deletes temp file and removes credential whether cleanup delay
        // completes normally or is cancelled by Dispose/ProcessExit.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CleanupDelay, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* drain on shutdown */ }
            finally
            {
                if (_pending.TryRemove(key, out var p))
                    p.Cleanup();
            }
        });

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _cts.Cancel();
        DrainPending();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnProcessExit(object? sender, EventArgs e) => DrainPending();

    private void DrainPending()
    {
        foreach (var key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out var p))
                p.Cleanup();
        }
    }

    /// <summary>mstsc argument list for a normal launch: the .rdp file + CLI-only switches.</summary>
    internal static List<string> BuildLaunchArguments(ConnectionProfile profile, string rdpPath)
    {
        var args = new List<string> { rdpPath };
        if (profile.RestrictedAdmin)
            args.Add("/restrictedAdmin");
        else if (profile.RemoteGuard)
            args.Add("/remoteGuard");
        if (profile.PromptForCredentials)
            args.Add("/prompt");
        if (profile.PublicMode)
            args.Add("/public");
        return args;
    }

    /// <summary>mstsc argument list for shadowing an existing session (no .rdp file involved).</summary>
    internal static List<string> BuildShadowArguments(ConnectionProfile profile, int sessionId, bool control, bool noConsentPrompt)
    {
        var target = profile.Port == 3389 ? profile.Host : $"{profile.Host}:{profile.Port}";
        var args = new List<string> { $"/v:{target}", $"/shadow:{sessionId}" };
        if (control) args.Add("/control");
        if (noConsentPrompt) args.Add("/noConsentPrompt");
        return args;
    }

    public Task LaunchShadowAsync(ConnectionProfile profile, int sessionId, bool control, bool noConsentPrompt)
    {
        if (!HostPattern.IsMatch(profile.Host))
            throw new InvalidOperationException(TranslationSource.Format("InvalidHost", profile.Host));
        if (sessionId < 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId));

        var psi = new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            UseShellExecute = true
        };
        foreach (var arg in BuildShadowArguments(profile, sessionId, control, noConsentPrompt))
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException(TranslationSource.Get("FailedStartMstsc"));

        Log.Info($"Shadowing session {sessionId} on {profile.Host} (control={control})");
        return Task.CompletedTask;
    }


    private void InjectCredential(string target, string user, string password)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmdkey.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // ArgumentList escapes each element per Win32 CommandLineToArgvW rules,
        // preventing injection via spaces, quotes, backslashes, or shell metacharacters.
        psi.ArgumentList.Add($"/generic:{target}");
        psi.ArgumentList.Add($"/user:{user}");
        psi.ArgumentList.Add($"/pass:{password}");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Log.Warn("cmdkey.exe failed to start");
                return;
            }
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                Log.Warn("cmdkey inject timed out after 5s");
                return;
            }
            if (process.ExitCode != 0)
                Log.Warn($"cmdkey inject returned exit code {process.ExitCode}");
        }
        catch (Exception ex)
        {
            Log.Error("cmdkey inject failed", ex);
        }
    }

    private void TryRemoveCredential(string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmdkey.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add($"/delete:{target}");

        try
        {
            using var process = Process.Start(psi);
            if (process is not null && !process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"cmdkey delete failed for {target}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private sealed class PendingLaunch
    {
        public string RdpFile { get; init; } = string.Empty;
        public string? CredentialTarget { get; init; }

        public void Cleanup()
        {
            try
            {
                if (File.Exists(RdpFile)) File.Delete(RdpFile);
            }
            catch { /* temp file locked — best effort */ }

            if (!string.IsNullOrEmpty(CredentialTarget))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmdkey.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add($"/delete:{CredentialTarget}");

                try
                {
                    using var process = Process.Start(psi);
                    process?.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Log.Warn($"cmdkey delete failed for {CredentialTarget}", ex);
                }
            }
        }
    }
}
