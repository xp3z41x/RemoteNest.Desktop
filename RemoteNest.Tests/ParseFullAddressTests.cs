using FluentAssertions;
using RemoteNest.Models;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>
/// Validates the .rdp "full address" parser that feeds ImportFromRdpFileAsync.
/// Covers IPv6 bracketed hosts, explicit ports, IPv4, DNS names, and malformed fallback.
/// </summary>
public class ParseFullAddressTests
{
    [Theory]
    [InlineData("host.example.com",  "host.example.com", 3389)]
    [InlineData("10.0.0.1",           "10.0.0.1",         3389)]
    [InlineData("10.0.0.1:3390",      "10.0.0.1",         3390)]
    [InlineData("server01:3389",      "server01",         3389)]
    [InlineData("[::1]",              "[::1]",            3389)]
    [InlineData("[::1]:3389",         "[::1]",            3389)]
    [InlineData("[fe80::1]:3390",     "[fe80::1]",        3390)]
    public void ParseFullAddress_Canonical_Inputs(string value, string expectedHost, int expectedPort)
    {
        var profile = new ConnectionProfile { Port = 3389 };

        ConnectionService.ParseFullAddress(value, profile);

        profile.Host.Should().Be(expectedHost);
        profile.Port.Should().Be(expectedPort);
    }

    [Theory]
    [InlineData("host:70000")] // port out of range — fall back to default
    [InlineData("host:0")]      // port must be >= 1
    public void ParseFullAddress_Port_Out_Of_Range_Uses_Default(string value)
    {
        var profile = new ConnectionProfile { Port = 3389 };

        ConnectionService.ParseFullAddress(value, profile);

        profile.Port.Should().Be(3389); // untouched
    }

    [Fact]
    public void ParseFullAddress_Malformed_Falls_Back_To_Whole_Value_As_Host()
    {
        var profile = new ConnectionProfile { Port = 3389 };

        ConnectionService.ParseFullAddress("not valid host", profile);

        profile.Host.Should().Be("not valid host");
    }
}

public class EscapeLikeTests
{
    [Theory]
    [InlineData("plain",       "plain")]
    [InlineData("50%",         "50\\%")]
    [InlineData("a_b",         "a\\_b")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("%_\\",        "\\%\\_\\\\")]
    public void EscapeLike_Escapes_Sqlite_Wildcards(string input, string expected)
    {
        ConnectionService.EscapeLike(input).Should().Be(expected);
    }
}
