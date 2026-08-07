using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using RemoteNest.Data;
using RemoteNest.Models;
using RemoteNest.Serialization;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>
/// Import/export tests: full .rdp key coverage, unknown-key passthrough, DTO parity,
/// and hostile-input clamping.
/// </summary>
public class ImportExportTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private Database _db = null!;

    public Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"remotenest-ie-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _db.EnsureCreated();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    private ConnectionService NewService() => new(_db);

    private async Task<ConnectionProfile> ImportRdpContent(string content)
    {
        var svc = NewService();
        var path = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}.rdp");
        try
        {
            File.WriteAllText(path, content, Encoding.Unicode);
            return await svc.ImportFromRdpFileAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Rdp_Import_Parses_All_New_First_Class_Keys()
    {
        var profile = await ImportRdpContent("""
            full address:s:big.example.com:3390
            use multimon:i:1
            selectedmonitors:s:0,1
            dynamic resolution:i:1
            smart sizing:i:1
            desktopscalefactor:i:150
            audiomode:i:1
            audiocapturemode:i:1
            audioqualitymode:i:2
            camerastoredirect:s:*
            devicestoredirect:s:*
            usbdevicestoredirect:s:{6bdd1fc6-810f-11d0-bec7-08002be2092f}
            redirectcomports:i:1
            redirectsmartcards:i:0
            redirectwebauthn:i:0
            redirectlocation:i:1
            keyboardhook:i:1
            gatewayhostname:s:gw.corp.com
            gatewayusagemethod:i:1
            gatewaycredentialssource:i:4
            promptcredentialonce:i:0
            loadbalanceinfo:s:tsv://MS Terminal Services Plugin.1.Farm1
            pcb:s:8f7c2d34-vm-id
            kdcproxyname:s:kdc.corp.com
            enablerdsaadauth:i:1
            networkautodetect:i:0
            connection type:i:3
            bandwidthautodetect:i:0
            compression:i:0
            bitmapcachepersistenable:i:0
            disable wallpaper:i:1
            disable themes:i:1
            allow font smoothing:i:1
            videoplaybackmode:i:0
            administrative session:i:1
            autoreconnection enabled:i:0
            autoreconnect max retries:i:7
            displayconnectionbar:i:0
            pinconnectionbar:i:0
            public mode:i:1
            authentication level:i:1
            remoteapplicationmode:i:1
            remoteapplicationprogram:s:||excel
            remoteapplicationname:s:Excel
            remoteapplicationcmdline:s:/x
            drivestoredirect:s:C:;D:
            """);

        profile.Host.Should().Be("big.example.com");
        profile.Port.Should().Be(3390);
        profile.UseMultimon.Should().BeTrue();
        profile.SelectedMonitors.Should().Be("0,1");
        profile.DynamicResolution.Should().BeTrue();
        profile.SmartSizing.Should().BeTrue();
        profile.DesktopScaleFactor.Should().Be(150);
        profile.AudioPlaybackMode.Should().Be(1);
        profile.AudioCaptureMode.Should().BeTrue();
        profile.AudioQualityMode.Should().Be(2);
        profile.CamerasToRedirect.Should().Be("*");
        profile.PnpDevicesToRedirect.Should().Be("*");
        profile.UsbDevicesToRedirect.Should().Be("{6bdd1fc6-810f-11d0-bec7-08002be2092f}");
        profile.RedirectComPorts.Should().BeTrue();
        profile.RedirectSmartCards.Should().BeFalse();
        profile.RedirectWebAuthn.Should().BeFalse();
        profile.RedirectLocation.Should().BeTrue();
        profile.KeyboardHook.Should().Be(1);
        profile.GatewayHostname.Should().Be("gw.corp.com");
        profile.GatewayUsageMethod.Should().Be(1);
        profile.GatewayCredentialsSource.Should().Be(4);
        profile.GatewayPromptCredentialOnce.Should().BeFalse();
        profile.LoadBalanceInfo.Should().Be("tsv://MS Terminal Services Plugin.1.Farm1");
        profile.Pcb.Should().Be("8f7c2d34-vm-id");
        profile.KdcProxyName.Should().Be("kdc.corp.com");
        profile.EnableRdsAadAuth.Should().BeTrue();
        profile.NetworkAutoDetect.Should().BeFalse();
        profile.ConnectionType.Should().Be(3);
        profile.BandwidthAutoDetect.Should().BeFalse();
        profile.Compression.Should().BeFalse();
        profile.BitmapCachePersist.Should().BeFalse();
        profile.DisableWallpaper.Should().BeTrue();
        profile.DisableThemes.Should().BeTrue();
        profile.AllowFontSmoothing.Should().BeTrue();
        profile.VideoPlaybackMode.Should().BeFalse();
        profile.AdministrativeSession.Should().BeTrue();
        profile.AutoReconnect.Should().BeFalse();
        profile.AutoReconnectMaxRetries.Should().Be(7);
        profile.DisplayConnectionBar.Should().BeFalse();
        profile.PinConnectionBar.Should().BeFalse();
        profile.PublicMode.Should().BeTrue();
        profile.AuthenticationLevel.Should().Be(1);
        profile.RemoteAppMode.Should().BeTrue();
        profile.RemoteAppProgram.Should().Be("||excel");
        profile.RemoteAppName.Should().Be("Excel");
        profile.RemoteAppCmdLine.Should().Be("/x");
        // drivestoredirect with a list implies drives on, list preserved.
        profile.RedirectDrives.Should().BeTrue();
        profile.DrivesToRedirect.Should().Be("C:;D:");
        profile.ExtraSettings.Should().BeEmpty("every key in this file is first-class");
    }

    [Fact]
    public async Task Rdp_Import_Preserves_Unknown_Keys_And_Drops_Signatures()
    {
        var profile = await ImportRdpContent("""
            full address:s:host1
            winposstr:s:0,1,10,10,800,600
            shell working directory:s:C:\work
            signature:s:AAAABBBB
            signscope:s:Full Address
            password 51:b:01000000ABCDEF
            camerastoredirect:s:
            """);

        profile.ExtraSettings.Should().Contain("winposstr:s:0,1,10,10,800,600");
        profile.ExtraSettings.Should().Contain(@"shell working directory:s:C:\work");
        profile.ExtraSettings.Should().NotContain("signature").And.NotContain("password 51");
    }

    [Fact]
    public async Task Rdp_Import_Star_Drive_List_Maps_To_All_Drives()
    {
        var profile = await ImportRdpContent("""
            full address:s:host1
            drivestoredirect:s:*
            """);
        profile.RedirectDrives.Should().BeTrue();
        profile.DrivesToRedirect.Should().BeEmpty();
    }

    [Fact]
    public async Task Rdp_Import_Disabled_Gateway_Clears_Hostname()
    {
        var profile = await ImportRdpContent("""
            full address:s:host1
            gatewayhostname:s:gw.corp.com
            gatewayusagemethod:i:0
            """);
        profile.GatewayHostname.Should().BeEmpty();
    }

    [Fact]
    public async Task Rdp_Import_Then_Build_Roundtrips_Semantically()
    {
        var profile = await ImportRdpContent("""
            full address:s:rt.example.com
            use multimon:i:1
            screen mode id:i:2
            keyboardhook:i:0
            gatewayhostname:s:gw.example.com
            winposstr:s:0,1,10,10,800,600
            """);

        var rebuilt = RdpFileBuilder.Build(profile, "rt.example.com", hasCredential: false, forExport: true);
        var reimported = await ImportRdpContent(rebuilt);

        reimported.UseMultimon.Should().BeTrue();
        reimported.KeyboardHook.Should().Be(0);
        reimported.GatewayHostname.Should().Be("gw.example.com");
        reimported.ExtraSettings.Should().Contain("winposstr:s:0,1,10,10,800,600");
    }

    [Fact]
    public async Task Json_Export_Then_Import_Roundtrips_Every_New_Field()
    {
        var svc = NewService();
        var full = ProfileReflection.CreateFullyPopulated(seed: 3);
        // Fields the import path deliberately rewrites are exercised separately.
        full.RestrictedAdmin = true;
        full.RemoteGuard = false;
        await svc.CreateAsync(full);

        var json = await svc.ExportToJsonAsync();
        json.Should().NotContain("EncryptedPassword").And.NotContain("encryptedPassword");

        await svc.DeleteAsync(full.Id);
        var count = await svc.ImportFromJsonAsync(json);
        count.Should().Be(1);

        var imported = (await svc.GetAllAsync()).Single();
        ProfileReflection.AssertAllPropertiesEqual(full, imported, ignoreCreatedAt: true,
            ignore:
            [
                nameof(ConnectionProfile.Id),
                nameof(ConnectionProfile.EncryptedPassword),
                nameof(ConnectionProfile.LastConnectedAt),
                nameof(ConnectionProfile.ConnectionCount),
                nameof(ConnectionProfile.AutoConnectOnStartup), // always forced off on import
                // clamped fields get their own theory below
                nameof(ConnectionProfile.Port),
                nameof(ConnectionProfile.ScreenWidth),
                nameof(ConnectionProfile.ScreenHeight),
                nameof(ConnectionProfile.ColorDepth),
                nameof(ConnectionProfile.AudioPlaybackMode),
                nameof(ConnectionProfile.AudioQualityMode),
                nameof(ConnectionProfile.KeyboardHook),
                nameof(ConnectionProfile.AuthenticationLevel),
                nameof(ConnectionProfile.ConnectionType),
                nameof(ConnectionProfile.AutoReconnectMaxRetries),
                nameof(ConnectionProfile.DesktopScaleFactor),
                nameof(ConnectionProfile.GatewayUsageMethod),
                nameof(ConnectionProfile.GatewayCredentialsSource),
                nameof(ConnectionProfile.ExtraSettings)         // sanitized on import
            ]);
    }

    [Fact]
    public void Export_Dto_Defaults_Match_The_Model_Defaults()
    {
        // A JSON file that omits a field must import as the new-profile default. If the
        // DTO kept CLR zero values instead, an absent AuthenticationLevel would silently
        // become 0 — "connect even if the certificate is bad".
        var model = new ConnectionProfile();
        var dto = new ConnectionProfileExport();

        foreach (var dtoProp in typeof(ConnectionProfileExport).GetProperties())
        {
            var modelProp = typeof(ConnectionProfile).GetProperty(dtoProp.Name);
            modelProp.Should().NotBeNull($"{dtoProp.Name} must exist on the model");
            dtoProp.GetValue(dto).Should().Be(modelProp!.GetValue(model),
                $"the export DTO default for {dtoProp.Name} must match the model");
        }
    }

    [Fact]
    public async Task Json_Import_Of_A_Minimal_File_Fills_The_Rest_From_Defaults()
    {
        var svc = NewService();
        const string json = """
            [{ "Name": "Minimal", "Host": "min.example.com", "AutoConnectOnStartup": true }]
            """;

        (await svc.ImportFromJsonAsync(json)).Should().Be(1);
        var p = (await svc.GetAllAsync()).Single();

        p.Host.Should().Be("min.example.com");
        p.Port.Should().Be(3389, "absent numeric fields fall back to the column default");
        p.ColorDepth.Should().Be("32");
        p.AuthenticationLevel.Should().Be(2);
        p.DynamicResolution.Should().BeTrue();
        p.KeyboardHook.Should().Be(2);
        p.AutoConnectOnStartup.Should().BeFalse("imports never auto-connect");
    }

    [Fact]
    public async Task Json_Import_Clamps_Hostile_Values_And_Resolves_Exclusions()
    {
        var svc = NewService();
        const string json = """
            [{
              "Name": "Evil", "Host": "h", "Port": 99999,
              "KeyboardHook": 9, "AuthenticationLevel": 42, "ConnectionType": 0,
              "AudioQualityMode": -5, "AudioPlaybackMode": 77, "DesktopScaleFactor": 133,
              "AutoReconnectMaxRetries": 5000, "GatewayUsageMethod": 9,
              "GatewayCredentialsSource": 3,
              "RestrictedAdmin": true, "RemoteGuard": true,
              "Username": "u\r\nalternate shell:s:evil.exe",
              "ExtraSettings": "good:i:1\r\nsignature:s:AAA\r\nmalformed line\r\nalternate shell:s:cmd.exe"
            }]
            """;

        (await svc.ImportFromJsonAsync(json)).Should().Be(1);
        var p = (await svc.GetAllAsync()).Single();

        p.Port.Should().Be(3389);
        p.KeyboardHook.Should().Be(2);
        p.AuthenticationLevel.Should().Be(3);
        p.ConnectionType.Should().Be(1);
        p.AudioQualityMode.Should().Be(0);
        p.AudioPlaybackMode.Should().Be(2);
        p.DesktopScaleFactor.Should().Be(0);
        p.AutoReconnectMaxRetries.Should().Be(200);
        p.GatewayUsageMethod.Should().Be(2);
        p.GatewayCredentialsSource.Should().Be(0);
        p.RestrictedAdmin.Should().BeFalse("inconsistent exclusion clears both");
        p.RemoteGuard.Should().BeFalse();
        p.ExtraSettings.Should().Contain("good:i:1").And.Contain("alternate shell:s:cmd.exe");
        p.ExtraSettings.Should().NotContain("signature").And.NotContain("malformed");

        // The injection attempt in Username is neutralized at emission time.
        var rdp = RdpFileBuilder.Build(p, "h", false);
        rdp.Split(Environment.NewLine).Should().NotContain(l => l.StartsWith("alternate shell:s:evil.exe"));
    }
}

