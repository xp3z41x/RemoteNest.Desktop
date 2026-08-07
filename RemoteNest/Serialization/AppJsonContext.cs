using System.Text.Json.Serialization;
using RemoteNest.Models;

namespace RemoteNest.Serialization;

/// <summary>
/// Password-free projection of <see cref="ConnectionProfile"/> used by JSON export and
/// import. Property names match the model 1:1 — the mapping is reflection-driven and a
/// parity test enforces coverage.
///
/// Initializers mirror the model's, so a JSON file that omits a field imports as the
/// new-profile default rather than as the CLR zero value. That matters for security:
/// an absent <c>AuthenticationLevel</c> must mean "warn on a bad certificate", not
/// "connect silently". A test asserts these defaults never drift from the model.
/// </summary>
public sealed class ConnectionProfileExport
{
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;

    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public bool FullScreen { get; set; }
    public string ColorDepth { get; set; } = "32";
    public bool UseMultimon { get; set; }
    public string SelectedMonitors { get; set; } = string.Empty;
    public bool DynamicResolution { get; set; } = true;
    public bool SmartSizing { get; set; }
    public int DesktopScaleFactor { get; set; }

    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public string DrivesToRedirect { get; set; } = string.Empty;
    public bool RedirectPrinters { get; set; }
    public int AudioPlaybackMode { get; set; }
    public bool AudioCaptureMode { get; set; }
    public int AudioQualityMode { get; set; }
    public string CamerasToRedirect { get; set; } = string.Empty;
    public string PnpDevicesToRedirect { get; set; } = string.Empty;
    public string UsbDevicesToRedirect { get; set; } = string.Empty;
    public bool RedirectComPorts { get; set; }
    public bool RedirectSmartCards { get; set; } = true;
    public bool RedirectWebAuthn { get; set; } = true;
    public bool RedirectLocation { get; set; }
    public int KeyboardHook { get; set; } = 2;
    public bool UseNetworkLevelAuth { get; set; } = true;

    public string GatewayHostname { get; set; } = string.Empty;
    public int GatewayUsageMethod { get; set; } = 2;
    public int GatewayCredentialsSource { get; set; }
    public bool GatewayPromptCredentialOnce { get; set; } = true;
    public string LoadBalanceInfo { get; set; } = string.Empty;
    public string Pcb { get; set; } = string.Empty;
    public string KdcProxyName { get; set; } = string.Empty;
    public bool EnableRdsAadAuth { get; set; }
    public int ConnectionType { get; set; } = 7;
    public bool NetworkAutoDetect { get; set; } = true;
    public bool BandwidthAutoDetect { get; set; } = true;
    public bool Compression { get; set; } = true;
    public bool BitmapCachePersist { get; set; } = true;

    public bool DisableWallpaper { get; set; }
    public bool DisableFullWindowDrag { get; set; }
    public bool DisableMenuAnims { get; set; }
    public bool DisableThemes { get; set; }
    public bool AllowFontSmoothing { get; set; }
    public bool AllowDesktopComposition { get; set; }
    public bool VideoPlaybackMode { get; set; } = true;

    public bool AdministrativeSession { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public int AutoReconnectMaxRetries { get; set; } = 20;
    public bool DisplayConnectionBar { get; set; } = true;
    public bool PinConnectionBar { get; set; } = true;
    public bool PublicMode { get; set; }

    public int AuthenticationLevel { get; set; } = 2;
    public bool RestrictedAdmin { get; set; }
    public bool RemoteGuard { get; set; }
    public bool PromptForCredentials { get; set; }

    public bool RemoteAppMode { get; set; }
    public string RemoteAppProgram { get; set; } = string.Empty;
    public string RemoteAppName { get; set; } = string.Empty;
    public string RemoteAppCmdLine { get; set; } = string.Empty;

    public string ExtraSettings { get; set; } = string.Empty;

    public bool AutoConnectOnStartup { get; set; }
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Source-generated System.Text.Json metadata — avoids the reflection-based
/// serializer warm-up (settings.json is read during startup) and keeps the
/// serializer trim/AOT friendly.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<ConnectionProfileExport>))]
public sealed partial class AppJsonContext : JsonSerializerContext
{
}
