using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using RemoteNest.Data;
using RemoteNest.Localization;
using RemoteNest.Models;
using RemoteNest.Serialization;

namespace RemoteNest.Services;

/// <summary>
/// Profile CRUD + import/export over Microsoft.Data.Sqlite.
/// Connections are opened per operation; pooling (on by default) makes that cheap.
/// </summary>
public class ConnectionService : IConnectionService
{
    // Parses .rdp "full address" values. Handles IPv6 in brackets (optionally with :port),
    // bare hostnames/IPv4 (optionally with :port).
    //   [::1]          -> host=[::1], port=default
    //   [::1]:3389     -> host=[::1], port=3389
    //   host.ex.com    -> host=host.ex.com
    //   10.0.0.1:3390  -> host=10.0.0.1, port=3390
    private static readonly Regex FullAddressPattern = new(
        @"^(?<host>\[[0-9a-fA-F:]+\]|[^:\s]+)(:(?<port>\d+))?$",
        RegexOptions.Compiled);

    // Matches .rdp lines: key:type:value  (type is s/i/b)
    private static readonly Regex RdpLinePattern = new(
        @"^(?<key>.+?):(?<type>[sib]):(?<value>.*)$",
        RegexOptions.Compiled);

    // SQL text is generated from ConnectionProfile's public read/write properties
    // (column name == property name), so model, INSERT, UPDATE, and reads can never
    // drift apart. Id is handled separately (autoincrement PK).
    private static readonly PropertyInfo[] DataProperties = typeof(ConnectionProfile)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(prop => prop.CanRead && prop.CanWrite && prop.Name != nameof(ConnectionProfile.Id))
        .ToArray();

    private static readonly string AllColumns =
        "\"Id\", " + string.Join(", ", DataProperties.Select(prop => $"\"{prop.Name}\""));

    private static readonly string InsertSql =
        $"""
         INSERT INTO "ConnectionProfiles" ({string.Join(", ", DataProperties.Select(prop => $"\"{prop.Name}\""))})
         VALUES ({string.Join(", ", DataProperties.Select(prop => "$" + prop.Name))});
         SELECT last_insert_rowid();
         """;

    private static readonly string UpdateSql =
        $"""
         UPDATE "ConnectionProfiles"
         SET {string.Join(", ", DataProperties.Select(prop => $"\"{prop.Name}\" = ${prop.Name}"))}
         WHERE "Id" = $Id
         """;

    private readonly Database _db;

    public ConnectionService(Database db)
    {
        _db = db;
    }

