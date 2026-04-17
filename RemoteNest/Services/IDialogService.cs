namespace RemoteNest.Services;

/// <summary>
/// Abstraction over Win32 file dialogs and MessageBox so ViewModels stay
/// testable without a live Dispatcher. All methods return null/false when the
/// user cancels rather than throwing.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a Save File dialog. Returns the chosen path or null if cancelled.</summary>
    string? ShowSaveFileDialog(string filter, string defaultFileName);

    /// <summary>Shows an Open File dialog. Returns the chosen path or null if cancelled.</summary>
    string? ShowOpenFileDialog(string filter);

    /// <summary>Shows an error MessageBox with the given title and message.</summary>
    void ShowError(string title, string message);
}
