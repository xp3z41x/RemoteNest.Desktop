using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteNest.Localization;
using RemoteNest.Models;
using RemoteNest.Services;

namespace RemoteNest.ViewModels;

/// <summary>
/// ViewModel for the connection editor dialog (create/edit profile).
///
/// VM ⇄ model mapping is reflection-driven over matching property names (the same
/// convention the persistence layer uses), so a new profile setting only needs a
/// matching observable property here — a parity test enforces coverage. Password is
/// deliberately VM-only (the model stores DPAPI ciphertext).
/// </summary>
public partial class ConnectionEditorViewModel : ObservableObject
{
    /// <summary>Model properties that intentionally have no VM counterpart.</summary>
    internal static readonly string[] ModelPropertiesWithoutVmCounterpart =
    [
        nameof(ConnectionProfile.Id),
        nameof(ConnectionProfile.EncryptedPassword), // VM exposes plain Password instead
        nameof(ConnectionProfile.CreatedAt),
        nameof(ConnectionProfile.LastConnectedAt),
        nameof(ConnectionProfile.ConnectionCount)
    ];

    /// <summary>Fields NOT overwritten by "Copy settings from…" — they identify the profile.</summary>
    internal static readonly string[] IdentityProperties =
    [
        nameof(ConnectionProfile.Name),
        nameof(ConnectionProfile.Group),
        nameof(ConnectionProfile.Host),
        nameof(ConnectionProfile.Port),
        nameof(ConnectionProfile.Username),
        nameof(ConnectionProfile.Domain),
        nameof(ConnectionProfile.Notes),
        nameof(ConnectionProfile.AutoConnectOnStartup)
    ];

