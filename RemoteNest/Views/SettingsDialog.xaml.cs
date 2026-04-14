using System.Windows;
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
        _isLoading = false;
    }

    private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (AutoStartToggle.IsOn)
            AutoStartService.Enable();
        else
            AutoStartService.Disable();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
