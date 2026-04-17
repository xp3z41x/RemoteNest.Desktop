using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<RdpLauncherService> _logger;
    private int _disposed;

    public RdpLauncherService() : this(NullLogger<RdpLauncherService>.Instance) { }

    public RdpLauncherService(ILogger<RdpLauncherService> logger)
    {
        _logger = logger;
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

        var hasCredential = !string.IsNullOrEmpty(plainPassword)
                            && !string.IsNullOrEmpty(profile.Username);

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
        var rdpContent = BuildRdpSettings(profile, connectionTarget, hasCredential);
        File.WriteAllText(rdpPath, rdpContent, Encoding.Unicode); // mstsc prefers UTF-16 LE

        Process? mstsc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                UseShellExecute = true
            };
            psi.ArgumentList.Add(rdpPath);

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
                    p.Cleanup(_logger);
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
                p.Cleanup(_logger);
        }
    }

    private static string BuildRdpSettings(
        ConnectionProfile profile,
        string fullAddress,
        bool hasCredential)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"full address:s:{fullAddress}");

        if (!string.IsNullOrEmpty(profile.Username))
        {
            var user = string.IsNullOrEmpty(profile.Domain)
                ? profile.Username
                : $"{profile.Domain}\\{profile.Username}";
            sb.AppendLine($"username:s:{user}");
        }

        sb.AppendLine($"screen mode id:i:{(profile.FullScreen ? 2 : 1)}");
        sb.AppendLine($"desktopwidth:i:{profile.ScreenWidth}");
        sb.AppendLine($"desktopheight:i:{profile.ScreenHeight}");
        sb.AppendLine($"session bpp:i:{profile.ColorDepth}");
        sb.AppendLine($"redirectclipboard:i:{(profile.RedirectClipboard ? 1 : 0)}");
        sb.AppendLine($"redirectdrives:i:{(profile.RedirectDrives ? 1 : 0)}");
        sb.AppendLine($"redirectprinters:i:{(profile.RedirectPrinters ? 1 : 0)}");
        sb.AppendLine($"audiomode:i:{(profile.RedirectAudio ? 0 : 2)}");
        sb.AppendLine($"enablecredsspsupport:i:{(profile.UseNetworkLevelAuth ? 1 : 0)}");
        sb.AppendLine("authentication level:i:0");

        if (hasCredential)
        {
            sb.AppendLine("prompt for credentials:i:0");
            sb.AppendLine("prompt for credentials on client:i:0");
        }

        return sb.ToString();
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
                _logger.LogWarning("cmdkey.exe failed to start");
                return;
            }
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                _logger.LogWarning("cmdkey inject timed out after 5s");
                return;
            }
            if (process.ExitCode != 0)
                _logger.LogWarning("cmdkey inject returned exit code {ExitCode}", process.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "cmdkey inject failed");
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
            _logger.LogWarning(ex, "cmdkey delete failed for {Target}", target);
        }
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete {Path}", path); }
    }

    private sealed class PendingLaunch
    {
        public string RdpFile { get; init; } = string.Empty;
        public string? CredentialTarget { get; init; }

        public void Cleanup(ILogger logger)
        {
            try
            {
                if (File.Exists(RdpFile)) File.Delete(RdpFile);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Temp .rdp cleanup failed for {Path}", RdpFile);
            }

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
                    logger.LogWarning(ex, "cmdkey delete failed for {Target}", CredentialTarget);
                }
            }
        }
    }
}