/// <summary>Reflection parity: the export DTO must cover every profile setting.</summary>
public class ExportDtoParityTests
{
    private static readonly string[] IntentionallyExcluded =
    [
        nameof(ConnectionProfile.Id),
        nameof(ConnectionProfile.EncryptedPassword),
        nameof(ConnectionProfile.CreatedAt),
        nameof(ConnectionProfile.LastConnectedAt),
        nameof(ConnectionProfile.ConnectionCount)
    ];

    [Fact]
    public void Every_Profile_Setting_Exists_On_The_Export_Dto()
    {
        var dtoNames = typeof(ConnectionProfileExport).GetProperties().Select(p => p.Name).ToHashSet();
        var missing = ProfileReflection.SettableProperties
            .Select(p => p.Name)
            .Where(n => !IntentionallyExcluded.Contains(n) && !dtoNames.Contains(n))
            .ToList();
        missing.Should().BeEmpty("every profile setting must round-trip through JSON export");
    }

    [Fact]
    public void Dto_Has_No_Orphan_Properties()
    {
        var modelNames = ProfileReflection.SettableProperties.Select(p => p.Name).ToHashSet();
        typeof(ConnectionProfileExport).GetProperties()
            .Select(p => p.Name)
            .Where(n => !modelNames.Contains(n))
            .Should().BeEmpty("DTO properties must map 1:1 onto the model for reflection mapping");
    }
}
