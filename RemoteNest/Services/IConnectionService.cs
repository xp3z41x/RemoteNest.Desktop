using RemoteNest.Models;

namespace RemoteNest.Services;

public interface IConnectionService
{
    /// <summary>Returns all connection profiles ordered by group then name.</summary>
    Task<List<ConnectionProfile>> GetAllAsync();

    /// <summary>Returns a single profile by ID, or null if not found.</summary>
    Task<ConnectionProfile?> GetByIdAsync(int id);

    /// <summary>Creates a new connection profile.</summary>
    Task<ConnectionProfile> CreateAsync(ConnectionProfile profile);

    /// <summary>Updates an existing connection profile. Returns false if not found.</summary>
    Task<bool> UpdateAsync(ConnectionProfile profile);

    /// <summary>Deletes a connection profile by ID.</summary>
    Task DeleteAsync(int id);

    /// <summary>Duplicates an existing profile with "(copy)" appended to the name.</summary>
    Task<ConnectionProfile> DuplicateAsync(int id);

    /// <summary>Returns all distinct group names.</summary>
    Task<List<string>> GetGroupsAsync();

    /// <summary>Searches profiles by name or host.</summary>
    Task<List<ConnectionProfile>> SearchAsync(string query);

    /// <summary>Records a connection event (updates LastConnectedAt and ConnectionCount).</summary>
    Task RecordConnectionAsync(int id);

    /// <summary>Exports all profiles to JSON (without passwords).</summary>
    Task<string> ExportToJsonAsync();

    /// <summary>Imports profiles from a JSON string.</summary>
    Task<int> ImportFromJsonAsync(string json);

    /// <summary>Parses an .rdp file and creates a ConnectionProfile from it.</summary>
    Task<ConnectionProfile> ImportFromRdpFileAsync(string rdpFilePath);
}
