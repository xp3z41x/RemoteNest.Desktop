using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
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
        IRdpLauncherService rdpLauncher)
    {
        _connectionService = connectionService;
        _encryptionService = encryptionService;
        _rdpLauncher = rdpLauncher;

        ConnectionList = new ConnectionListViewModel(connectionService);
        ConnectionList.PropertyChanged += OnConnectionListPropertyChanged;
    }

    private void OnConnectionListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionListViewModel.SelectedProfile))
            SelectedProfile = ConnectionList.SelectedProfile;
    }

    public void Cleanup()
    {
        ConnectionList.PropertyChanged -= OnConnectionListPropertyChanged;
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

    [RelayCommand(CanExecute = nameof(CanConnectOrEdit))]
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

    [RelayCommand]
    private async Task ConnectProfile(ConnectionProfile? profile)
    {
        if (profile is null) return;
        SelectedProfile = profile;
        ConnectionList.SelectedProfile = profile;
        await Connect();
    }

    [RelayCommand]
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

    [RelayCommand(CanExecute = nameof(CanConnectOrEdit))]
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

    [RelayCommand]
    private async Task ExportProfiles()
    {
        var dialog = new SaveFileDialog
        {
            Filter = $"{TranslationSource.Get("JsonFilter")}|*.json",
            FileName = TranslationSource.Get("DefaultExportName")
        };

        if (dialog.ShowDialog() == true)
        {
            var json = await _connectionService.ExportToJsonAsync();
            await File.WriteAllTextAsync(dialog.FileName, json);
            StatusText = TranslationSource.Format("ExportedStatus", TotalConnections, Path.GetFileName(dialog.FileName));
        }
    }

    [RelayCommand]
    private async Task ImportProfiles()
    {
        var jsonFilter = TranslationSource.Get("JsonFilter");
        var dialog = new OpenFileDialog
        {
            Filter = $"{jsonFilter}|*.json|RDP Files (*.rdp)|*.rdp"
        };

        if (dialog.ShowDialog() != true) return;

        int count;
        if (dialog.FileName.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase))
        {
            await _connectionService.ImportFromRdpFileAsync(dialog.FileName);
            count = 1;
        }
        else
        {
            var json = await File.ReadAllTextAsync(dialog.FileName);
            count = await _connectionService.ImportFromJsonAsync(json);
        }

        await ConnectionList.LoadAsync();
        await RefreshStatsAsync();
        StatusText = TranslationSource.Format("ImportedStatus", count, Path.GetFileName(dialog.FileName));
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