    public Task<List<ConnectionProfile>> GetAllAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""SELECT {AllColumns} FROM "ConnectionProfiles" ORDER BY "Group", "Name" """;
            using var reader = cmd.ExecuteReader();
            return ReadProfiles(reader);
        }, ct);

    public Task<ConnectionProfile?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""SELECT {AllColumns} FROM "ConnectionProfiles" WHERE "Id" = $id""";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return ReadProfiles(reader).FirstOrDefault();
        }, ct);

    public Task<ConnectionProfile> CreateAsync(ConnectionProfile profile, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            profile.CreatedAt = DateTime.UtcNow;
            using var conn = _db.Open();
            profile.Id = Insert(conn, profile);
            return profile;
        }, ct);

    public Task<bool> UpdateAsync(ConnectionProfile profile, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = UpdateSql;
            cmd.Parameters.AddWithValue("$Id", profile.Id);
            AddProfileParameters(cmd, profile);
            return cmd.ExecuteNonQuery() > 0;
        }, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """DELETE FROM "ConnectionProfiles" WHERE "Id" = $id""";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }, ct);

    public async Task<ConnectionProfile> DuplicateAsync(int id, CancellationToken ct = default)
    {
        var original = await GetByIdAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(TranslationSource.Format("ProfileNotFound", id));

        var copy = original.CloneForDuplicate(TranslationSource.Get("ProfileCopySuffix"));
        return await CreateAsync(copy, ct).ConfigureAwait(false);
    }

    public Task<List<string>> GetGroupsAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT "Group" FROM "ConnectionProfiles" WHERE "Group" <> ''""";
            using var reader = cmd.ExecuteReader();
            var raw = new List<string>();
            while (reader.Read())
                raw.Add(reader.GetString(0));

            // Case-insensitive dedupe so "Prod" and "prod" collapse.
            return raw
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);

    public Task RecordConnectionAsync(int id, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            // Atomic UPDATE — rapid successive launches of the same profile must not lose counts.
            cmd.CommandText = """
                UPDATE "ConnectionProfiles"
                SET "ConnectionCount" = "ConnectionCount" + 1, "LastConnectedAt" = $now
                WHERE "Id" = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }, ct);

    // DTO<->model mapping is reflection-driven over matching property names, so new
    // fields are covered automatically (a parity test enforces DTO coverage).
    private static readonly PropertyInfo[] ExportProperties = typeof(ConnectionProfileExport)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(prop => prop.CanRead && prop.CanWrite)
        .ToArray();

    public async Task<string> ExportToJsonAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        var exportList = all.Select(ToExportDto).ToList();
        return JsonSerializer.Serialize(exportList, AppJsonContext.Default.ListConnectionProfileExport);
    }

    private static ConnectionProfileExport ToExportDto(ConnectionProfile profile)
    {
        var dto = new ConnectionProfileExport();
        foreach (var dtoProp in ExportProperties)
        {
            var modelProp = typeof(ConnectionProfile).GetProperty(dtoProp.Name);
            if (modelProp is null) continue;
            dtoProp.SetValue(dto, modelProp.GetValue(profile));
        }
        return dto;
    }

    /// <summary>Maps an imported DTO onto a fresh profile.</summary>
    private static ConnectionProfile FromExportDto(ConnectionProfileExport dto)
    {
        var profile = new ConnectionProfile();
        foreach (var dtoProp in ExportProperties)
        {
            var modelProp = typeof(ConnectionProfile).GetProperty(dtoProp.Name);
            if (modelProp is null || !modelProp.CanWrite) continue;
            modelProp.SetValue(profile, dtoProp.GetValue(dto));
        }
        return profile;
    }

    public Task<int> ImportFromJsonAsync(string json, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            List<ConnectionProfileExport>? dtos;
            try
            {
                dtos = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListConnectionProfileExport);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(TranslationSource.Get("InvalidJsonFormat"), ex);
            }

            if (dtos is null || dtos.Count == 0)
                return 0;

            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            int added = 0;
            foreach (var dto in dtos)
            {
                var p = FromExportDto(dto);

                if (string.IsNullOrWhiteSpace(p.Host) || string.IsNullOrWhiteSpace(p.Name))
                    continue;

                ClampImportedValues(p);
                p.EncryptedPassword = string.Empty;
                p.CreatedAt = DateTime.UtcNow;
                p.LastConnectedAt = default;
                p.ConnectionCount = 0;
                p.AutoConnectOnStartup = false; // never auto-connect on import

                p.Id = Insert(conn, p, tx);
                added++;
            }

            tx.Commit();
            Log.Info($"Imported {added} profile(s) from JSON");
            return added;
        }, ct);

    /// <summary>Clamps every bounded field of an untrusted (imported) profile into its valid range.</summary>
    internal static void ClampImportedValues(ConnectionProfile p)
    {
        p.Port = p.Port is < 1 or > 65535 ? 3389 : p.Port;
        p.ScreenWidth = Math.Clamp(p.ScreenWidth <= 0 ? 1920 : p.ScreenWidth, 640, 7680);
        p.ScreenHeight = Math.Clamp(p.ScreenHeight <= 0 ? 1080 : p.ScreenHeight, 480, 4320);
        if (string.IsNullOrEmpty(p.ColorDepth) || !ConnectionProfile.ValidColorDepths.Contains(p.ColorDepth))
            p.ColorDepth = "32";

        p.AudioPlaybackMode = Math.Clamp(p.AudioPlaybackMode, 0, 2);
        p.AudioQualityMode = Math.Clamp(p.AudioQualityMode, 0, 2);
        p.KeyboardHook = Math.Clamp(p.KeyboardHook, 0, 2);
        p.AuthenticationLevel = Math.Clamp(p.AuthenticationLevel, 0, 3);
        p.ConnectionType = Math.Clamp(p.ConnectionType, 1, 7);
        p.AutoReconnectMaxRetries = Math.Clamp(p.AutoReconnectMaxRetries, 0, 200);
        if (!ConnectionProfile.ValidScaleFactors.Contains(p.DesktopScaleFactor))
            p.DesktopScaleFactor = 0;
        if (p.GatewayUsageMethod is not (1 or 2))
            p.GatewayUsageMethod = 2;
        if (p.GatewayCredentialsSource is not (0 or 1 or 4))
            p.GatewayCredentialsSource = 0;

        // Mutually exclusive launch modes: an inconsistent import clears both.
        if (p is { RestrictedAdmin: true, RemoteGuard: true })
        {
            p.RestrictedAdmin = false;
            p.RemoteGuard = false;
        }

        p.ExtraSettings = RdpFileBuilder.SanitizeExtraSettings(p.ExtraSettings);
    }

    public async Task<ConnectionProfile> ImportFromRdpFileAsync(string rdpFilePath, CancellationToken ct = default)
    {
        string[] lines;
        try
        {
            // Windows-generated .rdp files are UTF-16 LE with BOM; detectEncodingFromByteOrderMarks
            // handles both UTF-16 and UTF-8-with-BOM. Default encoding used only for BOM-less ASCII.
            using var reader = new StreamReader(rdpFilePath, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var all = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(TranslationSource.Format("FailedReadRdp", ex.Message), ex);
        }

        var profile = new ConnectionProfile
        {
            Name = Path.GetFileNameWithoutExtension(rdpFilePath)
        };

        var extraLines = new List<string>();
        string? drivesToRedirectValue = null;
        var gatewayUsageSeen = -1;

        foreach (var line in lines)
        {
            var match = RdpLinePattern.Match(line.Trim());
            if (!match.Success) continue;

            var key = match.Groups["key"].Value.Trim().ToLowerInvariant();
            var type = match.Groups["type"].Value;
            var value = match.Groups["value"].Value.Trim();

            // Binary payloads (signatures, legacy password blobs) are never imported.
            if (type == "b") continue;

            switch (key)
            {
                case "full address":
                    ParseFullAddress(value, profile);
                    break;
                case "alternate full address":
                    if (string.IsNullOrEmpty(profile.Host))
                        ParseFullAddress(value, profile);
                    else
                        extraLines.Add(line.Trim());
                    break;
                case "username":
                    if (value.Contains('\\'))
                    {
                        var domUser = value.Split('\\', 2);
                        profile.Domain = domUser[0];
                        profile.Username = domUser[1];
                    }
                    else
                    {
                        profile.Username = value;
                    }
                    break;
                case "domain":
                    // Standalone `domain:s:` line - mstsc writes this when the user
                    // specifies a domain separately from username. Don't overwrite a
                    // domain already parsed from "DOMAIN\user".
                    if (string.IsNullOrEmpty(profile.Domain))
                        profile.Domain = value;
                    break;

                // ---- display ----
                case "screen mode id":
                    profile.FullScreen = value == "2";
                    break;
                case "desktopwidth":
                    if (int.TryParse(value, out var w)) profile.ScreenWidth = w;
                    break;
                case "desktopheight":
                    if (int.TryParse(value, out var h)) profile.ScreenHeight = h;
                    break;
                case "session bpp":
                    if (ConnectionProfile.ValidColorDepths.Contains(value))
                        profile.ColorDepth = value;
                    break;
                case "use multimon":
                    profile.UseMultimon = value == "1";
                    break;
                case "selectedmonitors":
                    profile.SelectedMonitors = value;
                    break;
                case "dynamic resolution":
                    profile.DynamicResolution = value == "1";
                    break;
                case "smart sizing":
                    profile.SmartSizing = value == "1";
                    break;
                case "desktopscalefactor":
                    if (int.TryParse(value, out var scale)) profile.DesktopScaleFactor = scale;
                    break;

                // ---- redirection ----
                case "redirectclipboard":
                    profile.RedirectClipboard = value == "1";
                    break;
                case "redirectdrives":
                    profile.RedirectDrives = value == "1";
                    break;
                case "drivestoredirect":
                    drivesToRedirectValue = value;
                    break;
                case "redirectprinters":
                    profile.RedirectPrinters = value == "1";
                    break;
                case "audiomode":
                    if (int.TryParse(value, out var audio)) profile.AudioPlaybackMode = audio;
                    break;
                case "audiocapturemode":
                    profile.AudioCaptureMode = value == "1";
                    break;
                case "audioqualitymode":
                    if (int.TryParse(value, out var quality)) profile.AudioQualityMode = quality;
                    break;
                case "camerastoredirect":
                    profile.CamerasToRedirect = value;
                    break;
                case "devicestoredirect":
                    profile.PnpDevicesToRedirect = value;
                    break;
                case "usbdevicestoredirect":
                    profile.UsbDevicesToRedirect = value;
                    break;
                case "redirectcomports":
                    profile.RedirectComPorts = value == "1";
                    break;
                case "redirectsmartcards":
                    profile.RedirectSmartCards = value == "1";
                    break;
                case "redirectwebauthn":
                    profile.RedirectWebAuthn = value == "1";
                    break;
                case "redirectlocation":
                    profile.RedirectLocation = value == "1";
                    break;
                case "keyboardhook":
                    if (int.TryParse(value, out var hook)) profile.KeyboardHook = hook;
                    break;

                // ---- gateway / network ----
                case "gatewayhostname":
                    profile.GatewayHostname = value;
                    break;
                case "gatewayusagemethod":
                    if (int.TryParse(value, out var usage)) gatewayUsageSeen = usage;
                    break;
                case "gatewaycredentialssource":
                    if (int.TryParse(value, out var credSource)) profile.GatewayCredentialsSource = credSource;
                    break;
                case "promptcredentialonce":
                    profile.GatewayPromptCredentialOnce = value == "1";
                    break;
                case "gatewayprofileusagemethod":
                    // Consumed: the app always emits 1 (explicit settings) when a gateway is set.
                    break;
                case "loadbalanceinfo":
                    profile.LoadBalanceInfo = value;
                    break;
                case "pcb":
                    profile.Pcb = value;
                    break;
                case "kdcproxyname":
                    profile.KdcProxyName = value;
                    break;
                case "enablerdsaadauth":
                    profile.EnableRdsAadAuth = value == "1";
                    break;
                case "connection type":
                    if (int.TryParse(value, out var connType)) profile.ConnectionType = connType;
                    break;
                case "networkautodetect":
                    profile.NetworkAutoDetect = value == "1";
                    break;
                case "bandwidthautodetect":
                    profile.BandwidthAutoDetect = value == "1";
                    break;
                case "compression":
                    profile.Compression = value == "1";
                    break;
                case "bitmapcachepersistenable":
                    profile.BitmapCachePersist = value == "1";
                    break;

                // ---- experience ----
                case "disable wallpaper":
                    profile.DisableWallpaper = value == "1";
                    break;
                case "disable full window drag":
                    profile.DisableFullWindowDrag = value == "1";
                    break;
                case "disable menu anims":
                    profile.DisableMenuAnims = value == "1";
                    break;
                case "disable themes":
                    profile.DisableThemes = value == "1";
                    break;
                case "allow font smoothing":
                    profile.AllowFontSmoothing = value == "1";
                    break;
                case "allow desktop composition":
                    profile.AllowDesktopComposition = value == "1";
                    break;
                case "videoplaybackmode":
                    profile.VideoPlaybackMode = value == "1";
                    break;

                // ---- session ----
                case "administrative session":
                    profile.AdministrativeSession = value == "1";
                    break;
                case "autoreconnection enabled":
                    profile.AutoReconnect = value == "1";
                    break;
                case "autoreconnect max retries":
                    if (int.TryParse(value, out var retries)) profile.AutoReconnectMaxRetries = retries;
                    break;
                case "displayconnectionbar":
                    profile.DisplayConnectionBar = value == "1";
                    break;
                case "pinconnectionbar":
                    profile.PinConnectionBar = value == "1";
                    break;
                case "public mode":
                    profile.PublicMode = value == "1";
                    break;

                // ---- security ----
                case "authentication level":
                    if (int.TryParse(value, out var authLevel)) profile.AuthenticationLevel = authLevel;
                    break;
                case "enablecredsspsupport":
                    profile.UseNetworkLevelAuth = value == "1";
                    break;

                // ---- RemoteApp ----
                case "remoteapplicationmode":
                    profile.RemoteAppMode = value == "1";
                    break;
                case "remoteapplicationprogram":
                    profile.RemoteAppProgram = value;
                    break;
                case "remoteapplicationname":
                    profile.RemoteAppName = value;
                    break;
                case "remoteapplicationcmdline":
                    profile.RemoteAppCmdLine = value;
                    break;

                // ---- app-managed lines are consumed, not round-tripped ----
                case "prompt for credentials":
                case "prompt for credentials on client":
                    break;

                default:
                    // Unknown key: preserved verbatim so nothing is lost on re-export.
                    extraLines.Add(line.Trim());
                    break;
            }
        }

        // Order-independent reconciliation of the two drive-redirection keys:
        // a non-empty modern list implies redirection is on ("*" means "all" = empty list).
        if (!string.IsNullOrEmpty(drivesToRedirectValue))
        {
            profile.RedirectDrives = true;
            profile.DrivesToRedirect = drivesToRedirectValue == "*" ? string.Empty : drivesToRedirectValue;
        }

        // gatewayusagemethod 0 means "don't use a gateway" - mstsc keeps the hostname
        // around but disabled; our model represents "off" as an empty hostname.
        if (gatewayUsageSeen == 0)
            profile.GatewayHostname = string.Empty;
        else if (gatewayUsageSeen > 0)
            profile.GatewayUsageMethod = gatewayUsageSeen;

        profile.ExtraSettings = RdpFileBuilder.SanitizeExtraSettings(string.Join('\n', extraLines));

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new InvalidOperationException(TranslationSource.Get("RdpNoHost"));

        ClampImportedValues(profile);
        var saved = await CreateAsync(profile, ct).ConfigureAwait(false);
        Log.Info($"Imported profile '{saved.Name}' from .rdp file");
        return saved;
    }


    internal static void ParseFullAddress(string value, ConnectionProfile profile)
    {
        var m = FullAddressPattern.Match(value);
        if (!m.Success)
        {
            // Fallback: treat entire value as host, default port.
            profile.Host = value;
            return;
        }

        profile.Host = m.Groups["host"].Value;
        if (m.Groups["port"].Success && int.TryParse(m.Groups["port"].Value, out var port)
            && port is >= 1 and <= 65535)
        {
            profile.Port = port;
        }
    }

    private static int Insert(SqliteConnection conn, ConnectionProfile profile, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertSql;
        AddProfileParameters(cmd, profile);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void AddProfileParameters(SqliteCommand cmd, ConnectionProfile profile)
    {
        foreach (var prop in DataProperties)
            cmd.Parameters.AddWithValue("$" + prop.Name, prop.GetValue(profile) ?? DBNull.Value);
    }

    private static List<ConnectionProfile> ReadProfiles(SqliteDataReader reader)
    {
        // Ordinals resolved once per reader; values mapped back through the same
        // property list that generated the SQL.
        var idOrdinal = reader.GetOrdinal("Id");
        var ordinals = new int[DataProperties.Length];
        for (var i = 0; i < DataProperties.Length; i++)
            ordinals[i] = reader.GetOrdinal(DataProperties[i].Name);

        var list = new List<ConnectionProfile>();
        while (reader.Read())
        {
            var profile = new ConnectionProfile { Id = reader.GetInt32(idOrdinal) };
            for (var i = 0; i < DataProperties.Length; i++)
            {
                var prop = DataProperties[i];
                var ord = ordinals[i];
                object value;
                if (prop.PropertyType == typeof(bool))
                    value = reader.GetBoolean(ord);
                else if (prop.PropertyType == typeof(int))
                    value = reader.GetInt32(ord);
                else if (prop.PropertyType == typeof(DateTime))
                    // Timestamps are written as DateTime.UtcNow; SQLite TEXT loses the
                    // Kind, so restore it — DateTimeToStringConverter relies on Kind=Utc.
                    value = DateTime.SpecifyKind(reader.GetDateTime(ord), DateTimeKind.Utc);
                else
                    value = reader.GetString(ord);
                prop.SetValue(profile, value);
            }
            list.Add(profile);
        }
        return list;
    }
}
