using FluentAssertions;
using RemoteNest.Models;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>Emission tests for <see cref="RdpFileBuilder"/>.</summary>
public class RdpFileBuilderTests
{
    private static readonly string NL = Environment.NewLine;

    private static ConnectionProfile MinimalProfile() => new() { Name = "P", Host = "srv1" };

    /// <summary>The always-emitted keys, in order. Credential suppression slots in
    /// right after them, before any section key.</summary>
    private static readonly string[] CoreLines =
    [
        "full address:s:srv1",
        "screen mode id:i:1",
        "desktopwidth:i:1920",
        "desktopheight:i:1080",
        "session bpp:i:32",
        "redirectclipboard:i:1",
        "redirectdrives:i:0",
        "redirectprinters:i:0",
        "audiomode:i:0",
        "enablecredsspsupport:i:1",
        "authentication level:i:2"
    ];

    /// <summary>Exactly what a default profile emits — nothing more, in this order.</summary>
    private static readonly string Golden =
        string.Join(NL, [.. CoreLines, "dynamic resolution:i:1", ""]);

    [Fact]
    public void Default_Profile_Emits_Exactly_The_Core_Block()
    {
        RdpFileBuilder.Build(MinimalProfile(), "srv1", hasCredential: false)
            .Should().Be(Golden);
    }

    [Fact]
    public void Credential_Injection_Appends_Prompt_Suppression()
    {
        var expected = string.Join(NL,
        [
            .. CoreLines,
            "prompt for credentials:i:0",
            "prompt for credentials on client:i:0",
            "dynamic resolution:i:1",
            ""
        ]);
        RdpFileBuilder.Build(MinimalProfile(), "srv1", hasCredential: true)
            .Should().Be(expected);
    }

    [Fact]
    public void Export_Mode_Never_Writes_Prompt_Suppression()
    {
        var output = RdpFileBuilder.Build(MinimalProfile(), "srv1", hasCredential: true, forExport: true);
        output.Should().NotContain("prompt for credentials");
    }

    [Fact]
    public void Inactive_Sections_Emit_No_Keys()
    {
        var output = RdpFileBuilder.Build(MinimalProfile(), "srv1", false);
        output.Should().NotContain("use multimon").And.NotContain("gateway").And.NotContain("remoteapplication");
    }

    [Fact]
    public void Username_With_Domain_Formats_As_Domain_Backslash_User()
    {
        var p = MinimalProfile();
        p.Username = "user";
        p.Domain = "CORP";
        RdpFileBuilder.Build(p, "srv1", false).Should().Contain(@"username:s:CORP\user" + NL);
    }

    [Fact]
    public void Control_Characters_In_Values_Cannot_Inject_Lines()
    {
        var p = MinimalProfile();
        p.Username = "user\r\nalternate shell:s:evil.exe";
        var output = RdpFileBuilder.Build(p, "srv1", false);

        // No LINE may start with the injected key — the payload must stay inside the value.
        output.Split(NL).Should().NotContain(line => line.StartsWith("alternate shell:"));
        output.Should().Contain("username:s:useralternate shell:s:evil.exe" + NL,
            "control characters are stripped, collapsing the payload into the value");
    }

    [Fact]
    public void Multimon_Emits_Only_Valid_Monitor_Lists()
    {
        var p = MinimalProfile();
        p.UseMultimon = true;
        p.SelectedMonitors = "0,2";
        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("use multimon:i:1" + NL).And.Contain("selectedmonitors:s:0,2" + NL);

        p.SelectedMonitors = "0;2 DROP TABLE";
        RdpFileBuilder.Build(p, "srv1", false).Should().NotContain("selectedmonitors");

        p.UseMultimon = false;
        p.SelectedMonitors = "0,2";
        RdpFileBuilder.Build(p, "srv1", false).Should().NotContain("selectedmonitors");
    }

    [Theory]
    [InlineData(false, "",      null)]                            // drives off -> no granular key
    [InlineData(true,  "",      "drivestoredirect:s:*")]          // on, no list -> all drives
    [InlineData(true,  "C:;D:", "drivestoredirect:s:C:;D:")]      // on, explicit list
    public void Drive_Redirection_Precedence(bool redirectDrives, string list, string? expectedLine)
    {
        var p = MinimalProfile();
        p.RedirectDrives = redirectDrives;
        p.DrivesToRedirect = list;
        var output = RdpFileBuilder.Build(p, "srv1", false);

        if (expectedLine is null)
            output.Should().NotContain("drivestoredirect");
        else
            output.Should().Contain(expectedLine + NL);
        output.Should().Contain($"redirectdrives:i:{(redirectDrives ? 1 : 0)}" + NL);
    }

    [Fact]
    public void Gateway_Block_Only_When_Hostname_Set_And_Includes_Profile_Usage_Method()
    {
        var p = MinimalProfile();
        RdpFileBuilder.Build(p, "srv1", false).Should().NotContain("gateway");

        p.GatewayHostname = "gw.corp.com";
        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("gatewayhostname:s:gw.corp.com" + NL);
        output.Should().Contain("gatewayusagemethod:i:2" + NL);
        output.Should().Contain("gatewayprofileusagemethod:i:1" + NL);
        output.Should().Contain("gatewaycredentialssource:i:0" + NL);
        output.Should().Contain("promptcredentialonce:i:1" + NL);
    }

