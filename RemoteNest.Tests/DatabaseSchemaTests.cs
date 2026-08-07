using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using RemoteNest.Data;
using RemoteNest.Models;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>
/// The CRUD SQL is generated from <see cref="ConnectionProfile"/>'s property list, so the
/// table and the model must describe the same set of columns. These tests fail the moment
/// a property is added without its column (or the reverse).
/// </summary>
public class DatabaseSchemaTests
{
    private static (Database db, string path) NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"remotenest-schema-{Guid.NewGuid():N}.db");
        var db = new Database(path);
        db.EnsureCreated();
        return (db, path);
    }

    private static List<string> Columns(Database db)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """PRAGMA table_info("ConnectionProfiles")""";
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));
        return names;
    }

    [Fact]
    public void Schema_Columns_Match_The_Model_Exactly()
    {
        var (db, path) = NewDatabase();
        try
        {
            var modelProperties = ProfileReflection.SettableProperties.Select(p => p.Name).ToList();
            Columns(db).Should().BeEquivalentTo(modelProperties);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Fresh_Database_Creates_The_Lookup_Indexes()
    {
        var (db, path) = NewDatabase();
        try
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'ConnectionProfiles'""";
            using var reader = cmd.ExecuteReader();
            var indexes = new List<string>();
            while (reader.Read()) indexes.Add(reader.GetString(0));

            indexes.Should().Contain([
                "IX_ConnectionProfiles_Group",
                "IX_ConnectionProfiles_Host",
                "IX_ConnectionProfiles_Name"
            ]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureCreated_Is_Idempotent_And_Preserves_Rows()
    {
        var (db, path) = NewDatabase();
        try
        {
            var svc = new ConnectionService(db);
            svc.CreateAsync(new ConnectionProfile { Name = "Keep", Host = "h" }).GetAwaiter().GetResult();

            db.EnsureCreated();
            db.EnsureCreated();

            svc.GetAllAsync().GetAwaiter().GetResult()
                .Should().ContainSingle(p => p.Name == "Keep");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Column_Defaults_Match_The_New_Profile_Defaults()
    {
        var (db, path) = NewDatabase();
        try
        {
            // Insert using only the required columns: every other value comes from the
            // column DEFAULT, which must agree with the model's property initializers.
            using (var conn = db.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO "ConnectionProfiles" ("Name", "Group", "Host", "CreatedAt", "LastConnectedAt")
                    VALUES ('Defaults', '', 'h', '2026-01-01 00:00:00', '0001-01-01 00:00:00')
                    """;
                cmd.ExecuteNonQuery();
            }

            var fromDb = new ConnectionService(db).GetAllAsync().GetAwaiter().GetResult().Single();
            var fromModel = new ConnectionProfile();

            foreach (var prop in ProfileReflection.SettableProperties)
            {
                if (prop.Name is nameof(ConnectionProfile.Id)
                              or nameof(ConnectionProfile.Name)
                              or nameof(ConnectionProfile.Host)
                              or nameof(ConnectionProfile.CreatedAt)) continue;

                prop.GetValue(fromDb).Should().Be(prop.GetValue(fromModel),
                    $"the DEFAULT for {prop.Name} must match the model initializer");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
