using System.Text;
using System.Text.RegularExpressions;
using RemoteNest.Models;

namespace RemoteNest.Services;

/// <summary>
/// Generates .rdp file content from a <see cref="ConnectionProfile"/>. Shared by the
/// launcher (temp session file), the "Export as .rdp" command, and tests.
///
/// Emit policy: the core connection keys are always written, in the order mstsc's own
/// exports use. Every other key is emitted only when its value differs from the mstsc
/// absent-key default, or when its section is active (gateway, RemoteApp, custom
/// experience) — a smaller file means fewer settings to reason about. User passthrough
/// lines come last and never override app keys.
/// </summary>
public static class RdpFileBuilder
{
    /// <summary>Matches .rdp lines: key:type:value (type is s/i/b).</summary>
    internal static readonly Regex LinePattern = new(
        @"^(?<key>.+?):(?<type>[sib]):(?<value>.*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Keys never accepted from passthrough/import: file signatures would be invalidated
    /// by our regeneration anyway, and machine-bound password blobs are dead weight.
    /// </summary>
    internal static readonly HashSet<string> DroppedKeys =
        new(["signature", "signscope", "password 51"], StringComparer.OrdinalIgnoreCase);

    private static readonly Regex MonitorListPattern = new(@"^\d+(,\d+)*$", RegexOptions.Compiled);

    /// <summary>Maximum ExtraSettings size honored at emission (defense in depth).</summary>
    internal const int MaxExtraSettingsLength = 8192;

    /// <param name="fullAddress">host or host:port, already validated by the caller.</param>
    /// <param name="hasCredential">True when cmdkey injection happened — suppresses credential prompts.</param>
    /// <param name="forExport">True for user-facing .rdp exports: never writes the
    /// prompt-suppression lines, which only make sense alongside the app's cmdkey lifecycle.</param>
    public static string Build(ConnectionProfile profile, string fullAddress, bool hasCredential, bool forExport = false)
    {
        var sb = new StringBuilder();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, char type, object value)
        {
            sb.Append(key).Append(':').Append(type).Append(':')
              .AppendLine(SanitizeValue(value.ToString() ?? string.Empty));
            emitted.Add(key);
        }

        // ---- Core connection block ----
        Add("full address", 's', fullAddress);

        if (!string.IsNullOrEmpty(profile.Username))
        {
            var user = string.IsNullOrEmpty(profile.Domain)
                ? profile.Username
                : $"{profile.Domain}\\{profile.Username}";
            Add("username", 's', user);
        }

        Add("screen mode id", 'i', profile.FullScreen ? 2 : 1);
        Add("desktopwidth", 'i', profile.ScreenWidth);
        Add("desktopheight", 'i', profile.ScreenHeight);
        Add("session bpp", 'i', profile.ColorDepth);
        Add("redirectclipboard", 'i', profile.RedirectClipboard ? 1 : 0);
        Add("redirectdrives", 'i', profile.RedirectDrives ? 1 : 0);
        Add("redirectprinters", 'i', profile.RedirectPrinters ? 1 : 0);
        Add("audiomode", 'i', Math.Clamp(profile.AudioPlaybackMode, 0, 2));
        Add("enablecredsspsupport", 'i', profile.UseNetworkLevelAuth ? 1 : 0);
        Add("authentication level", 'i', Math.Clamp(profile.AuthenticationLevel, 0, 3));

        if (hasCredential && !forExport)
        {
            Add("prompt for credentials", 'i', 0);
            Add("prompt for credentials on client", 'i', 0);
        }

        // ---- Display ----
        if (profile.UseMultimon)
        {
            Add("use multimon", 'i', 1);
            if (MonitorListPattern.IsMatch(profile.SelectedMonitors))
                Add("selectedmonitors", 's', profile.SelectedMonitors);
        }
        if (profile.DynamicResolution) Add("dynamic resolution", 'i', 1);
        if (profile.SmartSizing) Add("smart sizing", 'i', 1);
        if (profile.DesktopScaleFactor != 0 && ConnectionProfile.ValidScaleFactors.Contains(profile.DesktopScaleFactor))
            Add("desktopscalefactor", 'i', profile.DesktopScaleFactor);

        // ---- Redirection ----
        if (profile.RedirectDrives)
            Add("drivestoredirect", 's', profile.DrivesToRedirect.Length > 0 ? profile.DrivesToRedirect : "*");
        if (profile.AudioCaptureMode) Add("audiocapturemode", 'i', 1);
        if (profile.AudioQualityMode != 0) Add("audioqualitymode", 'i', Math.Clamp(profile.AudioQualityMode, 0, 2));
        if (profile.CamerasToRedirect.Length > 0) Add("camerastoredirect", 's', profile.CamerasToRedirect);
        if (profile.PnpDevicesToRedirect.Length > 0) Add("devicestoredirect", 's', profile.PnpDevicesToRedirect);
        if (profile.UsbDevicesToRedirect.Length > 0) Add("usbdevicestoredirect", 's', profile.UsbDevicesToRedirect);
        if (profile.RedirectComPorts) Add("redirectcomports", 'i', 1);
        if (!profile.RedirectSmartCards) Add("redirectsmartcards", 'i', 0);
        if (!profile.RedirectWebAuthn) Add("redirectwebauthn", 'i', 0);
        if (profile.RedirectLocation) Add("redirectlocation", 'i', 1);
        if (profile.KeyboardHook != 2) Add("keyboardhook", 'i', Math.Clamp(profile.KeyboardHook, 0, 2));

        // ---- Gateway ----
        if (profile.GatewayHostname.Length > 0)
        {
            Add("gatewayhostname", 's', profile.GatewayHostname);
            Add("gatewayusagemethod", 'i', profile.GatewayUsageMethod is 1 or 2 ? profile.GatewayUsageMethod : 2);
            // Explicit settings — without this the client may ignore gatewayhostname.
            Add("gatewayprofileusagemethod", 'i', 1);
            Add("gatewaycredentialssource", 'i', profile.GatewayCredentialsSource is 0 or 1 or 4 ? profile.GatewayCredentialsSource : 0);
            Add("promptcredentialonce", 'i', profile.GatewayPromptCredentialOnce ? 1 : 0);
        }

        // ---- Connectivity extras ----
        if (profile.LoadBalanceInfo.Length > 0) Add("loadbalanceinfo", 's', profile.LoadBalanceInfo);
        if (profile.Pcb.Length > 0) Add("pcb", 's', profile.Pcb);
        if (profile.KdcProxyName.Length > 0) Add("kdcproxyname", 's', profile.KdcProxyName);
        if (profile.EnableRdsAadAuth) Add("enablerdsaadauth", 'i', 1);
        if (!profile.Compression) Add("compression", 'i', 0);
        if (!profile.BitmapCachePersist) Add("bitmapcachepersistenable", 'i', 0);
        if (!profile.BandwidthAutoDetect) Add("bandwidthautodetect", 'i', 0);

        // ---- Experience: only meaningful when auto-detect is off ----
        if (!profile.NetworkAutoDetect)
        {
            Add("networkautodetect", 'i', 0);
            Add("connection type", 'i', Math.Clamp(profile.ConnectionType, 1, 7));
            if (profile.DisableWallpaper) Add("disable wallpaper", 'i', 1);
            if (profile.DisableFullWindowDrag) Add("disable full window drag", 'i', 1);
            if (profile.DisableMenuAnims) Add("disable menu anims", 'i', 1);
            if (profile.DisableThemes) Add("disable themes", 'i', 1);
            if (profile.AllowFontSmoothing) Add("allow font smoothing", 'i', 1);
            if (profile.AllowDesktopComposition) Add("allow desktop composition", 'i', 1);
            if (!profile.VideoPlaybackMode) Add("videoplaybackmode", 'i', 0);
        }

        // ---- Session behavior ----
        if (profile.AdministrativeSession) Add("administrative session", 'i', 1);
        if (!profile.AutoReconnect) Add("autoreconnection enabled", 'i', 0);
        if (profile.AutoReconnectMaxRetries != 20)
            Add("autoreconnect max retries", 'i', Math.Clamp(profile.AutoReconnectMaxRetries, 0, 200));
        if (!profile.DisplayConnectionBar) Add("displayconnectionbar", 'i', 0);
        if (!profile.PinConnectionBar) Add("pinconnectionbar", 'i', 0);
        if (profile.PublicMode) Add("public mode", 'i', 1);

        // ---- RemoteApp ----
        if (profile.RemoteAppMode)
        {
            Add("remoteapplicationmode", 'i', 1);
            if (profile.RemoteAppProgram.Length > 0) Add("remoteapplicationprogram", 's', profile.RemoteAppProgram);
            if (profile.RemoteAppName.Length > 0) Add("remoteapplicationname", 's', profile.RemoteAppName);
            if (profile.RemoteAppCmdLine.Length > 0) Add("remoteapplicationcmdline", 's', profile.RemoteAppCmdLine);
        }

        // ---- User passthrough: appended last, app-emitted keys win ----
        AppendExtraSettings(sb, profile.ExtraSettings, emitted);

        return sb.ToString();
    }

