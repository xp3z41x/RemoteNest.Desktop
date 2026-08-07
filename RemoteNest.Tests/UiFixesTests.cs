using FluentAssertions;
using RemoteNest.Models;
using RemoteNest.Services;
using RemoteNest.ViewModels;
using Xunit;

namespace RemoteNest.Tests;

internal sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; }
    public int ConfirmCalls { get; private set; }
    public string? LastConfirmMessage { get; private set; }

    public string? ShowSaveFileDialog(string filter, string defaultFileName) => null;
    public string? ShowOpenFileDialog(string filter) => null;
    public void ShowError(string title, string message) { }
    public bool Confirm(string title, string message)
    {
        ConfirmCalls++;
        LastConfirmMessage = message;
        return ConfirmResult;
    }
}

/// <summary>Regression tests for the UI-review fixes.</summary>
public class UiFixesTests
{
    // ---- Delete confirmation (was: bare Delete keypress destroyed the profile) ----

    [Fact]
    public async Task Delete_Is_Cancelled_When_Confirmation_Is_Declined()
    {
        var svc = new FakeConnectionService();
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var vm = new ConnectionListViewModel(svc, dialogs);
        var profile = new ConnectionProfile { Id = 7, Name = "prod-db", Host = "db01" };
        svc.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        dialogs.ConfirmCalls.Should().Be(1);
        dialogs.LastConfirmMessage.Should().Contain("prod-db");
        svc.DeletedIds.Should().BeEmpty();
        vm.SelectedProfile.Should().BeSameAs(profile, "a declined confirmation must change nothing");
    }

    [Fact]
    public async Task Delete_Proceeds_When_Confirmed()
    {
        var svc = new FakeConnectionService();
        var dialogs = new FakeDialogService { ConfirmResult = true };
        var vm = new ConnectionListViewModel(svc, dialogs);
        var profile = new ConnectionProfile { Id = 7, Name = "prod-db", Host = "db01" };
        svc.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        svc.DeletedIds.Should().Equal(7);
        vm.SelectedProfile.Should().BeNull();
    }

    // ---- Selection re-pointing (was: detail pane showed stale count/last-used) ----

    [Fact]
    public async Task LoadAsync_Repoints_Selection_At_The_Freshly_Loaded_Instance()
    {
        var svc = new FakeConnectionService();
        var vm = new ConnectionListViewModel(svc, new FakeDialogService());
        var fresh = new ConnectionProfile { Id = 3, Name = "app", Host = "app01", ConnectionCount = 5 };
        svc.Profiles.Add(fresh);

        // The UI still holds the pre-reload object (same identity, stale values).
        vm.SelectedProfile = new ConnectionProfile { Id = 3, Name = "app", Host = "app01", ConnectionCount = 4 };

        await vm.LoadAsync();

        vm.SelectedProfile.Should().BeSameAs(fresh,
            "bindings only see new values when the selected instance is swapped");
    }

    [Fact]
    public async Task LoadAsync_Clears_Selection_When_The_Profile_No_Longer_Exists()
    {
        var svc = new FakeConnectionService();
        var vm = new ConnectionListViewModel(svc, new FakeDialogService());
        vm.SelectedProfile = new ConnectionProfile { Id = 99, Name = "gone", Host = "gone" };

        await vm.LoadAsync();

        vm.SelectedProfile.Should().BeNull();
    }

    // ---- Editor validation (was: NameError/HostError could never appear) ----

    [Fact]
    public void New_Editor_Shows_No_Errors_Until_The_User_Touches_A_Field()
    {
        var vm = new ConnectionEditorViewModel(
            new FakeConnectionService(), new FakeEncryptionService(), new FakeRdpLauncher());

        vm.NameError.Should().BeEmpty("a pristine form must not open covered in errors");
        vm.HostError.Should().BeEmpty();
    }

    [Fact]
    public void Emptying_A_Required_Field_Sets_And_Clearing_Removes_The_Error()
    {
        var vm = new ConnectionEditorViewModel(
            new FakeConnectionService(), new FakeEncryptionService(), new FakeRdpLauncher());

        vm.Name = "server";
        vm.NameError.Should().BeEmpty();
        vm.Name = "   ";
        vm.NameError.Should().NotBeEmpty("the disabled Save button needs an explanation");
        vm.Name = "server";
        vm.NameError.Should().BeEmpty();

        vm.Host = "h";
        vm.Host = "";
        vm.HostError.Should().NotBeEmpty();
    }

    // ---- ColorDepths binding (was: static property invisible to WPF path binding) ----

    [Fact]
    public void ColorDepths_Is_An_Instance_Property_So_The_Binding_Resolves()
    {
        var prop = typeof(ConnectionEditorViewModel).GetProperty(nameof(ConnectionEditorViewModel.ColorDepths));
        prop.Should().NotBeNull();
        prop!.GetMethod!.IsStatic.Should().BeFalse(
            "WPF resolves {Binding ColorDepths} against the instance; a static property leaves the ComboBox empty");

        var vm = new ConnectionEditorViewModel(
            new FakeConnectionService(), new FakeEncryptionService(), new FakeRdpLauncher());
        vm.ColorDepths.Should().Equal("15", "16", "24", "32");
    }
}
