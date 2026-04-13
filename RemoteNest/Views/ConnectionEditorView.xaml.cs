using System.Windows;
using System.Windows.Controls;
using ModernWpf.Controls;
using RemoteNest.ViewModels;

namespace RemoteNest.Views;

public partial class ConnectionEditorView
{
    private readonly ConnectionEditorViewModel _viewModel;
    private readonly Action _closeHandler;
    private bool _isLoading;

    public ConnectionEditorView(ConnectionEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _closeHandler = () => Close();
        _viewModel.CloseRequested += _closeHandler;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // PasswordBox doesn't support binding — sync manually
        PasswordField.Password = _viewModel.Password ?? string.Empty;

        // ModernWPF ToggleSwitch IsOn binding is unreliable — sync manually
        FullScreenToggle.IsOn = _viewModel.FullScreen;
        RedirectClipboardToggle.IsOn = _viewModel.RedirectClipboard;
        RedirectDrivesToggle.IsOn = _viewModel.RedirectDrives;
        RedirectPrintersToggle.IsOn = _viewModel.RedirectPrinters;
        RedirectAudioToggle.IsOn = _viewModel.RedirectAudio;
        UseNlaToggle.IsOn = _viewModel.UseNetworkLevelAuth;

        // ModernWPF NumberBox Value binding can also be unreliable — sync manually
        PortBox.Value = _viewModel.Port;
        ScreenWidthBox.Value = _viewModel.ScreenWidth;
        ScreenHeightBox.Value = _viewModel.ScreenHeight;

        _isLoading = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= _closeHandler;
        base.OnClosed(e);
    }

    private void PasswordField_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.Password = pb.Password;
    }

    // ToggleSwitch event handlers
    private void FullScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            _viewModel.FullScreen = FullScreenToggle.IsOn;
    }

    private void RedirectClipboardToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            _viewModel.RedirectClipboard = RedirectClipboardToggle.IsOn;
    }

    private void RedirectDrivesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            _viewModel.RedirectDrives = RedirectDrivesToggle.IsOn;
    }

    private void RedirectPrintersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            _viewModel.RedirectPrinters = RedirectPrintersToggle.IsOn;
    }

    private void RedirectAudioToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            _viewModel.RedirectAudio = RedirectAudioToggle.IsOn;
    }

    private void UseNlaToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            _viewModel.UseNetworkLevelAuth = UseNlaToggle.IsOn;
    }

    // NumberBox event handlers
    private void PortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isLoading && !double.IsNaN(args.NewValue))
            _viewModel.Port = (int)args.NewValue;
    }

    private void ScreenWidthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isLoading && !double.IsNaN(args.NewValue))
            _viewModel.ScreenWidth = (int)args.NewValue;
    }

    private void ScreenHeightBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isLoading && !double.IsNaN(args.NewValue))
            _viewModel.ScreenHeight = (int)args.NewValue;
    }
}