    [Fact]
    public void Experience_Block_Gated_By_NetworkAutoDetect()
    {
        var p = MinimalProfile();
        p.DisableWallpaper = true;
        p.ConnectionType = 2;
        RdpFileBuilder.Build(p, "srv1", false).Should().NotContain("disable wallpaper").And.NotContain("connection type");

        p.NetworkAutoDetect = false;
        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("networkautodetect:i:0" + NL);
        output.Should().Contain("connection type:i:2" + NL);
        output.Should().Contain("disable wallpaper:i:1" + NL);
    }

    [Fact]
    public void RemoteApp_Block_Only_When_Mode_On()
    {
        var p = MinimalProfile();
        p.RemoteAppProgram = "||excel";
        RdpFileBuilder.Build(p, "srv1", false).Should().NotContain("remoteapplication");

        p.RemoteAppMode = true;
        p.RemoteAppName = "Excel";
        p.RemoteAppCmdLine = "/x report.xlsx";
        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("remoteapplicationmode:i:1" + NL);
        output.Should().Contain("remoteapplicationprogram:s:||excel" + NL);
        output.Should().Contain("remoteapplicationname:s:Excel" + NL);
        output.Should().Contain("remoteapplicationcmdline:s:/x report.xlsx" + NL);
    }

    [Fact]
    public void Session_And_Security_Keys_Emit_Only_On_Non_Defaults()
    {
        var p = MinimalProfile();
        p.AdministrativeSession = true;
        p.AutoReconnect = false;
        p.AutoReconnectMaxRetries = 5;
        p.DisplayConnectionBar = false;
        p.PublicMode = true;
        p.KeyboardHook = 0;
        p.AudioCaptureMode = true;
        p.RedirectSmartCards = false;
        p.Compression = false;

        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("administrative session:i:1" + NL);
        output.Should().Contain("autoreconnection enabled:i:0" + NL);
        output.Should().Contain("autoreconnect max retries:i:5" + NL);
        output.Should().Contain("displayconnectionbar:i:0" + NL);
        output.Should().Contain("public mode:i:1" + NL);
        output.Should().Contain("keyboardhook:i:0" + NL);
        output.Should().Contain("audiocapturemode:i:1" + NL);
        output.Should().Contain("redirectsmartcards:i:0" + NL);
        output.Should().Contain("compression:i:0" + NL);
    }

    [Fact]
    public void ExtraSettings_Appended_Last_With_App_Keys_Winning()
    {
        var p = MinimalProfile();
        p.ExtraSettings = string.Join('\n',
        [
            "winposstr:s:0,1,100,100,900,700",   // pass through
            "AUDIOMODE:i:1",                     // app already emits audiomode -> dropped
            "signature:s:AAAA",                  // drop-list
            "not a valid line",                  // malformed -> dropped
            "alternate shell:s:notepad.exe"      // pass through
        ]);

        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("winposstr:s:0,1,100,100,900,700" + NL);
        output.Should().Contain("alternate shell:s:notepad.exe" + NL);
        output.Should().Contain("audiomode:i:0" + NL);       // the app's value
        output.Should().NotContain("audiomode:i:1");
        output.Should().NotContain("signature");
        output.Should().NotContain("not a valid line");
        output.TrimEnd().Should().EndWith("alternate shell:s:notepad.exe", "passthrough comes last");
    }

    [Fact]
    public void ExtraSettings_Duplicate_Passthrough_Keys_Keep_First()
    {
        var p = MinimalProfile();
        p.ExtraSettings = "winposstr:s:first\nwinposstr:s:second";
        var output = RdpFileBuilder.Build(p, "srv1", false);
        output.Should().Contain("winposstr:s:first" + NL);
        output.Should().NotContain("winposstr:s:second");
    }
}

public class RdpLauncherArgumentTests
{
    [Fact]
    public void Launch_Arguments_Start_With_Rdp_File_And_Add_Cli_Switches()
    {
        var p = new ConnectionProfile { Host = "h", RestrictedAdmin = true, PromptForCredentials = true, PublicMode = true };
        var args = RdpLauncherService.BuildLaunchArguments(p, @"C:\t\s.rdp");
        args.Should().Equal(@"C:\t\s.rdp", "/restrictedAdmin", "/prompt", "/public");
    }

    [Fact]
    public void RestrictedAdmin_Wins_Over_RemoteGuard()
    {
        var p = new ConnectionProfile { Host = "h", RestrictedAdmin = true, RemoteGuard = true };
        RdpLauncherService.BuildLaunchArguments(p, "f.rdp")
            .Should().Contain("/restrictedAdmin").And.NotContain("/remoteGuard");
    }

    [Fact]
    public void RemoteGuard_Alone_Is_Emitted()
    {
        var p = new ConnectionProfile { Host = "h", RemoteGuard = true };
        RdpLauncherService.BuildLaunchArguments(p, "f.rdp")
            .Should().Equal("f.rdp", "/remoteGuard");
    }

    [Fact]
    public void Shadow_Arguments_Include_Port_And_Flags()
    {
        var p = new ConnectionProfile { Host = "srv", Port = 3390 };
        RdpLauncherService.BuildShadowArguments(p, 7, control: true, noConsentPrompt: true)
            .Should().Equal("/v:srv:3390", "/shadow:7", "/control", "/noConsentPrompt");

        p.Port = 3389;
        RdpLauncherService.BuildShadowArguments(p, 7, control: false, noConsentPrompt: false)
            .Should().Equal("/v:srv", "/shadow:7");
    }
}
