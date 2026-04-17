using RemoteNest.Models;

namespace RemoteNest.Services;

public interface IConnectionService
{
    /// <summary>Returns all connection profiles ordered by group then name.</summary>
    Task<List<ConnectionProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single profile by ID, or null if not found.</summary>
    Task<ConnectionProfile?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Creates a new connection profile.</summary>
    Task<ConnectionProfile> CreateAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>Updates an existing connection profile. Returns false if not found.</summary>
    Task<bool> UpdateAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>Deletes a connection profile by ID.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Duplicates an existing profile with "(copy)" appended to the name.</summary>
    Task<ConnectionProfile> DuplicateAsync(int id, CancellationToken ct = default);

    /// <summary>Returns all distinct group names (case-insensitive).</summary>
    Task<List<string>> GetGroupsAsync(CancellationToken ct = default);

    /// <summary>Searches profiles by name or host (case-insensitive, unicode-aware).</summary>
    Task<List<ConnectionProfile>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>Records a connection event atomically (updates LastConnectedAt and ConnectionCount).</summary>
    Task RecordConnectionAsync(int id, CancellationToken ct = default);

    /// <summary>Exports all profiles to JSON (without passwords).</summary>
    Task<string> ExportToJsonAsync(CancellationToken ct = default);

    /// <summary>Imports profiles from a JSON string.</summary>
    Task<int> ImportFromJsonAsync(string json, CancellationToken ct = default);

    /// <summary>Parses an .rdp file (UTF-8 or UTF-16 BOM) and creates a ConnectionProfile from it.</summary>
    Task<ConnectionProfile> ImportFromRdpFileAsync(string rdpFilePath, CancellationToken ct = default);
}
