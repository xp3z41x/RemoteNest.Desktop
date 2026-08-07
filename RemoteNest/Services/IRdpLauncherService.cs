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

    /// <summary>
    /// Shadows (views or controls) an existing session on the profile's host via
    /// <c>mstsc /shadow</c>. Session IDs are ephemeral, so nothing is persisted.
    /// </summary>
    Task LaunchShadowAsync(ConnectionProfile profile, int sessionId, bool control, bool noConsentPrompt);
}
