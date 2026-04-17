using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteNest.Data;
using RemoteNest.Localization;
using RemoteNest.Models;

namespace RemoteNest.Services;

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

    private readonly AppDbContextFactory _factory;
    private readonly ILogger<ConnectionService> _logger;

    public ConnectionService(AppDbContextFactory factory)
        : this(factory, NullLogger<ConnectionService>.Instance) { }

    public ConnectionService(AppDbContextFactory factory, ILogger<ConnectionService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<List<ConnectionProfile>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _factory.Create();
        return await db.ConnectionProfiles
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ConnectionProfile?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        return await db.ConnectionProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<ConnectionProfile> CreateAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        profile.CreatedAt = DateTime.UtcNow;
        db.ConnectionProfiles.Add(profile);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return profile;
    }

    public async Task<bool> UpdateAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var existing = await db.ConnectionProfiles.FindAsync(new object?[] { profile.Id }, ct)
            .ConfigureAwait(false);
        if (existing is null) return false;

        existing.Name = profile.Name;
        existing.Group = profile.Group;
        existing.Host = profile.Host;
        existing.Port = profile.Port;
        existing.Username = profile.Username;
        existing.EncryptedPassword = profile.EncryptedPassword;
        existing.Domain = profile.Domain;
        existing.ScreenWidth = profile.ScreenWidth;
        existing.ScreenHeight = profile.ScreenHeight;
        existing.FullScreen = profile.FullScreen;
        existing.ColorDepth = profile.ColorDepth;
        existing.RedirectClipboard = profile.RedirectClipboard;
        existing.RedirectDrives = profile.RedirectDrives;
        existing.RedirectPrinters = profile.RedirectPrinters;
        existing.RedirectAudio = profile.RedirectAudio;
        existing.UseNetworkLevelAuth = profile.UseNetworkLevelAuth;
        existing.AutoConnectOnStartup = profile.AutoConnectOnStartup;
        existing.Notes = profile.Notes;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        await db.ConnectionProfiles
            .Where(p => p.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ConnectionProfile> DuplicateAsync(int id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var original = await db.ConnectionProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(TranslationSource.Format("ProfileNotFound", id));

        var copy = new ConnectionProfile
        {
            Name = original.Name + " " + TranslationSource.Get("ProfileCopySuffix"),
            Group = original.Group,
            Host = original.Host,
            Port = original.Port,
            Username = original.Username,
            EncryptedPassword = original.EncryptedPassword,
            Domain = original.Domain,
            ScreenWidth = original.ScreenWidth,
            ScreenHeight = original.ScreenHeight,
            FullScreen = original.FullScreen,
            ColorDepth = original.ColorDepth,
            RedirectClipboard = original.RedirectClipboard,
            RedirectDrives = original.RedirectDrives,
            RedirectPrinters = original.RedirectPrinters,
            RedirectAudio = original.RedirectAudio,
            UseNetworkLevelAuth = original.UseNetworkLevelAuth,
            AutoConnectOnStartup = false, // never auto-connect a freshly duplicated profile
            Notes = original.Notes,
            CreatedAt = DateTime.UtcNow
        };

        db.ConnectionProfiles.Add(copy);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return copy;
    }

    public async Task<List<string>> GetGroupsAsync(CancellationToken ct = default)
    {
        using var db = _factory.Create();
        // Case-insensitive dedupe: pull then dedupe client-side so "Prod" and "prod" collapse.
        var raw = await db.ConnectionProfiles
            .Where(p => !string.IsNullOrEmpty(p.Group))
            .Select(p => p.Group)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return raw
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<ConnectionProfile>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync(ct).ConfigureAwait(false);

        using var db = _factory.Create();
        // EF.Functions.Like uses SQLite LIKE which is case-insensitive for ASCII only.
        // The 3-arg overload emits `LIKE pattern ESCAPE '\'`, making EscapeLike's
        // backslash-escaped %, _ and \ behave as literals. Without ESCAPE, SQLite treats
        // \ as literal, which made queries containing % or _ silently return zero results.
        const string escape = "\\";
        var pattern = $"%{EscapeLike(query)}%";
        var candidates = await db.ConnectionProfiles
            .Where(p => EF.Functions.Like(p.Name, pattern, escape)
                     || EF.Functions.Like(p.Host, pattern, escape))
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Second-pass filter for unicode-correctness (handles accented characters).
        return candidates
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || p.Host.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public async Task RecordConnectionAsync(int id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        // Atomic UPDATE — previously a load-modify-save race was losing counts
        // when the same profile was launched rapidly in succession.
        var now = DateTime.UtcNow;
        await db.ConnectionProfiles
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ConnectionCount, p => p.ConnectionCount + 1)
                .SetProperty(p => p.LastConnectedAt, now), ct)
            .ConfigureAwait(false);
    }

    public async Task<string> ExportToJsonAsync(CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var exportList = await db.ConnectionProfiles
            .AsNoTracking()
            .Select(p => new
            {
                p.Name,
                p.Group,
                p.Host,
                p.Port,
                p.Username,
                p.Domain,
                p.ScreenWidth,
                p.ScreenHeight,
                p.FullScreen,
                p.ColorDepth,
                p.RedirectClipboard,
                p.RedirectDrives,
                p.RedirectPrinters,
                p.RedirectAudio,
                p.UseNetworkLevelAuth,
                p.AutoConnectOnStartup,
                p.Notes
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(exportList, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<int> ImportFromJsonAsync(string json, CancellationToken ct = default)
    {
        List<ConnectionProfile>? profiles;
        try
        {
            profiles = JsonSerializer.Deserialize<List<ConnectionProfile>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(TranslationSource.Get("InvalidJsonFormat"), ex);
        }

        if (profiles is null || profiles.Count == 0)
            return 0;

        using var db = _factory.Create();
        int added = 0;
        foreach (var p in profiles)
        {
            // Null-coalesce each string to guard against JSON "Name": null breaking SQLite constraints.
            p.Name = p.Name ?? string.Empty;
            p.Group = p.Group ?? string.Empty;
            p.Host = p.Host ?? string.Empty;
            p.Username = p.Username ?? string.Empty;
            p.Domain = p.Domain ?? string.Empty;
            p.ColorDepth = p.ColorDepth ?? "32";
            p.Notes = p.Notes ?? string.Empty;

            if (string.IsNullOrWhiteSpace(p.Host) || string.IsNullOrWhiteSpace(p.Name))
                continue;

            p.Id = 0;
            p.EncryptedPassword = string.Empty;
            p.CreatedAt = DateTime.UtcNow;
            p.LastConnectedAt = default;
            p.ConnectionCount = 0;
            p.Port = p.Port is < 1 or > 65535 ? 3389 : p.Port;
            p.ScreenWidth = Math.Clamp(p.ScreenWidth <= 0 ? 1920 : p.ScreenWidth, 640, 7680);
            p.ScreenHeight = Math.Clamp(p.ScreenHeight <= 0 ? 1080 : p.ScreenHeight, 480, 4320);
            if (!ConnectionProfile.ValidColorDepths.Contains(p.ColorDepth))
                p.ColorDepth = "32";
            p.AutoConnectOnStartup = false; // never auto-connect on import

            db.ConnectionProfiles.Add(p);
            added++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Imported {Count} profile(s) from JSON", added);
        return added;
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
            Name = Path.GetFileNameWithoutExtension(rdpFilePath),
            CreatedAt = DateTime.UtcNow
        };

        foreach (var line in lines)
        {
            var match = RdpLinePattern.Match(line);
            if (!match.Success) continue;

            var key = match.Groups["key"].Value.Trim().ToLowerInvariant();
            var type = match.Groups["type"].Value;
            var value = match.Groups["value"].Value.Trim();

            // Only process string ('s') and int ('i') types; skip binary ('b').
            if (type == "b") continue;

            switch (key)
            {
                case "full address":
                    ParseFullAddress(value, profile);
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
                    // Standalone `domain:s:` line — mstsc writes this when the user
                    // specifies a domain separately from username. Don't overwrite a
                    // domain already parsed from "DOMAIN\user".
                    if (string.IsNullOrEmpty(profile.Domain))
                        profile.Domain = value;
                    break;
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
                case "redirectclipboard":
                    profile.RedirectClipboard = value == "1";
                    break;
                case "redirectdrives":
                    profile.RedirectDrives = value == "1";
                    break;
                case "redirectprinters":
                    profile.RedirectPrinters = value == "1";
                    break;
                case "audiomode":
                    profile.RedirectAudio = value == "0";
                    break;
                case "enablecredsspsupport":
                    profile.UseNetworkLevelAuth = value == "1";
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new InvalidOperationException(TranslationSource.Get("RdpNoHost"));

        using var db = _factory.Create();
        db.ConnectionProfiles.Add(profile);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Imported profile '{Name}' from .rdp file", profile.Name);
        return profile;
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

    internal static string EscapeLike(string query)
    {
        // Escape SQLite LIKE special characters: %, _, \ (\ is the default ESCAPE char in EF core LIKE)
        return query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
