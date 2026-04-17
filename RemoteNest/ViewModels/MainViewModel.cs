using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteNest.Localization;
using RemoteNest.Models;
using RemoteNest.Services;

namespace RemoteNest.ViewModels;

/// <summary>
/// Root ViewModel for MainWindow — orchestrates list, detail view, and toolbar actions.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IConnectionService _connectionService;
    private readonly IEncryptionService _encryptionService;
    private readonly IRdpLauncherService _rdpLauncher;
    private readonly IDialogService _dialogService;
    private readonly ILogger<MainViewModel> _logger;

    public ConnectionListViewModel ConnectionList { get; }

    [ObservableProperty]
    private string _statusText = TranslationSource.Get("Ready");

    [ObservableProperty]
    private int _totalConnections;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditConnectionCommand))]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private List<ConnectionProfile> _recentConnections = new();

    /// <summary>Event requesting the UI to open the editor dialog.</summary>
    public event Func<ConnectionEditorViewModel, Task<bool>>? EditRequested;

    /// <summary>Event requesting the UI to focus the search box.</summary>
    public event Action? FocusSearchRequested;

    /// <summary>Event requesting the UI to open the settings dialog.</summary>
    public event Action? OpenSettingsRequested;

    public MainViewModel(
        IConnectionService connectionService,
        IEncryptionService encryptionService,
        IRdpLauncherService rdpLauncher,
        IDialogService? dialogService = null,
        ILogger<MainViewModel>? logger = null)
    {
        _connectionService = connectionService;
        _encryptionService = encryptionService;
        _rdpLauncher = rdpLauncher;
        _dialogService = dialogService ?? new DialogService();
        _logger = logger ?? NullLogger<MainViewModel>.Instance;

        ConnectionList = new ConnectionListViewModel(connectionService);
        ConnectionList.PropertyChanged += OnConnectionListPropertyChanged;

        // Refresh localized status text when the user switches UI language at runtime.
        TranslationSource.Instance.PropertyChanged += OnTranslationChanged;
    }

    private void OnTranslationChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only refresh if the current status is one of the localized defaults —
        // avoids overwriting a contextual message like "Connected to X at HH:mm:ss".
        StatusText = TranslationSource.Get("Ready");
    }

    private void OnConnectionListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionListViewModel.SelectedProfile))
            SelectedProfile = ConnectionList.SelectedProfile;
    }

    public void Cleanup()
    {
        ConnectionList.PropertyChanged -= OnConnectionListPropertyChanged;
        TranslationSource.Instance.PropertyChanged -= OnTranslationChanged;
    }

    public void RefreshStatusText()
    {
        StatusText = TranslationSource.Get("Ready");
    }

    public async Task InitializeAsync()
    {
        await ConnectionList.LoadAsync();
        await RefreshStatsAsync();
        await AutoConnectMarkedProfilesAsync();
    }

    private async Task AutoConnectMarkedProfilesAsync()
    {
        var all = await _connectionService.GetAllAsync();
        var toConnect = all.Where(p => p.AutoConnectOnStartup).ToList();
        if (toConnect.Count == 0) return;

        foreach (var profile in toConnect)
        {
            try
            {
                string? plainPassword = null;
                if (!string.IsNullOrEmpty(profile.EncryptedPassword))
                {
                    try { plainPassword = _encryptionService.Decrypt(profile.EncryptedPassword); }
                    catch { /* corrupted DPAPI — skip password */ }
                }

                await _rdpLauncher.LaunchAsync(profile, plainPassword);
                await _connectionService.RecordConnectionAsync(profile.Id);

                // 500ms delay between launches — RdpLauncherService writes Default.rdp;
                // mstsc needs time to read it before we overwrite for the next connection.
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                StatusText = $"{TranslationSource.Get("ConnectionFailed")}: {profile.Name} — {ex.Message}";
                // Continue with next profile — don't abort the whole auto-connect chain.
            }
        }

        // Refresh after all launches to update LastConnectedAt counters in the UI.
        await ConnectionList.LoadAsync();
        await RefreshStatsAsync();
    }

    private async Task RefreshStatsAsync()
    {
        var all = await _connectionService.GetAllAsync();
        TotalConnections = all.Count;
        RecentConnections = all
            .Where(p => p.LastConnectedAt != default)
            .OrderByDescending(p => p.LastConnectedAt)
            .Take(5)
            .ToList();
    }

    private bool CanConnectOrEdit() => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanConnectOrEdit), AllowConcurrentExecutions = false)]
    private async Task Connect()
    {
        var profile = SelectedProfile;
        if (profile is null) return;

        string? plainPassword = null;
        if (!string.IsNullOrEmpty(profile.EncryptedPassword))
        {
            try { plainPassword = _encryptionService.Decrypt(profile.EncryptedPassword); }
            catch (Exception) { /* corrupted DPAPI data — connect without password */ }
        }

        try
        {
            await _rdpLauncher.LaunchAsync(profile, plainPassword);
            await _connectionService.RecordConnectionAsync(profile.Id);
            StatusText = TranslationSource.Format("ConnectedStatus", profile.Name, DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            StatusText = $"{TranslationSource.Get("ConnectionFailed")}: {ex.Message}";
            return;
        }

        await ConnectionList.LoadAsync();
        await RefreshStatsAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ConnectProfile(ConnectionProfile? profile)
    {
        if (profile is null) return;
        SelectedProfile = profile;
        ConnectionList.SelectedProfile = profile;
        await Connect();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task AddConnection()
    {
        var editorVm = new ConnectionEditorViewModel(_connectionService, _encryptionService, _rdpLauncher);
        await editorVm.LoadNewAsync();

        if (EditRequested is not null && await EditRequested(editorVm))
        {
            await ConnectionList.LoadAsync();
            await RefreshStatsAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnectOrEdit), AllowConcurrentExecutions = false)]
    private async Task EditConnection()
    {
        if (SelectedProfile is null) return;

        var profileId = SelectedProfile.Id;
        var editorVm = new ConnectionEditorViewModel(_connectionService, _encryptionService, _rdpLauncher);
        await editorVm.LoadExistingAsync(SelectedProfile);

        if (EditRequested is not null && await EditRequested(editorVm))
        {
            await ConnectionList.LoadAsync();
            await RefreshStatsAsync();
            ConnectionList.SelectById(profileId);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ExportProfiles()
    {
        var filter = $"{TranslationSource.Get("JsonFilter")}|*.json";
        var path = _dialogService.ShowSaveFileDialog(filter, TranslationSource.Get("DefaultExportName"));
        if (path is null) return;

        try
        {
            var json = await _connectionService.ExportToJsonAsync();
            await File.WriteAllTextAsync(path, json);
            StatusText = TranslationSource.Format("ExportedStatus", TotalConnections, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed for {Path}", path);
            _dialogService.ShowError(TranslationSource.Get("ErrorOccurred"), ex.Message);
            StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ImportProfiles()
    {
        var jsonFilter = TranslationSource.Get("JsonFilter");
        var filter = $"{jsonFilter}|*.json|RDP Files (*.rdp)|*.rdp";
        var path = _dialogService.ShowOpenFileDialog(filter);
        if (path is null) return;

        try
        {
            int count;
            if (path.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase))
            {
                await _connectionService.ImportFromRdpFileAsync(path);
                count = 1;
            }
            else
            {
                var json = await File.ReadAllTextAsync(path);
                count = await _connectionService.ImportFromJsonAsync(json);
            }

            await ConnectionList.LoadAsync();
            await RefreshStatsAsync();
            StatusText = TranslationSource.Format("ImportedStatus", count, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed for {Path}", path);
            _dialogService.ShowError(TranslationSource.Get("ErrorOccurred"), ex.Message);
            StatusText = $"{TranslationSource.Get("ErrorOccurred")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void FocusSearch()
    {
        FocusSearchRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
    }
}
