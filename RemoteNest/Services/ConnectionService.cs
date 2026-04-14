using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RemoteNest.Data;
using RemoteNest.Localization;
using RemoteNest.Models;

namespace RemoteNest.Services;

public class ConnectionService : IConnectionService
{
    private readonly AppDbContextFactory _factory;

    public ConnectionService(AppDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<ConnectionProfile>> GetAllAsync()
    {
        using var db = _factory.Create();
        return await db.ConnectionProfiles
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<ConnectionProfile?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        return await db.ConnectionProfiles.FindAsync(id);
    }

    public async Task<ConnectionProfile> CreateAsync(ConnectionProfile profile)
    {
        using var db = _factory.Create();
        profile.CreatedAt = DateTime.UtcNow;
        db.ConnectionProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<bool> UpdateAsync(ConnectionProfile profile)
    {
        using var db = _factory.Create();
        var existing = await db.ConnectionProfiles.FindAsync(profile.Id);
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

        await db.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAsync(int id)
    {
        using var db = _factory.Create();
        var profile = await db.ConnectionProfiles.FindAsync(id);
        if (profile is not null)
        {
            db.ConnectionProfiles.Remove(profile);
            await db.SaveChangesAsync();
        }
    }

    public async Task<ConnectionProfile> DuplicateAsync(int id)
    {
        using var db = _factory.Create();
        var original = await db.ConnectionProfiles.FindAsync(id)
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
            AutoConnectOnStartup = original.AutoConnectOnStartup,
            Notes = original.Notes,
            CreatedAt = DateTime.UtcNow
        };

        db.ConnectionProfiles.Add(copy);
        await db.SaveChangesAsync();
        return copy;
    }

    public async Task<List<string>> GetGroupsAsync()
    {
        using var db = _factory.Create();
        return await db.ConnectionProfiles
            .Where(p => !string.IsNullOrEmpty(p.Group))
            .Select(p => p.Group)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    public async Task<List<ConnectionProfile>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync();

        using var db = _factory.Create();
        var lower = query.ToLowerInvariant();
        return await db.ConnectionProfiles
            .Where(p => p.Name.ToLower().Contains(lower) || p.Host.ToLower().Contains(lower))
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task RecordConnectionAsync(int id)
    {
        using var db = _factory.Create();
        var profile = await db.ConnectionProfiles.FindAsync(id);
        if (profile is not null)
        {
            profile.LastConnectedAt = DateTime.UtcNow;
            profile.ConnectionCount++;
            await db.SaveChangesAsync();
        }
    }

    public async Task<string> ExportToJsonAsync()
    {
        using var db = _factory.Create();
        var exportList = await db.ConnectionProfiles
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
            .ToListAsync();

        return JsonSerializer.Serialize(exportList, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<int> ImportFromJsonAsync(string json)
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
            if (string.IsNullOrWhiteSpace(p.Host))
                continue;

            p.Id = 0;
            p.EncryptedPassword = string.Empty;
            p.CreatedAt = DateTime.UtcNow;
            p.LastConnectedAt = default;
            p.ConnectionCount = 0;
            p.Port = p.Port is < 1 or > 65535 ? 3389 : p.Port;
            p.ScreenWidth = Math.Clamp(p.ScreenWidth, 640, 7680);
            p.ScreenHeight = Math.Clamp(p.ScreenHeight, 480, 4320);
            if (!ConnectionProfile.ValidColorDepths.Contains(p.ColorDepth))
                p.ColorDepth = "32";

            db.ConnectionProfiles.Add(p);
            added++;
        }

        await db.SaveChangesAsync();
        return added;
    }

    public async Task<ConnectionProfile> ImportFromRdpFileAsync(string rdpFilePath)
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(rdpFilePath);
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
            var match = Regex.Match(line, @"^(.+?):([si]):(.*)$");
            if (!match.Success) continue;

            var key = match.Groups[1].Value.Trim().ToLowerInvariant();
            var value = match.Groups[3].Value.Trim();

            switch (key)
            {
                case "full address":
                    var parts = value.Split(':');
                    profile.Host = parts[0];
                    if (parts.Length > 1 && int.TryParse(parts[1], out var port))
                        profile.Port = port;
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
        await db.SaveChangesAsync();
        return profile;
    }
}
