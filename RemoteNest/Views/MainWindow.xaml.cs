using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RemoteNest.Localization;
using RemoteNest.Models;
using RemoteNest.ViewModels;

namespace RemoteNest.Views;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly Action _focusSearchHandler;
    private readonly Action _openSettingsHandler;
    private bool _isLoadingLanguage;

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

        InitializeLanguageComboBox();
    }

    private void OnOpenSettings()
    {
        var dialog = new SettingsDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void InitializeLanguageComboBox()
    {
        _isLoadingLanguage = true;
        LanguageComboBox.Items.Clear();
        for (var i = 0; i < LanguageManager.SupportedLanguages.Length; i++)
            LanguageComboBox.Items.Add(LanguageManager.SupportedLanguageNames[i]);

        var current = LanguageManager.GetCurrentLanguage();
        var idx = Array.IndexOf(LanguageManager.SupportedLanguages, current);
        LanguageComboBox.SelectedIndex = idx >= 0 ? idx : 0;
        _isLoadingLanguage = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingLanguage || LanguageComboBox.SelectedIndex < 0) return;
        var code = LanguageManager.SupportedLanguages[LanguageComboBox.SelectedIndex];
        LanguageManager.SetLanguage(code);
        _viewModel.RefreshStatusText();
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

                if (e.ClickCount == 2)
                {
                    await _viewModel.ConnectCommand.ExecuteAsync(null);
                    e.Handled = true;
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
    }

    private async void RecentItem_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ConnectionProfile profile })
            {
                await _viewModel.ConnectProfileCommand.ExecuteAsync(profile);
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    private Task<bool> OnEditRequested(ConnectionEditorViewModel editorVm)
    {
        var dialog = new ConnectionEditorView(editorVm) { Owner = this };
        dialog.ShowDialog();
        return Task.FromResult(editorVm.DialogResult);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.EditRequested -= OnEditRequested;
        _viewModel.FocusSearchRequested -= _focusSearchHandler;
        _viewModel.OpenSettingsRequested -= _openSettingsHandler;
        _viewModel.Cleanup();
        base.OnClosed(e);
    }
}
