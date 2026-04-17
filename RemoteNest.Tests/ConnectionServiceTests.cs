using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RemoteNest.Data;
using RemoteNest.Models;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>
/// Integration tests for <see cref="ConnectionService"/> backed by SQLite in-memory.
/// Uses the real EF Core SQLite provider (not InMemory) so LIKE + unicode collation
/// behavior matches production.
/// </summary>
public class ConnectionServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContextFactory _factory = null!;

    public async Task InitializeAsync()
    {
        // SQLite in-memory db survives only while at least one connection is open.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        _factory = new AppDbContextFactory(options);
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    private ConnectionService NewService() => new(_factory);

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
    public async Task Search_Is_Case_Insensitive()
    {
        var svc = NewService();
        await svc.CreateAsync(new ConnectionProfile { Name = "Production", Host = "prod.example.com" });
        await svc.CreateAsync(new ConnectionProfile { Name = "staging",    Host = "stage.example.com" });

        (await svc.SearchAsync("PROD"))   .Should().HaveCount(1);
        (await svc.SearchAsync("staging")).Should().HaveCount(1);
        (await svc.SearchAsync("STAGING")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Search_Escapes_Like_Wildcards()
    {
        var svc = NewService();
        await svc.CreateAsync(new ConnectionProfile { Name = "50%Match", Host = "h1" });
        await svc.CreateAsync(new ConnectionProfile { Name = "NoPct",    Host = "h2" });

        // Without LIKE escaping, "%" would match everything. We want literal match only.
        var results = await svc.SearchAsync("50%");
        results.Should().ContainSingle(p => p.Name == "50%Match");
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
            profile.RedirectAudio.Should().BeTrue();
            profile.UseNetworkLevelAuth.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportFromRdpFileAsync_Handles_Utf8()
    {
        var svc = NewService();
        var content = "full address:s:utf8host.example.com\r\nusername:s:utf8-user\r\n";

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content, Encoding.UTF8);

            var profile = await svc.ImportFromRdpFileAsync(path);

            profile.Host.Should().Be("utf8host.example.com");
            profile.Username.Should().Be("utf8-user");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportFromJsonAsync_Rejects_Entries_Without_Name_Or_Host()
    {
        var svc = NewService();
        var json = """
                   [
                     { "Name": "",      "Host": "h.example.com" },
                     { "Name": "valid", "Host": ""               },
                     { "Name": "keeper", "Host": "k.example.com" }
                   ]
                   """;

        var count = await svc.ImportFromJsonAsync(json);
        count.Should().Be(1);

        var all = await svc.GetAllAsync();
        all.Should().ContainSingle(p => p.Name == "keeper");
    }

    [Fact]
    public async Task ImportFromJsonAsync_Forces_AutoConnect_False()
    {
        var svc = NewService();
        // Even if someone hand-crafts a JSON payload with AutoConnectOnStartup=true,
        // imports must not hijack the app's startup behavior.
        var json = """
                   [{ "Name": "forced-off", "Host": "f.example.com", "AutoConnectOnStartup": true }]
                   """;

        await svc.ImportFromJsonAsync(json);

        var p = (await svc.GetAllAsync()).Single();
        p.AutoConnectOnStartup.Should().BeFalse();
    }

    [Fact]
    public async Task GetGroupsAsync_Returns_Distinct_Non_Empty_Groups()
    {
        var svc = NewService();
        await svc.CreateAsync(new ConnectionProfile { Name = "A", Host = "h", Group = "Prod" });
        await svc.CreateAsync(new ConnectionProfile { Name = "B", Host = "h", Group = "Prod" });
        await svc.CreateAsync(new ConnectionProfile { Name = "C", Host = "h", Group = "Dev" });
        await svc.CreateAsync(new ConnectionProfile { Name = "D", Host = "h", Group = "" });

        var groups = await svc.GetGroupsAsync();

        groups.Should().BeEquivalentTo(new[] { "Dev", "Prod" });
    }
}
