namespace RemoteNest.Models;

/// <summary>
/// Represents a saved RDP connection profile with all configuration settings.
/// Bounds (port range, resolution clamps, enum ranges) are enforced by the editor
/// UI and by ConnectionService import validation. Property initializers are the
/// defaults for new profiles and mirror the column DEFAULTs in <c>Database</c>.
/// </summary>
public class ConnectionProfile
{
    public int Id { get; set; }

    /// <summary>Friendly display name for the connection.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Logical group/folder (e.g. "Production", "Clients").</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>IP address or hostname of the remote machine.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>RDP port (default 3389).</summary>
    public int Port { get; set; } = 3389;

    /// <summary>Username for the RDP session.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password encrypted with DPAPI. Never stored in plain text.</summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>Windows domain (optional).</summary>
    public string Domain { get; set; } = string.Empty;

    // ---- Display ----

    public int ScreenWidth { get; set; } = 1920;

    public int ScreenHeight { get; set; } = 1080;
    public bool FullScreen { get; set; }

    /// <summary>Valid color depth values for RDP connections.</summary>
    public static readonly HashSet<string> ValidColorDepths = ["15", "16", "24", "32"];

    /// <summary>Color depth: 15, 16, 24, or 32.</summary>
    public string ColorDepth { get; set; } = "32";

    /// <summary>Use all (or selected) local monitors. Requires full screen.</summary>
    public bool UseMultimon { get; set; }

    /// <summary>Comma-separated display IDs (see `mstsc /l`). Only used with multimon.</summary>
    public string SelectedMonitors { get; set; } = string.Empty;

    /// <summary>Resize the remote resolution live with the window.</summary>
    public bool DynamicResolution { get; set; } = true;

    /// <summary>Scale (zoom) remote content to fit the window instead of scrollbars.</summary>
    public bool SmartSizing { get; set; }

    /// <summary>Valid desktop scale factors (0 = auto / don't emit).</summary>
    public static readonly HashSet<int> ValidScaleFactors = [0, 100, 125, 150, 175, 200, 250, 300, 400, 500];

    /// <summary>DPI scale override: 100–500, or 0 for auto (matches local device).</summary>
    public int DesktopScaleFactor { get; set; }

    // ---- Redirection ----

    public bool RedirectClipboard { get; set; } = true;

    /// <summary>Master drive-redirection toggle (redirectdrives).</summary>
    public bool RedirectDrives { get; set; }

    /// <summary>
    /// Granular drive list for drivestoredirect (e.g. "C:;D:" or "DynamicDrives").
    /// Empty = all drives when <see cref="RedirectDrives"/> is on.
    /// </summary>
    public string DrivesToRedirect { get; set; } = string.Empty;

    public bool RedirectPrinters { get; set; }

    /// <summary>audiomode: 0 = play locally, 1 = play on remote, 2 = don't play.</summary>
    public int AudioPlaybackMode { get; set; }

    /// <summary>Redirect the local microphone into the session (audiocapturemode).</summary>
    public bool AudioCaptureMode { get; set; }

    /// <summary>audioqualitymode: 0 = dynamic, 1 = medium, 2 = uncompressed.</summary>
    public int AudioQualityMode { get; set; }

    /// <summary>Semicolon list of camera device paths, "*" for all (camerastoredirect).</summary>
    public string CamerasToRedirect { get; set; } = string.Empty;

    /// <summary>MTP/PTP device list (devicestoredirect). Empty = none.</summary>
    public string PnpDevicesToRedirect { get; set; } = string.Empty;

    /// <summary>Opaque USB redirection selectors (usbdevicestoredirect). Empty = none.</summary>
    public string UsbDevicesToRedirect { get; set; } = string.Empty;

    public bool RedirectComPorts { get; set; }
    public bool RedirectSmartCards { get; set; } = true;
    public bool RedirectWebAuthn { get; set; } = true;
    public bool RedirectLocation { get; set; }

    /// <summary>keyboardhook: 0 = local, 1 = remote, 2 = remote in full screen only.</summary>
    public int KeyboardHook { get; set; } = 2;

    public bool UseNetworkLevelAuth { get; set; } = true;

    // ---- Gateway / network ----

    /// <summary>RD Gateway host. Empty = no gateway (gateway keys not emitted).</summary>
    public string GatewayHostname { get; set; } = string.Empty;

    /// <summary>gatewayusagemethod: 1 = always, 2 = bypass for local addresses.</summary>
    public int GatewayUsageMethod { get; set; } = 2;

    /// <summary>gatewaycredentialssource: 0 = password, 1 = smartcard, 4 = ask later.</summary>
    public int GatewayCredentialsSource { get; set; }

