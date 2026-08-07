using System.Collections;
using System.Resources;
using FluentAssertions;
using RemoteNest.Models;
using RemoteNest.Services;
using RemoteNest.ViewModels;
using Xunit;

namespace RemoteNest.Tests;

/// <summary>Fakes so the editor VM is testable without a database or WPF.</summary>
internal sealed class FakeConnectionService : IConnectionService
{
    public List<ConnectionProfile> Profiles { get; } = new();
    public ConnectionProfile? LastUpdated { get; private set; }

    public List<int> DeletedIds { get; } = new();

    public Task<List<ConnectionProfile>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(Profiles.ToList());
    public Task<ConnectionProfile?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Profiles.FirstOrDefault(p => p.Id == id));
    public Task<ConnectionProfile> CreateAsync(ConnectionProfile profile, CancellationToken ct = default)
    { profile.Id = Profiles.Count + 1; Profiles.Add(profile); return Task.FromResult(profile); }
    public Task<bool> UpdateAsync(ConnectionProfile profile, CancellationToken ct = default)
    { LastUpdated = profile; return Task.FromResult(true); }
    public Task DeleteAsync(int id, CancellationToken ct = default)
    { DeletedIds.Add(id); Profiles.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
    public Task<ConnectionProfile> DuplicateAsync(int id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<List<string>> GetGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<string>());
    public Task RecordConnectionAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> ExportToJsonAsync(CancellationToken ct = default) => Task.FromResult("[]");
    public Task<int> ImportFromJsonAsync(string json, CancellationToken ct = default) => Task.FromResult(0);
    public Task<ConnectionProfile> ImportFromRdpFileAsync(string rdpFilePath, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class FakeEncryptionService : IEncryptionService
{
    public string Encrypt(string plainText) => "enc:" + plainText;
    public string Decrypt(string encryptedText) =>
        encryptedText.StartsWith("enc:") ? encryptedText[4..] : string.Empty;
}

internal sealed class FakeRdpLauncher : IRdpLauncherService
{
    public ConnectionProfile? LastLaunched { get; private set; }
    public Task LaunchAsync(ConnectionProfile profile, string? plainPassword = null)
    { LastLaunched = profile; return Task.CompletedTask; }
    public Task LaunchShadowAsync(ConnectionProfile profile, int sessionId, bool control, bool noConsentPrompt) =>
        Task.CompletedTask;
}

public class EditorViewModelTests
{
    private static (ConnectionEditorViewModel vm, FakeConnectionService svc, FakeRdpLauncher launcher) NewVm()
    {
        var svc = new FakeConnectionService();
        var launcher = new FakeRdpLauncher();
        return (new ConnectionEditorViewModel(svc, new FakeEncryptionService(), launcher), svc, launcher);
    }

    [Fact]
    public void Every_Mappable_Model_Property_Has_A_Vm_Counterpart()
    {
        var vmNames = typeof(ConnectionEditorViewModel).GetProperties().Select(p => p.Name).ToHashSet();
        var missing = ProfileReflection.SettableProperties
            .Select(p => p.Name)
            .Where(n => !ConnectionEditorViewModel.ModelPropertiesWithoutVmCounterpart.Contains(n))
            .Where(n => !vmNames.Contains(n))
            .ToList();
        missing.Should().BeEmpty("every profile setting needs an editor property, or must be listed as intentionally excluded");
    }

    [Fact]
    public async Task LoadExisting_Then_Save_Roundtrips_Every_Setting()
    {
        var (vm, svc, _) = NewVm();
        var source = ProfileReflection.CreateFullyPopulated(seed: 4);
        source.Id = 42;
        // Fields with VM couplings need consistent values to survive untouched.
        source.RestrictedAdmin = true;
        source.RemoteGuard = false;
        source.FullScreen = true;   // UseMultimon=true (flipped default) requires it
        source.UseMultimon = true;
        source.EncryptedPassword = "enc:hunter2";
        svc.Profiles.Add(source);

        await vm.LoadExistingAsync(source);
        vm.Password.Should().Be("hunter2");

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = svc.LastUpdated!;
        saved.Id.Should().Be(42);
        ProfileReflection.AssertAllPropertiesEqual(source, saved, ignoreCreatedAt: true,
            ignore:
            [
                nameof(ConnectionProfile.Id),
                nameof(ConnectionProfile.EncryptedPassword), // re-encrypted
                nameof(ConnectionProfile.LastConnectedAt),
                nameof(ConnectionProfile.ConnectionCount),
                // trimmed + sanitized on save; string factory values have no whitespace so
                // only ExtraSettings (not a valid key:type:value line) actually changes
                nameof(ConnectionProfile.ExtraSettings)
            ]);
        saved.EncryptedPassword.Should().Be("enc:hunter2");
    }

    [Fact]
    public void New_Vm_Defaults_Match_New_Profile_Defaults()
    {
        var (vm, _, _) = NewVm();
        var target = new ConnectionProfile();
        vm.CollectProfile(target);

        ProfileReflection.AssertAllPropertiesEqual(new ConnectionProfile(), target, ignoreCreatedAt: true,
            ignore:
            [
                nameof(ConnectionProfile.Id),
                nameof(ConnectionProfile.EncryptedPassword),
                nameof(ConnectionProfile.LastConnectedAt),
                nameof(ConnectionProfile.ConnectionCount)
            ]);
        target.AuthenticationLevel.Should().Be(2);
        target.DynamicResolution.Should().BeTrue();
    }

    [Fact]
    public void RestrictedAdmin_And_RemoteGuard_Are_Mutually_Exclusive()
    {
        var (vm, _, _) = NewVm();

        vm.RestrictedAdmin = true;
        vm.RemoteGuard = true;
        vm.RestrictedAdmin.Should().BeFalse("enabling RemoteGuard clears RestrictedAdmin");
        vm.RemoteGuard.Should().BeTrue();

        vm.RestrictedAdmin = true;
        vm.RemoteGuard.Should().BeFalse("enabling RestrictedAdmin clears RemoteGuard");
    }

    [Fact]
    public void Multimon_Forces_FullScreen_And_Leaving_FullScreen_Drops_Multimon()
    {
        var (vm, _, _) = NewVm();

        vm.UseMultimon = true;
        vm.FullScreen.Should().BeTrue();

        vm.FullScreen = false;
        vm.UseMultimon.Should().BeFalse();
    }

    [Fact]
    public async Task CopyFromProfile_Copies_Settings_But_Not_Identity()
    {
        var (vm, svc, _) = NewVm();
        var template = ProfileReflection.CreateFullyPopulated(seed: 9);
        template.Id = 7;
        template.Name = "Template";
        template.Host = "template.example.com";
        template.RestrictedAdmin = false;
        template.RemoteGuard = false;
        svc.Profiles.Add(template);

        await vm.LoadNewAsync();
        vm.Name = "Mine";
        vm.Host = "mine.example.com";
        vm.Notes = "my notes";

        vm.SelectedCopySource = template;
        vm.CopyFromProfileCommand.Execute(null);

        // Identity untouched
        vm.Name.Should().Be("Mine");
        vm.Host.Should().Be("mine.example.com");
        vm.Notes.Should().Be("my notes");
        // Settings copied
        vm.KeyboardHook.Should().Be(template.KeyboardHook);
        vm.GatewayHostname.Should().Be(template.GatewayHostname);
        vm.DisableWallpaper.Should().Be(template.DisableWallpaper);
        vm.RemoteAppProgram.Should().Be(template.RemoteAppProgram);
    }

    [Fact]
    public async Task TestConnection_Carries_Gateway_And_Auth_Level()
    {
        var (vm, _, launcher) = NewVm();
        vm.Host = "h.example.com";
        vm.GatewayHostname = "gw.example.com";
        vm.AuthenticationLevel = 1;
        vm.Password = "secret";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        var test = launcher.LastLaunched!;
        test.GatewayHostname.Should().Be("gw.example.com");
        test.AuthenticationLevel.Should().Be(1);
        test.EncryptedPassword.Should().BeEmpty("test connections never carry stored credentials");
        test.FullScreen.Should().BeFalse();
    }
}

/// <summary>
/// en/pt-BR resource parity — a missing key renders as "[key]" at runtime with no build
/// error, so this is the only guard for the ~200-key catalog.
/// </summary>
public class ResourceParityTests
{
    private static HashSet<string> Keys(string culture)
    {
        var rm = new ResourceManager("RemoteNest.Resources.Strings", typeof(RemoteNest.Localization.TranslationSource).Assembly);
        var set = rm.GetResourceSet(new System.Globalization.CultureInfo(culture), true, true)!;
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
    }

    [Fact]
    public void PtBr_Has_Exactly_The_Same_Keys_As_Neutral_English()
    {
        var en = Keys("");
        en.Should().NotBeEmpty();
        // GetResourceSet(pt-BR) with tryParents merges parents in some cases; enumerate the
        // satellite directly instead: any en key missing from pt-BR surfaces here.
        var rm = new ResourceManager("RemoteNest.Resources.Strings", typeof(RemoteNest.Localization.TranslationSource).Assembly);
        var ptOnly = rm.GetResourceSet(new System.Globalization.CultureInfo("pt-BR"), true, false);
        ptOnly.Should().NotBeNull("the pt-BR satellite assembly must be present");
        var ptKeys = ptOnly!.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        ptKeys.Except(en).Should().BeEmpty("pt-BR must not carry orphan keys");
        en.Except(ptKeys).Should().BeEmpty("every English key needs a pt-BR translation");
    }
}
