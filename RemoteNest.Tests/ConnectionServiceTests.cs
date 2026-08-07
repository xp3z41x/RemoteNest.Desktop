using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using RemoteNest.Data;
using RemoteNest.Models;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>
/// Integration tests for <see cref="ConnectionService"/> over a real on-disk SQLite file —
/// exactly the production storage path, including schema creation and pooling.
/// </summary>
public class ConnectionServiceTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private Database _db = null!;

    public Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"remotenest-test-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _db.EnsureCreated();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // Pooled connections keep the file locked until the pool is cleared.
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    private ConnectionService NewService() => new(_db);

    [Fact]
    public async Task Create_Then_GetById_Returns_Same_Profile()
    {
        var svc = NewService();

        var saved = await svc.CreateAsync(new ConnectionProfile
        {
            Name = "Prod", Host = "prod.example.com", Port = 3389, Username = "admin"
        });
        saved.Id.Should().BeGreaterThan(0);

        var loaded = await svc.GetByIdAsync(saved.Id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Prod");
        loaded.Host.Should().Be("prod.example.com");
    }

    [Fact]
    public async Task Update_Returns_True_And_Persists_Changes()
    {
        var svc = NewService();

        var saved = await svc.CreateAsync(new ConnectionProfile { Name = "A", Host = "a.example.com" });
        saved.Name = "A2"; saved.Host = "a2.example.com";

        var ok = await svc.UpdateAsync(saved);
        ok.Should().BeTrue();

        var loaded = await svc.GetByIdAsync(saved.Id);
        loaded!.Name.Should().Be("A2");
        loaded.Host.Should().Be("a2.example.com");
    }

    [Fact]
    public async Task Update_NonExistent_Returns_False()
    {
        var svc = NewService();

        var ok = await svc.UpdateAsync(new ConnectionProfile { Id = 99999, Name = "X", Host = "x" });
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Removes_Profile()
    {
        var svc = NewService();

        var saved = await svc.CreateAsync(new ConnectionProfile { Name = "ToDelete", Host = "x" });
        await svc.DeleteAsync(saved.Id);

        (await svc.GetByIdAsync(saved.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_Creates_Copy_With_Suffix_And_Resets_AutoConnect()
    {
        var svc = NewService();
        var saved = await svc.CreateAsync(new ConnectionProfile
        {
            Name = "Source", Host = "src.example.com", AutoConnectOnStartup = true
        });

        var copy = await svc.DuplicateAsync(saved.Id);

        copy.Id.Should().NotBe(saved.Id);
        copy.Name.Should().StartWith("Source ");
        copy.AutoConnectOnStartup.Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_Copies_Every_Setting_Reflectively()
    {
        var svc = NewService();
        var original = ProfileReflection.CreateFullyPopulated(seed: 7);
        original.AutoConnectOnStartup = true;
        var saved = await svc.CreateAsync(original);

        var copy = await svc.DuplicateAsync(saved.Id);
        var loadedCopy = await svc.GetByIdAsync(copy.Id);

        // Everything carried over except identity/statistics fields.
        ProfileReflection.AssertAllPropertiesEqual(saved, loadedCopy!,
            ignoreCreatedAt: true,
            ignore: [nameof(ConnectionProfile.Id), nameof(ConnectionProfile.Name),
                     nameof(ConnectionProfile.AutoConnectOnStartup),
                     nameof(ConnectionProfile.LastConnectedAt),
                     nameof(ConnectionProfile.ConnectionCount)]);
        loadedCopy!.AutoConnectOnStartup.Should().BeFalse();
    }

    [Fact]
    public async Task RecordConnectionAsync_Increments_Count_Atomically()
    {
        var svc = NewService();
        var saved = await svc.CreateAsync(new ConnectionProfile { Name = "N", Host = "h" });

        // Serial increments (parallel would require SQLite journal tuning).
        for (int i = 0; i < 10; i++)
            await svc.RecordConnectionAsync(saved.Id);

        var loaded = await svc.GetByIdAsync(saved.Id);
        loaded!.ConnectionCount.Should().Be(10);
        loaded.LastConnectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Roundtrip_Preserves_Every_Property_Reflectively()
    {
        // Set every public settable property to a non-default value, save, reload,
        // compare all — catches any drift between the model and the generated SQL.
        var svc = NewService();
        var profile = ProfileReflection.CreateFullyPopulated(seed: 1);

        var saved = await svc.CreateAsync(profile);
        var loaded = await svc.GetByIdAsync(saved.Id);
        loaded.Should().NotBeNull();
        ProfileReflection.AssertAllPropertiesEqual(saved, loaded!);
        loaded!.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);

        // Same guarantee for UPDATE.
        var updated = ProfileReflection.CreateFullyPopulated(seed: 2);
        updated.Id = saved.Id;
        (await svc.UpdateAsync(updated)).Should().BeTrue();
        var reloaded = await svc.GetByIdAsync(saved.Id);
        ProfileReflection.AssertAllPropertiesEqual(updated, reloaded!, ignoreCreatedAt: true);
    }

    [Fact]
    public async Task ImportFromJson_Then_Export_Roundtrips_Without_Passwords()
    {
        var svc = NewService();
        const string json = """
            [{ "name": "Json1", "host": "j1.example.com", "port": 3391, "colorDepth": "16" },
             { "name": "",      "host": "skipped.example.com" }]
            """;

        var added = await svc.ImportFromJsonAsync(json);
        added.Should().Be(1);

        var exported = await svc.ExportToJsonAsync();
        exported.Should().Contain("Json1").And.NotContain("EncryptedPassword");
    }

    [Fact]
    public async Task ImportFromRdpFileAsync_Handles_Utf16_LE_BOM()
    {
        var svc = NewService();

        // mstsc saves UTF-16 LE BOM by default.
        var content = new StringBuilder()
            .AppendLine("full address:s:utf16host.example.com:3390")
            .AppendLine("username:s:utf16-user")
            .AppendLine("domain:s:CORP")
            .AppendLine("screen mode id:i:2")
            .AppendLine("desktopwidth:i:1920")
            .AppendLine("desktopheight:i:1080")
            .AppendLine("session bpp:i:32")
            .AppendLine("redirectclipboard:i:1")
            .AppendLine("audiomode:i:0")
            .AppendLine("enablecredsspsupport:i:1")
            .ToString();

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content, Encoding.Unicode); // UTF-16 LE with BOM

            var profile = await svc.ImportFromRdpFileAsync(path);

            profile.Host.Should().Be("utf16host.example.com");
            profile.Port.Should().Be(3390);
            profile.Username.Should().Be("utf16-user");
            profile.Domain.Should().Be("CORP");
            profile.ScreenWidth.Should().Be(1920);
            profile.ScreenHeight.Should().Be(1080);
            profile.ColorDepth.Should().Be("32");
            profile.FullScreen.Should().BeTrue();
            profile.RedirectClipboard.Should().BeTrue();
            profile.AudioPlaybackMode.Should().Be(0);
            profile.UseNetworkLevelAuth.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
