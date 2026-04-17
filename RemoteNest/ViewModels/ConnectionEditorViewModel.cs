using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteNest.Models;
using RemoteNest.Services;

namespace RemoteNest.ViewModels;

/// <summary>
/// ViewModel for the connection editor dialog (create/edit profile).
/// </summary>
public partial class ConnectionEditorViewModel : ObservableObject
{
    private readonly IConnectionService _connectionService;
    private readonly IEncryptionService _encryptionService;
    private readonly IRdpLauncherService _rdpLauncher;

    private int _profileId;
    private bool _isNew;

    // General tab
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty] private string _group = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _host = string.Empty;
    [ObservableProperty] private int _port = 3389;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    // Validation errors
    [ObservableProperty] private string _nameError = string.Empty;
    [ObservableProperty] private string _hostError = string.Empty;

    // Display tab
    [ObservableProperty] private int _screenWidth = 1920;
    [ObservableProperty] private int _screenHeight = 1080;
    [ObservableProperty] private bool _fullScreen;
    [ObservableProperty] private string _colorDepth = "32";

    // Resources tab
    [ObservableProperty] private bool _redirectClipboard = true;
    [ObservableProperty] private bool _redirectDrives;
    [ObservableProperty] private bool _redirectPrinters;
    [ObservableProperty] private bool _redirectAudio = true;
    [ObservableProperty] private bool _useNetworkLevelAuth = true;

    // Auto-connect on app startup
    [ObservableProperty] private bool _autoConnectOnStartup;

    // Available groups for ComboBox
    [ObservableProperty] private List<string> _availableGroups = new();

    public static List<string> ColorDepths => ["15", "16", "24", "32"];

    partial void OnNameChanged(string value) => NameError = string.Empty;
    partial void OnHostChanged(string value) => HostError = string.Empty;

    /// <summary>True if the dialog result is Save (set by the Save command).</summary>
    public bool DialogResult { get; private set; }

    /// <summary>Event raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public ConnectionEditorViewModel(IConnectionService connectionService, IEncryptionService encryptionService, IRdpLauncherService rdpLauncher)
    {
        _connectionService = connectionService;
        _encryptionService = encryptionService;
        _rdpLauncher = rdpLauncher;
    }

    /// <summary>Loads a blank form for creating a new profile.</summary>
    public async Task LoadNewAsync()
    {
        _isNew = true;
        _profileId = 0;
        AvailableGroups = await _connectionService.GetGroupsAsync();
    }

    /// <summary>Loads an existing profile for editing.</summary>
    public async Task LoadExistingAsync(ConnectionProfile profile)
    {
        _isNew = false;
        _profileId = profile.Id;

        Name = profile.Name;
        Group = profile.Group;
        Host = profile.Host;
        Port = profile.Port;
        Username = profile.Username;
        Domain = profile.Domain;
        Notes = profile.Notes;
        ScreenWidth = profile.ScreenWidth;
        ScreenHeight = profile.ScreenHeight;
        FullScreen = profile.FullScreen;
        ColorDepth = profile.ColorDepth;
        RedirectClipboard = profile.RedirectClipboard;
        RedirectDrives = profile.RedirectDrives;
        RedirectPrinters = profile.RedirectPrinters;
        RedirectAudio = profile.RedirectAudio;
        UseNetworkLevelAuth = profile.UseNetworkLevelAuth;
        AutoConnectOnStartup = profile.AutoConnectOnStartup;

        if (!string.IsNullOrEmpty(profile.EncryptedPassword))
        {
            try { Password = _encryptionService.Decrypt(profile.EncryptedPassword); }
            catch { Password = string.Empty; }
        }

        AvailableGroups = await _connectionService.GetGroupsAsync();
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Host);

    [RelayCommand(CanExecute = nameof(CanSave), AllowConcurrentExecutions = false)]
    private async Task Save()
    {
        var profile = new ConnectionProfile { Id = _profileId };

        profile.Name = Name.Trim();
        profile.Group = Group.Trim();
        profile.Host = Host.Trim();
        profile.Port = Port;
        profile.Username = Username.Trim();
        profile.Domain = Domain.Trim();
        profile.Notes = Notes.Trim();
        profile.ScreenWidth = ScreenWidth;
        profile.ScreenHeight = ScreenHeight;
        profile.FullScreen = FullScreen;
        profile.ColorDepth = ColorDepth;
        profile.RedirectClipboard = RedirectClipboard;
        profile.RedirectDrives = RedirectDrives;
        profile.RedirectPrinters = RedirectPrinters;
        profile.RedirectAudio = RedirectAudio;
        profile.UseNetworkLevelAuth = UseNetworkLevelAuth;
        profile.AutoConnectOnStartup = AutoConnectOnStartup;

        profile.EncryptedPassword = string.IsNullOrEmpty(Password)
            ? string.Empty
            : _encryptionService.Encrypt(Password);

        if (_isNew)
            await _connectionService.CreateAsync(profile);
        else
            await _connectionService.UpdateAsync(profile);

        DialogResult = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;

        var testProfile = new ConnectionProfile
        {
            Host = Host.Trim(),
            Port = Port,
            Username = Username.Trim(),
            Domain = Domain.Trim(),
            FullScreen = false,
            ScreenWidth = 1024,
            ScreenHeight = 768,
            ColorDepth = ColorDepth,
            RedirectClipboard = RedirectClipboard,
            UseNetworkLevelAuth = UseNetworkLevelAuth
        };

        // We do NOT pass the password for test — let the user type it in mstsc
        await _rdpLauncher.LaunchAsync(testProfile);
    }
}
