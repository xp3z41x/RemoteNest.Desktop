using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RemoteNest.Localization;
using RemoteNest.Models;
using RemoteNest.Services;
using RemoteNest.ViewModels;

namespace RemoteNest.Views;

public partial class MainWindow
{
    private static bool _firstFrameLogged;

    private readonly MainViewModel _viewModel;
    private readonly Action _focusSearchHandler;
    private readonly Action _openSettingsHandler;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.EditRequested += OnEditRequested;
        _focusSearchHandler = () => SearchBox.Focus();
        _viewModel.FocusSearchRequested += _focusSearchHandler;
        _openSettingsHandler = OnOpenSettings;
        _viewModel.OpenSettingsRequested += _openSettingsHandler;
        _viewModel.ShadowRequested += OnShadowRequested;
        _viewModel.GoHomeRequested += OnGoHome;
        _viewModel.ConnectionList.PropertyChanged += OnConnectionListChanged;
    }

    /// <summary>
    /// VM-driven selection (reloads re-point at fresh instances, Edit uses SelectById)
    /// never reaches the TreeView on its own — restore the highlight so the tree and the
    /// detail pane stay in agreement. Deferred so item containers exist after a rebuild.
    /// </summary>
    private void OnConnectionListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.ConnectionListViewModel.SelectedProfile)) return;
        if (_viewModel.ConnectionList.SelectedProfile is null) return;
        Dispatcher.BeginInvoke(new Action(SyncTreeSelection),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SyncTreeSelection()
    {
        var profile = _viewModel.ConnectionList.SelectedProfile;
        if (profile is null) return;

        foreach (var group in ConnectionTree.Items)
        {
            if (group is not ViewModels.GroupViewModel groupVm || !groupVm.Profiles.Contains(profile))
                continue;
            if (ConnectionTree.ItemContainerGenerator.ContainerFromItem(group) is not TreeViewItem groupItem)
                return;
            if (groupItem.ItemContainerGenerator.ContainerFromItem(profile) is TreeViewItem leaf
                && !leaf.IsSelected)
            {
                leaf.IsSelected = true;
            }
            return;
        }
    }

    /// <summary>
    /// Returns to the dashboard. The TreeView owns selection, so clearing the VM alone
    /// isn't enough — the selected container has to be deselected too.
    /// </summary>
    private void OnGoHome()
    {
        if (ConnectionTree.SelectedItem is not null
            && ConnectionTree.ItemContainerGenerator.ContainerFromItem(ConnectionTree.SelectedItem)
                is TreeViewItem container)
        {
            container.IsSelected = false;
        }
        else
        {
            // Profiles live in nested containers; walk the groups to find the selected one.
            foreach (var group in ConnectionTree.Items)
            {
                if (ConnectionTree.ItemContainerGenerator.ContainerFromItem(group) is not TreeViewItem groupItem)
                    continue;
                foreach (var child in groupItem.Items)
                {
                    if (groupItem.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem { IsSelected: true } leaf)
                        leaf.IsSelected = false;
                }
            }
        }

        _viewModel.ConnectionList.SelectedProfile = null;
        _viewModel.SelectedProfile = null;
    }

    private void OnOpenSettings()
    {
        // The settings dialog offers to pre-trust the saved hosts when patching the
        // Windows RDP warnings, so it needs a way to ask for them.
        var dialog = new SettingsDialog(_viewModel.GetHostsAsync) { Owner = this };
        dialog.ShowDialog();
    }

    private async void OnShadowRequested(ConnectionProfile profile)
    {
        try
        {
            var dialog = new ShadowSessionDialog { Owner = this };
            AcrylicHelper.Track(dialog);
            if (dialog.ShowDialog() == true)
                await _viewModel.LaunchShadowAsync(profile, dialog.SessionId, dialog.TakeControl, dialog.NoConsent);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    private async void ProfileItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ConnectionProfile profile })
            {
                _viewModel.ConnectionList.SelectedProfile = profile;

                // CanExecute is the concurrency gate: direct ExecuteAsync would bypass
                // AllowConcurrentExecutions=false and a double-click could launch twice.
                if (e.ClickCount == 2 && _viewModel.ConnectCommand.CanExecute(null))
                {
                    e.Handled = true;
                    await _viewModel.ConnectCommand.ExecuteAsync(null);
                }
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    private void ConnectionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ConnectionProfile profile)
            _viewModel.ConnectionList.SelectedProfile = profile;
        else if (e.NewValue is ViewModels.GroupViewModel)
            // A group header is now highlighted; keeping the old profile "selected" would
            // let Delete/Ctrl+E act on something the tree no longer points at.
            _viewModel.ConnectionList.SelectedProfile = null;
        // e.NewValue == null happens transiently while the list rebuilds — keep the
        // selection; SyncTreeSelection restores the highlight when containers return.
    }

    /// <summary>Enter on a selected profile launches it — the tree's primary action.</summary>
    private async void ConnectionTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        try
        {
            if (_viewModel.ConnectCommand.CanExecute(null))
            {
                e.Handled = true;
                await _viewModel.ConnectCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    /// <summary>Right-click selects the profile before its context menu opens, so the
    /// menu commands act on the item under the cursor.</summary>
    private void ProfileTreeItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: ConnectionProfile } item && !item.IsSelected)
            item.IsSelected = true;
    }

    private Task<bool> OnEditRequested(ConnectionEditorViewModel editorVm)
    {
        var dialog = new ConnectionEditorView(editorVm) { Owner = this };
        AcrylicHelper.Track(dialog);
        dialog.ShowDialog();
        return Task.FromResult(editorVm.DialogResult);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // One-line startup diagnostic: real time from process start to first rendered frame.
        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            try
            {
                var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
                Log.Info($"First frame rendered {elapsed.TotalMilliseconds:F0} ms after process start");
            }
            catch { /* diagnostics only */ }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.EditRequested -= OnEditRequested;
        _viewModel.FocusSearchRequested -= _focusSearchHandler;
        _viewModel.OpenSettingsRequested -= _openSettingsHandler;
        _viewModel.ShadowRequested -= OnShadowRequested;
        _viewModel.GoHomeRequested -= OnGoHome;
        _viewModel.ConnectionList.PropertyChanged -= OnConnectionListChanged;
        _viewModel.Cleanup();
        base.OnClosed(e);
    }
}
