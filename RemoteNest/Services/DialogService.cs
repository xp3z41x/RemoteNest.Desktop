using System.Windows;
using Microsoft.Win32;

namespace RemoteNest.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/> using Win32 common dialogs.
/// </summary>
public class DialogService : IDialogService
{
    public string? ShowSaveFileDialog(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenFileDialog(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