    private static readonly PropertyInfo[] MappableModelProperties = typeof(ConnectionProfile)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && !ModelPropertiesWithoutVmCounterpart.Contains(p.Name))
        .ToArray();

    private readonly IConnectionService _connectionService;
    private readonly IEncryptionService _encryptionService;
    private readonly IRdpLauncherService _rdpLauncher;

    private int _profileId;
    private bool _isNew;

    // ---- General tab ----
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
    [ObservableProperty] private bool _autoConnectOnStartup;

    // Validation errors
    [ObservableProperty] private string _nameError = string.Empty;
    [ObservableProperty] private string _hostError = string.Empty;

    // ---- Display tab ----
    [ObservableProperty] private int _screenWidth = 1920;
    [ObservableProperty] private int _screenHeight = 1080;
    [ObservableProperty] private bool _fullScreen;
    [ObservableProperty] private string _colorDepth = "32";
    [ObservableProperty] private bool _useMultimon;
    [ObservableProperty] private string _selectedMonitors = string.Empty;
    [ObservableProperty] private bool _dynamicResolution = true;
    [ObservableProperty] private bool _smartSizing;
    [ObservableProperty] private int _desktopScaleFactor;

    // ---- Local resources tab ----
    [ObservableProperty] private int _audioPlaybackMode;
    [ObservableProperty] private bool _audioCaptureMode;
    [ObservableProperty] private int _audioQualityMode;
    [ObservableProperty] private bool _redirectClipboard = true;
    [ObservableProperty] private bool _redirectDrives;
    [ObservableProperty] private string _drivesToRedirect = string.Empty;
    [ObservableProperty] private bool _redirectPrinters;
    [ObservableProperty] private bool _redirectComPorts;
    [ObservableProperty] private bool _redirectSmartCards = true;
    [ObservableProperty] private bool _redirectWebAuthn = true;
    [ObservableProperty] private bool _redirectLocation;
    [ObservableProperty] private string _camerasToRedirect = string.Empty;
    [ObservableProperty] private string _pnpDevicesToRedirect = string.Empty;
    [ObservableProperty] private string _usbDevicesToRedirect = string.Empty;
    [ObservableProperty] private int _keyboardHook = 2;

    // ---- Experience tab ----
    [ObservableProperty] private bool _networkAutoDetect = true;
    [ObservableProperty] private int _connectionType = 7;
    [ObservableProperty] private bool _bandwidthAutoDetect = true;
    [ObservableProperty] private bool _compression = true;
    [ObservableProperty] private bool _bitmapCachePersist = true;
    [ObservableProperty] private bool _disableWallpaper;
    [ObservableProperty] private bool _disableFullWindowDrag;
    [ObservableProperty] private bool _disableMenuAnims;
    [ObservableProperty] private bool _disableThemes;
    [ObservableProperty] private bool _allowFontSmoothing;
    [ObservableProperty] private bool _allowDesktopComposition;
    [ObservableProperty] private bool _videoPlaybackMode = true;

    // ---- Gateway tab ----
    [ObservableProperty] private string _gatewayHostname = string.Empty;
    [ObservableProperty] private int _gatewayUsageMethod = 2;
    [ObservableProperty] private int _gatewayCredentialsSource;
    [ObservableProperty] private bool _gatewayPromptCredentialOnce = true;
    [ObservableProperty] private string _loadBalanceInfo = string.Empty;
    [ObservableProperty] private string _pcb = string.Empty;
    [ObservableProperty] private string _kdcProxyName = string.Empty;
    [ObservableProperty] private bool _enableRdsAadAuth;

    // ---- Advanced tab ----
    [ObservableProperty] private bool _administrativeSession;
    [ObservableProperty] private bool _autoReconnect = true;
    [ObservableProperty] private int _autoReconnectMaxRetries = 20;
    [ObservableProperty] private bool _displayConnectionBar = true;
    [ObservableProperty] private bool _pinConnectionBar = true;
    [ObservableProperty] private bool _publicMode;
    [ObservableProperty] private int _authenticationLevel = 2;
    [ObservableProperty] private bool _useNetworkLevelAuth = true;
    [ObservableProperty] private bool _restrictedAdmin;
    [ObservableProperty] private bool _remoteGuard;
    [ObservableProperty] private bool _promptForCredentials;
    [ObservableProperty] private bool _remoteAppMode;
    [ObservableProperty] private string _remoteAppProgram = string.Empty;
    [ObservableProperty] private string _remoteAppName = string.Empty;
    [ObservableProperty] private string _remoteAppCmdLine = string.Empty;
    [ObservableProperty] private string _extraSettings = string.Empty;

    // ---- Copy settings from another profile ----
    [ObservableProperty] private List<ConnectionProfile> _copySourceProfiles = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyFromProfileCommand))]
    private ConnectionProfile? _selectedCopySource;

    // Available groups for ComboBox
    [ObservableProperty] private List<string> _availableGroups = new();

    // Instance property on purpose: WPF path binding resolves against the DataContext
    // instance and never finds static CLR properties, which left the ComboBox empty.
    public List<string> ColorDepths { get; } = ["15", "16", "24", "32"];

    private static readonly int[] ScaleFactors = [0, 100, 125, 150, 175, 200, 250, 300, 400, 500];

    // Localized option lists for enum-backed ComboBoxes (snapshot at dialog construction;
    // the dialog is modal, so a language switch can't happen while it is open).
    public string[] AudioPlaybackOptions { get; } =
    [
        TranslationSource.Get("AudioPlayLocal"),
        TranslationSource.Get("AudioPlayRemote"),
        TranslationSource.Get("AudioPlayNone")
    ];

    public string[] AudioQualityOptions { get; } =
    [
        TranslationSource.Get("AudioQualityDynamic"),
        TranslationSource.Get("AudioQualityMedium"),
        TranslationSource.Get("AudioQualityHigh")
    ];

    public string[] KeyboardHookOptions { get; } =
    [
        TranslationSource.Get("KeyboardHookLocal"),
        TranslationSource.Get("KeyboardHookRemote"),
        TranslationSource.Get("KeyboardHookFullScreen")
    ];

    public string[] AuthLevelOptions { get; } =
    [
        TranslationSource.Get("AuthLevelConnectAlways"),
        TranslationSource.Get("AuthLevelRefuse"),
        TranslationSource.Get("AuthLevelWarn"),
        TranslationSource.Get("AuthLevelUnspecified")
    ];

    public string[] GatewayUsageOptions { get; } =
    [
        TranslationSource.Get("GatewayUsageAlways"),
        TranslationSource.Get("GatewayUsageBypassLocal")
    ];

    public string[] GatewayCredSourceOptions { get; } =
    [
        TranslationSource.Get("GatewayCredPassword"),
        TranslationSource.Get("GatewayCredSmartcard"),
        TranslationSource.Get("GatewayCredAskLater")
    ];

    public string[] ConnectionTypeOptions { get; } =
    [
        TranslationSource.Get("ConnTypeModem"),
        TranslationSource.Get("ConnTypeBroadbandLow"),
        TranslationSource.Get("ConnTypeSatellite"),
        TranslationSource.Get("ConnTypeBroadbandHigh"),
        TranslationSource.Get("ConnTypeWan"),
        TranslationSource.Get("ConnTypeLan"),
        TranslationSource.Get("ConnTypeAuto")
    ];

    public string[] ScaleFactorOptions { get; } =
        ScaleFactors.Select(f => f == 0 ? TranslationSource.Get("ScaleFactorAuto") : $"{f}%").ToArray();

    // ---- ComboBox index adapters for non-contiguous enum values ----

    /// <summary>gatewayusagemethod 1 (always) / 2 (bypass local) as combo indices 0/1.</summary>
    public int GatewayUsageIndex
    {
        get => GatewayUsageMethod == 1 ? 0 : 1;
        set => GatewayUsageMethod = value == 0 ? 1 : 2;
    }

    /// <summary>gatewaycredentialssource 0/1/4 as combo indices 0/1/2.</summary>
    public int GatewayCredSourceIndex
    {
        get => GatewayCredentialsSource switch { 1 => 1, 4 => 2, _ => 0 };
        set => GatewayCredentialsSource = value switch { 1 => 1, 2 => 4, _ => 0 };
    }

    /// <summary>connection type 1–7 as combo indices 0–6.</summary>
    public int ConnectionTypeIndex
    {
        get => Math.Clamp(ConnectionType, 1, 7) - 1;
        set => ConnectionType = value + 1;
    }

    public int ScaleFactorIndex
    {
        get => Math.Max(0, Array.IndexOf(ScaleFactors, DesktopScaleFactor));
        set => DesktopScaleFactor = value >= 0 && value < ScaleFactors.Length ? ScaleFactors[value] : 0;
    }

    partial void OnGatewayUsageMethodChanged(int value) => OnPropertyChanged(nameof(GatewayUsageIndex));
    partial void OnGatewayCredentialsSourceChanged(int value) => OnPropertyChanged(nameof(GatewayCredSourceIndex));
    partial void OnConnectionTypeChanged(int value) => OnPropertyChanged(nameof(ConnectionTypeIndex));
    partial void OnDesktopScaleFactorChanged(int value) => OnPropertyChanged(nameof(ScaleFactorIndex));

    // ---- Coupling rules (kept here so they are unit-testable without WPF) ----

    // Validate on edit: a pristine blank form shows no errors, but once the user (or a
    // bulk copy) empties a required field, the inline message explains the disabled Save.
    partial void OnNameChanged(string value) =>
        NameError = !_suppressValidation && string.IsNullOrWhiteSpace(value)
            ? TranslationSource.Get("NameRequiredError")
            : string.Empty;

    partial void OnHostChanged(string value) =>
        HostError = !_suppressValidation && string.IsNullOrWhiteSpace(value)
            ? TranslationSource.Get("HostRequiredError")
            : string.Empty;

    partial void OnRestrictedAdminChanged(bool value)
    {
        if (value) RemoteGuard = false;
    }

    partial void OnRemoteGuardChanged(bool value)
    {
        if (value) RestrictedAdmin = false;
    }

    partial void OnUseMultimonChanged(bool value)
    {
        // Multi-monitor only works in full screen — mstsc ignores it otherwise.
        if (value) FullScreen = true;
    }

    partial void OnFullScreenChanged(bool value)
    {
        if (!value) UseMultimon = false;
    }

    /// <summary>True if the dialog result is Save (set by the Save command).</summary>
    public bool DialogResult { get; private set; }

    /// <summary>Event raised when the dialog should close.</summary>
    public event Action? CloseRequested;


    public ConnectionEditorViewModel(IConnectionService connectionService, IEncryptionService encryptionService, IRdpLauncherService rdpLauncher)
    {
        _connectionService = connectionService;
        _encryptionService = encryptionService;
        _rdpLauncher = rdpLauncher;

        // Single source of truth for new-profile defaults: the model's initializers.
        ApplyProfile(new ConnectionProfile(), includeIdentity: true);
    }

    /// <summary>Loads a blank form for creating a new profile.</summary>
    public async Task LoadNewAsync()
    {
        _isNew = true;
        _profileId = 0;
        AvailableGroups = await _connectionService.GetGroupsAsync();
        CopySourceProfiles = await _connectionService.GetAllAsync();
    }

    /// <summary>Loads an existing profile for editing.</summary>
    public async Task LoadExistingAsync(ConnectionProfile profile)
    {
        _isNew = false;
        _profileId = profile.Id;

        ApplyProfile(profile, includeIdentity: true);

        if (!string.IsNullOrEmpty(profile.EncryptedPassword))
        {
            try { Password = _encryptionService.Decrypt(profile.EncryptedPassword); }
            catch { Password = string.Empty; }
        }

        AvailableGroups = await _connectionService.GetGroupsAsync();
        CopySourceProfiles = (await _connectionService.GetAllAsync())
            .Where(p => p.Id != profile.Id)
            .ToList();
    }

    /// <summary>
    /// Copies profile values onto the VM's observable properties by name. With
    /// <paramref name="includeIdentity"/> false, identity fields (name/host/credentials)
    /// are left untouched — the "Copy settings from…" semantics.
    /// </summary>
    /// <summary>True while ApplyProfile seeds the form, so required-field errors don't
    /// appear on a pristine dialog before the user has touched anything.</summary>
    private bool _suppressValidation;

    internal void ApplyProfile(ConnectionProfile source, bool includeIdentity)
    {
        _suppressValidation = true;
        try
        {
            var vmType = GetType();
            foreach (var modelProp in MappableModelProperties)
            {
                if (!includeIdentity && IdentityProperties.Contains(modelProp.Name)) continue;
                var vmProp = vmType.GetProperty(modelProp.Name);
                vmProp?.SetValue(this, modelProp.GetValue(source));
            }
        }
        finally
        {
            _suppressValidation = false;
        }
    }

    /// <summary>Writes the VM's values onto a profile (inverse of <see cref="ApplyProfile"/>).</summary>
    internal void CollectProfile(ConnectionProfile target)
    {
        var vmType = GetType();
        foreach (var modelProp in MappableModelProperties)
        {
            var vmProp = vmType.GetProperty(modelProp.Name);
            if (vmProp is not null)
                modelProp.SetValue(target, vmProp.GetValue(this));
        }
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Host);

    [RelayCommand(CanExecute = nameof(CanSave), AllowConcurrentExecutions = false)]
    private async Task Save()
    {
        var profile = new ConnectionProfile { Id = _profileId };
        CollectProfile(profile);

        profile.Name = profile.Name.Trim();
        profile.Group = profile.Group.Trim();
        profile.Host = profile.Host.Trim();
        profile.Username = profile.Username.Trim();
        profile.Domain = profile.Domain.Trim();
        profile.Notes = profile.Notes.Trim();
        profile.GatewayHostname = profile.GatewayHostname.Trim();
        profile.ExtraSettings = RdpFileBuilder.SanitizeExtraSettings(profile.ExtraSettings);

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

    private bool CanCopyFrom() => SelectedCopySource is not null;

    [RelayCommand(CanExecute = nameof(CanCopyFrom))]
    private void CopyFromProfile()
    {
        if (SelectedCopySource is null) return;
        ApplyProfile(SelectedCopySource, includeIdentity: false);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;

        // Carry the full current configuration (gateway, auth level, RemoteApp…) so the
        // test exercises the same path as a real connect — just windowed, small, and
        // without a stored password (mstsc will prompt).
        var testProfile = new ConnectionProfile();
        CollectProfile(testProfile);
        testProfile.Host = testProfile.Host.Trim();
        testProfile.FullScreen = false;
        testProfile.UseMultimon = false;
        testProfile.ScreenWidth = 1024;
        testProfile.ScreenHeight = 768;
        testProfile.EncryptedPassword = string.Empty;
        testProfile.ExtraSettings = RdpFileBuilder.SanitizeExtraSettings(testProfile.ExtraSettings);

        await _rdpLauncher.LaunchAsync(testProfile);
    }
}
