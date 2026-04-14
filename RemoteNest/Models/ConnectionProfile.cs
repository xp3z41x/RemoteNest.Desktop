using System.ComponentModel.DataAnnotations;

namespace RemoteNest.Models;

/// <summary>
/// Represents a saved RDP connection profile with all configuration settings.
/// </summary>
public class ConnectionProfile
{
    public int Id { get; set; }

    /// <summary>Friendly display name for the connection.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Logical group/folder (e.g. "Production", "Clients").</summary>
    [MaxLength(200)]
    public string Group { get; set; } = string.Empty;

    /// <summary>IP address or hostname of the remote machine.</summary>
    [Required, MaxLength(500)]
    public string Host { get; set; } = string.Empty;

    /// <summary>RDP port (default 3389).</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 3389;

    /// <summary>Username for the RDP session.</summary>
    [MaxLength(200)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Password encrypted with DPAPI. Never stored in plain text.</summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>Windows domain (optional).</summary>
    [MaxLength(200)]
    public string Domain { get; set; } = string.Empty;

    [Range(640, 7680)]
    public int ScreenWidth { get; set; } = 1920;

    [Range(480, 4320)]
    public int ScreenHeight { get; set; } = 1080;
    public bool FullScreen { get; set; }

    /// <summary>Valid color depth values for RDP connections.</summary>
    public static readonly HashSet<string> ValidColorDepths = ["15", "16", "24", "32"];

    /// <summary>Color depth: 15, 16, 24, or 32.</summary>
    [MaxLength(2)]
    public string ColorDepth { get; set; } = "32";

    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool RedirectPrinters { get; set; }
    public bool RedirectAudio { get; set; } = true;
    public bool UseNetworkLevelAuth { get; set; } = true;

    /// <summary>When true, this profile auto-connects when the app starts.</summary>
    public bool AutoConnectOnStartup { get; set; }

    [MaxLength(2000)]
    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastConnectedAt { get; set; }
    public int ConnectionCount { get; set; }
}