    private static void AppendExtraSettings(StringBuilder sb, string extraSettings, HashSet<string> emitted)
    {
        if (string.IsNullOrWhiteSpace(extraSettings)) return;

        var slice = extraSettings.Length > MaxExtraSettingsLength
            ? extraSettings[..MaxExtraSettingsLength]
            : extraSettings;

        foreach (var rawLine in slice.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var match = LinePattern.Match(line);
            if (!match.Success) continue;

            var key = match.Groups["key"].Value.Trim();
            if (key.Length == 0 || emitted.Contains(key) || DroppedKeys.Contains(key)) continue;

            sb.Append(SanitizeValue(key)).Append(':')
              .Append(match.Groups["type"].Value).Append(':')
              .AppendLine(SanitizeValue(match.Groups["value"].Value));
            emitted.Add(key);
        }
    }

    /// <summary>
    /// Strips CR/LF and all other C0 control characters so no profile field can smuggle
    /// an extra .rdp line into the generated file (values come from JSON imports and
    /// free-text UI fields).
    /// </summary>
    internal static string SanitizeValue(string value)
    {
        if (value.All(c => c >= 0x20)) return value;
        return new string(value.Where(c => c >= 0x20).ToArray());
    }

    /// <summary>
    /// Normalizes an ExtraSettings blob at import time: keeps only well-formed
    /// <c>key:type:value</c> lines that aren't on the drop list, and caps the total
    /// size. Emission applies the same rules again (defense in depth).
    /// </summary>
    internal static string SanitizeExtraSettings(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var kept = new List<string>();
        var total = 0;
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var match = LinePattern.Match(line);
            if (!match.Success) continue;

            var key = match.Groups["key"].Value.Trim();
            if (key.Length == 0 || DroppedKeys.Contains(key)) continue;

            total += line.Length + 1;
            if (total > MaxExtraSettingsLength) break;
            kept.Add(line);
        }
        return string.Join('\n', kept);
    }
}
