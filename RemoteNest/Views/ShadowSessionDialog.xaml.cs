using System.Windows;

namespace RemoteNest.Views;

/// <summary>
/// Collects ephemeral shadow-session parameters (session IDs change constantly,
/// so nothing here is persisted).
/// </summary>
public partial class ShadowSessionDialog
{
    public int SessionId { get; private set; } = 1;
    public bool TakeControl { get; private set; } = true;
    public bool NoConsent { get; private set; }

    public ShadowSessionDialog()
    {
        InitializeComponent();
        NumberBoxAccessibility.Attach(SessionIdBox);
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var value = SessionIdBox.Value;
        SessionId = double.IsNaN(value) ? 1 : (int)value;
        TakeControl = ControlToggle.IsOn;
        NoConsent = NoConsentToggle.IsOn;
        DialogResult = true;
        Close();
    }
}
