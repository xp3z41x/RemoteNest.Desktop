using RemoteNest.Models;

namespace RemoteNest.Services;

public interface IRdpLauncherService
{
    /// <summary>
    /// Launches an RDP session for the given profile:
    /// generates a temp .rdp file, injects credentials via cmdkey, starts mstsc.exe,
    /// and schedules cleanup of credentials and the temp file.
    /// </summary>
    Task LaunchAsync(ConnectionProfile profile, string? plainPassword = null);
}
