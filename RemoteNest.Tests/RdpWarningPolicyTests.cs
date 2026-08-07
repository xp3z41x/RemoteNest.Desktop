using FluentAssertions;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>In-memory registry — these tests must never touch the real hives.</summary>
internal sealed class FakeRegistry : IRegistryAccessor
{
    private readonly Dictionary<string, int> _values = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(RegistryHiveKind hive, string path, string name) => $"{hive}|{path}|{name}";

    public int? ReadDword(RegistryHiveKind hive, string path, string name) =>
        _values.TryGetValue(Key(hive, path, name), out var v) ? v : null;

    public void WriteDword(RegistryHiveKind hive, string path, string name, int value) =>
        _values[Key(hive, path, name)] = value;

    public void DeleteValue(RegistryHiveKind hive, string path, string name) =>
        _values.Remove(Key(hive, path, name));

    public bool Exists(RegistryHiveKind hive, string path, string name) =>
        _values.ContainsKey(Key(hive, path, name));

    public int Count => _values.Count;
}

internal sealed class FakeBackupStore : IPolicyBackupStore
{
    private string? _value;
    public string? Read() => _value;
    public void Write(string value) => _value = value;
}

public class RdpWarningPolicyTests
{
    private static readonly string[] Hosts = ["srv-a.example.com", "srv-b.example.com"];

    private static RdpWarningPolicyService.PolicyValue Core(string id) =>
        RdpWarningPolicyService.CoreValues.Single(v => v.Id == id);

    /// <summary>Elevation stand-in that performs the HKLM change directly against the fake.</summary>
    private static Func<string, Task<bool>> GrantingElevation(FakeRegistry registry) => arg =>
        Task.FromResult(RdpWarningPolicyService.RunElevatedOperation(arg, registry) == 0);

    private static Func<string, Task<bool>> DeniedElevation() => _ => Task.FromResult(false);

    [Fact]
    public async Task Apply_Sets_Every_Documented_Value_Including_Per_Host_Trust()
    {
        var registry = new FakeRegistry();
        var svc = new RdpWarningPolicyService(registry, new FakeBackupStore(), GrantingElevation(registry));

        (await svc.ApplyAsync(Hosts)).Should().BeTrue();

        // The April 2026 anti-phishing / redirection dialog (machine-wide policy).
        var redirection = Core("RedirectionWarningDialogVersion");
        redirection.Hive.Should().Be(RegistryHiveKind.LocalMachine);
        registry.ReadDword(redirection.Hive, redirection.Path, redirection.Name).Should().Be(1);

        var consent = Core("RdpLaunchConsentAccepted");
        registry.ReadDword(consent.Hive, consent.Path, consent.Name).Should().Be(1);

        var authLevel = Core("AuthenticationLevelOverride");
        registry.ReadDword(authLevel.Hive, authLevel.Path, authLevel.Name).Should().Be(0);

        foreach (var host in Hosts)
        {
            var entry = RdpWarningPolicyService.ForHost(host);
            registry.ReadDword(entry.Hive, entry.Path, entry.Name).Should().Be(0x4D);
        }

        svc.GetStatus().Should().Be(RdpWarningPolicyStatus.Patched);
    }

    [Fact]
    public async Task Revert_Deletes_Values_That_Did_Not_Exist_Before()
    {
        var registry = new FakeRegistry();
        var svc = new RdpWarningPolicyService(registry, new FakeBackupStore(), GrantingElevation(registry));

        await svc.ApplyAsync(Hosts);
        registry.Count.Should().BeGreaterThan(0);

        (await svc.RevertAsync()).Should().BeTrue();

        registry.Count.Should().Be(0, "nothing existed before the patch, so nothing may remain after revert");
        svc.GetStatus().Should().Be(RdpWarningPolicyStatus.Default);
    }

    [Fact]
    public async Task Revert_Restores_Pre_Existing_Values_Rather_Than_Deleting_Them()
    {
        var registry = new FakeRegistry();
        var svc = new RdpWarningPolicyService(registry, new FakeBackupStore(), GrantingElevation(registry));

        // The user already had their own values before ever using the patch.
        var redirection = Core("RedirectionWarningDialogVersion");
        var authLevel = Core("AuthenticationLevelOverride");
        registry.WriteDword(redirection.Hive, redirection.Path, redirection.Name, 7);
        registry.WriteDword(authLevel.Hive, authLevel.Path, authLevel.Name, 2);

        await svc.ApplyAsync(Hosts);
        registry.ReadDword(authLevel.Hive, authLevel.Path, authLevel.Name).Should().Be(0);

        await svc.RevertAsync();

        registry.ReadDword(redirection.Hive, redirection.Path, redirection.Name)
            .Should().Be(7, "the original machine-wide value must come back untouched");
        registry.ReadDword(authLevel.Hive, authLevel.Path, authLevel.Name)
            .Should().Be(2, "the original per-user value must come back untouched");
    }

