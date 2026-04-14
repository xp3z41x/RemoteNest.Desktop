using System.Windows;
using System.Windows.Controls;
using RemoteNest.Localization;
using RemoteNest.Services;

namespace RemoteNest.Views;

public partial class SettingsDialog
{
    private bool _isLoading;

    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        AutoStartToggle.IsOn = AutoStartService.IsEnabled();
        InitializeThemeComboBox();
        _isLoading = false;
    }

    private void InitializeThemeComboBox()
    {
        ThemeComboBox.Items.Clear();
        foreach (var theme in Services.ThemeManager.Supported)
            ThemeComboBox.Items.Add(GetThemeDisplayName(theme));

        var idx = Array.IndexOf(Services.ThemeManager.Supported, Services.ThemeManager.CurrentTheme);
        ThemeComboBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static string GetThemeDisplayName(AppTheme theme) => theme switch
    {
        AppTheme.System => TranslationSource.Get("ThemeSystem"),
        AppTheme.Light => TranslationSource.Get("ThemeLight"),
        AppTheme.DarkBlue => TranslationSource.Get("ThemeDarkBlue"),
        AppTheme.Dark => TranslationSource.Get("ThemeDark"),
        _ => theme.ToString()
    };

    private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (AutoStartToggle.IsOn)
            AutoStartService.Enable();
        else
            AutoStartService.Disable();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ThemeComboBox.SelectedIndex < 0) return;
        var theme = Services.ThemeManager.Supported[ThemeComboBox.SelectedIndex];
        Services.ThemeManager.Apply(theme);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
