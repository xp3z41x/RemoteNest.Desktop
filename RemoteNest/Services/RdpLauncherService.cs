using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using RemoteNest.Localization;
using RemoteNest.Models;

namespace RemoteNest.Services;

public class RdpLauncherService : IRdpLauncherService, IDisposable
{
    private static readonly string DefaultRdpPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Default.rdp");

    private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(30);
    private static readonly object DefaultRdpLock = new();

    private readonly ConcurrentBag<Task> _cleanupTasks = new();
    private readonly CancellationTokenSource _cts = new();

    public Task LaunchAsync(ConnectionProfile profile, string? plainPassword = null)
    {
        var host = profile.Port == 3389 ? profile.Host : $"{profile.Host}:{profile.Port}";

        // Inject credential via cmdkey before launching mstsc
        if (!string.IsNullOrEmpty(plainPassword) && !string.IsNullOrEmpty(profile.Username))
        {
            var user = string.IsNullOrEmpty(profile.Domain)
                ? profile.Username
                : $"{profile.Domain}\\{profile.Username}";

            InjectCredential(host, user, plainPassword);
        }

        // Write settings to Default.rdp (trusted by mstsc — no publisher warning)
        // then launch mstsc /v:host which overrides the address but keeps all other settings.
        string? originalContent = null;
        lock (DefaultRdpLock)
        {
            try
            {
                if (File.Exists(DefaultRdpPath))
                    originalContent = File.ReadAllText(DefaultRdpPath);
            }
            catch { /* best effort */ }

            var rdpContent = BuildRdpSettings(profile, plainPassword);
            File.WriteAllText(DefaultRdpPath, rdpContent, Encoding.UTF8);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"/v:{host}",
                UseShellExecute = true
            };

            if (Process.Start(psi) is null)
                throw new InvalidOperationException(TranslationSource.Get("FailedStartMstsc"));
        }
        catch
        {
            // Restore Default.rdp immediately on failure
            RestoreDefaultRdp(originalContent);
            throw;
        }

        // Schedule cleanup: restore Default.rdp and remove credential after mstsc reads them
        var capturedOriginal = originalContent;
        var cleanupTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CleanupDelay, _cts.Token);
                RestoreDefaultRdp(capturedOriginal);
                if (!string.IsNullOrEmpty(plainPassword))
                {
                    try { RemoveCredential(host); } catch { /* best effort */ }
                }
            }
            catch (OperationCanceledException)
            {
                RestoreDefaultRdp(capturedOriginal);
            }
        });
        _cleanupTasks.Add(cleanupTask);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Cancel();
        Task.WhenAll(_cleanupTasks.ToArray()).Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string BuildRdpSettings(ConnectionProfile profile, string? plainPassword)
    {
        var sb = new StringBuilder();

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

        if (!string.IsNullOrEmpty(plainPassword))
        {
            sb.AppendLine("prompt for credentials:i:0");
            sb.AppendLine("prompt for credentials on client:i:0");
        }

        return sb.ToString();
    }

    private static void RestoreDefaultRdp(string? originalContent)
    {
        lock (DefaultRdpLock)
        {
            try
            {
                if (originalContent is not null)
                    File.WriteAllText(DefaultRdpPath, originalContent, Encoding.UTF8);
                else if (File.Exists(DefaultRdpPath))
                    File.Delete(DefaultRdpPath);
            }
            catch { /* best effort */ }
        }
    }

    private static void InjectCredential(string host, string user, string password)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmdkey.exe",
            Arguments = $"/generic:TERMSRV/{host} /user:{user} /pass:{password}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process is not null)
        {
            process.WaitForExit(5000);
            Debug.WriteLine($"cmdkey inject exit code: {process.ExitCode}");
        }
    }

    private static void RemoveCredential(string host)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmdkey.exe",
            Arguments = $"/delete:TERMSRV/{host}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000);
    }
}