    [Fact]
    public async Task Applying_Twice_Keeps_The_Original_Snapshot()
    {
        var registry = new FakeRegistry();
        var backup = new FakeBackupStore();
        var svc = new RdpWarningPolicyService(registry, backup, GrantingElevation(registry));

        var authLevel = Core("AuthenticationLevelOverride");
        registry.WriteDword(authLevel.Hive, authLevel.Path, authLevel.Name, 2);

        await svc.ApplyAsync(Hosts);
        await svc.ApplyAsync(Hosts); // second apply must not snapshot our own patched value
        await svc.RevertAsync();

        registry.ReadDword(authLevel.Hive, authLevel.Path, authLevel.Name).Should().Be(2);
    }

    [Fact]
    public async Task Declined_Elevation_Still_Applies_Per_User_Values_And_Reports_Partial()
    {
        var registry = new FakeRegistry();
        var svc = new RdpWarningPolicyService(registry, new FakeBackupStore(), DeniedElevation());

        (await svc.ApplyAsync(Hosts)).Should().BeFalse();

        var redirection = Core("RedirectionWarningDialogVersion");
        registry.Exists(redirection.Hive, redirection.Path, redirection.Name)
            .Should().BeFalse("the machine-wide value needs admin approval that was declined");

        var consent = Core("RdpLaunchConsentAccepted");
        registry.ReadDword(consent.Hive, consent.Path, consent.Name).Should().Be(1);

        svc.GetStatus().Should().Be(RdpWarningPolicyStatus.Partial);
    }

    [Fact]
    public async Task Declined_Elevation_On_Revert_Keeps_The_Pending_Entry_For_A_Retry()
    {
        var registry = new FakeRegistry();
        var backup = new FakeBackupStore();
        var granting = new RdpWarningPolicyService(registry, backup, GrantingElevation(registry));
        await granting.ApplyAsync(Hosts);

        var denied = new RdpWarningPolicyService(registry, backup, DeniedElevation());
        (await denied.RevertAsync()).Should().BeFalse();

        // Per-user values are already restored...
        var consent = Core("RdpLaunchConsentAccepted");
        registry.Exists(consent.Hive, consent.Path, consent.Name).Should().BeFalse();

        // ...and the machine-wide entry survives in the snapshot so a retry completes it.
        var pending = denied.LoadBackup();
        pending.Should().ContainKey("RedirectionWarningDialogVersion");
        pending.Should().HaveCount(1);

        var retry = new RdpWarningPolicyService(registry, backup, GrantingElevation(registry));
        (await retry.RevertAsync()).Should().BeTrue();
        registry.Count.Should().Be(0);
    }

    [Fact]
    public async Task Revert_Without_A_Snapshot_Falls_Back_To_Windows_Defaults()
    {
        var registry = new FakeRegistry();
        // Values present but no backup recorded (e.g. patched by hand or settings lost).
        foreach (var value in RdpWarningPolicyService.CoreValues)
            registry.WriteDword(value.Hive, value.Path, value.Name, value.PatchedData);

        var svc = new RdpWarningPolicyService(registry, new FakeBackupStore(), GrantingElevation(registry));
        (await svc.RevertAsync()).Should().BeTrue();

        registry.Count.Should().Be(0);
        svc.GetStatus().Should().Be(RdpWarningPolicyStatus.Default);
    }

    [Theory]
    [InlineData("set:1", 0, 1)]
    [InlineData("set:0", 0, 0)]
    [InlineData("bogus", 2, null)]
    public void Elevated_Helper_Handles_Its_Arguments(string argument, int expectedExit, int? expectedData)
    {
        var registry = new FakeRegistry();
        RdpWarningPolicyService.RunElevatedOperation(argument, registry).Should().Be(expectedExit);

        var policy = Core("RedirectionWarningDialogVersion");
        registry.ReadDword(policy.Hive, policy.Path, policy.Name).Should().Be(expectedData);
    }

    [Fact]
    public void Elevated_Helper_Only_Ever_Touches_The_Machine_Wide_Value()
    {
        var registry = new FakeRegistry();
        RdpWarningPolicyService.RunElevatedOperation("set:1", registry);

        registry.Count.Should().Be(1);
        RdpWarningPolicyService.CoreValues
            .Where(v => !v.RequiresElevation)
            .Should().OnlyContain(v => !registry.Exists(v.Hive, v.Path, v.Name));
    }

    [Fact]
    public void Host_Entries_Round_Trip_Through_Their_Id()
    {
        var entry = RdpWarningPolicyService.ForHost("host.example.com");
        var resolved = RdpWarningPolicyService.ResolveById(entry.Id);

        resolved.Should().NotBeNull();
        resolved!.Name.Should().Be("host.example.com");
        resolved.Hive.Should().Be(RegistryHiveKind.CurrentUser);
        resolved.RequiresElevation.Should().BeFalse();
    }
}