    /// <summary>Use the same credentials for gateway and remote host (promptcredentialonce).</summary>
    public bool GatewayPromptCredentialOnce { get; set; } = true;

    /// <summary>Broker/farm load balancing cookie (loadbalanceinfo).</summary>
    public string LoadBalanceInfo { get; set; } = string.Empty;

    /// <summary>Preconnection blob (pcb) — e.g. Hyper-V VM ID for VMConnect-style access.</summary>
    public string Pcb { get; set; } = string.Empty;

    /// <summary>KDC proxy FQDN for Kerberos over the internet (kdcproxyname).</summary>
    public string KdcProxyName { get; set; } = string.Empty;

    /// <summary>Use Microsoft Entra ID auth when the host supports it (enablerdsaadauth).</summary>
    public bool EnableRdsAadAuth { get; set; }

    /// <summary>connection type: 1–6 fixed profiles, 7 = auto detect.</summary>
    public int ConnectionType { get; set; } = 7;

    public bool NetworkAutoDetect { get; set; } = true;
    public bool BandwidthAutoDetect { get; set; } = true;
    public bool Compression { get; set; } = true;
    public bool BitmapCachePersist { get; set; } = true;

    // ---- Experience (only emitted when NetworkAutoDetect is off) ----

    public bool DisableWallpaper { get; set; }
    public bool DisableFullWindowDrag { get; set; }
    public bool DisableMenuAnims { get; set; }
    public bool DisableThemes { get; set; }
    public bool AllowFontSmoothing { get; set; }
    public bool AllowDesktopComposition { get; set; }
    public bool VideoPlaybackMode { get; set; } = true;

    // ---- Session behavior ----

    /// <summary>Connect to the admin/console session (administrative session).</summary>
    public bool AdministrativeSession { get; set; }

    public bool AutoReconnect { get; set; } = true;

    /// <summary>autoreconnect max retries: 0–200.</summary>
    public int AutoReconnectMaxRetries { get; set; } = 20;

    public bool DisplayConnectionBar { get; set; } = true;
    public bool PinConnectionBar { get; set; } = true;

    /// <summary>Public/kiosk mode — client caches nothing (public mode).</summary>
    public bool PublicMode { get; set; }

    // ---- Security ----

    /// <summary>
    /// authentication level: 0 = connect silently on cert failure, 1 = refuse,
    /// 2 = warn (mstsc's own default), 3 = unspecified.
    /// </summary>
    public int AuthenticationLevel { get; set; } = 2;

    /// <summary>mstsc /restrictedAdmin — no credential delegation to the host.</summary>
    public bool RestrictedAdmin { get; set; }

    /// <summary>mstsc /remoteGuard — credential guard. Mutually exclusive with RestrictedAdmin.</summary>
    public bool RemoteGuard { get; set; }

    /// <summary>mstsc /prompt — always ask for credentials; skips cmdkey injection.</summary>
    public bool PromptForCredentials { get; set; }

    // ---- RemoteApp ----

    /// <summary>Launch a single published app instead of a full desktop.</summary>
    public bool RemoteAppMode { get; set; }

    /// <summary>RemoteApp alias or path (remoteapplicationprogram).</summary>
    public string RemoteAppProgram { get; set; } = string.Empty;

    /// <summary>Display name shown while launching (remoteapplicationname).</summary>
    public string RemoteAppName { get; set; } = string.Empty;

    /// <summary>Optional command line for the RemoteApp (remoteapplicationcmdline).</summary>
    public string RemoteAppCmdLine { get; set; } = string.Empty;

    // ---- Passthrough ----

    /// <summary>
    /// Raw `key:type:value` lines appended to the generated .rdp file. Keys the app
    /// already emits win; signature/signscope/password lines are dropped. This is the
    /// escape hatch for every .rdp property without first-class UI.
    /// </summary>
    public string ExtraSettings { get; set; } = string.Empty;

    // ---- Misc ----

    /// <summary>When true, this profile auto-connects when the app starts.</summary>
    public bool AutoConnectOnStartup { get; set; }

    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastConnectedAt { get; set; }
    public int ConnectionCount { get; set; }

    /// <summary>
    /// Copy for the Duplicate action: every setting carried over, identity and
    /// statistics reset. MemberwiseClone keeps this immune to new-field drift.
    /// </summary>
    public ConnectionProfile CloneForDuplicate(string copySuffix)
    {
        var copy = (ConnectionProfile)MemberwiseClone();
        copy.Id = 0;
        copy.Name = Name + " " + copySuffix;
        copy.AutoConnectOnStartup = false; // never auto-connect a freshly duplicated profile
        copy.CreatedAt = DateTime.UtcNow;
        copy.LastConnectedAt = default;
        copy.ConnectionCount = 0;
        return copy;
    }
}
