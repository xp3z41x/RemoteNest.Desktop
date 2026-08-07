using System.Windows;
using System.Windows.Controls;
using RemoteNest.ViewModels;

namespace RemoteNest.Views;

public partial class ConnectionEditorView
{
    private readonly ConnectionEditorViewModel _viewModel;
    private readonly Action _closeHandler;

    public ConnectionEditorView(ConnectionEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // On short displays (e.g. 1366x768) the declared height would push the
        // Save/Cancel row past the screen edge — clamp to the work area.
        var maxUsable = SystemParameters.WorkArea.Height - 20;
        if (maxUsable > 0 && Height > maxUsable)
        {
            MaxHeight = Math.Max(MinHeight, maxUsable);
            Height = MaxHeight;
        }

        _closeHandler = () => Close();
        _viewModel.CloseRequested += _closeHandler;

        NumberBoxAccessibility.Attach(PortBox, ScreenWidthBox, ScreenHeightBox, RetriesBox);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // PasswordBox doesn't support binding — sync manually.
        PasswordField.Password = _viewModel.Password;
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
}
