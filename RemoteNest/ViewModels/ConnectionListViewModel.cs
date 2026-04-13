using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteNest.Localization;
using RemoteNest.Models;
using RemoteNest.Services;

namespace RemoteNest.ViewModels;

/// <summary>
/// ViewModel for the left-panel connection list with grouping, search, and selection.
/// </summary>
public partial class ConnectionListViewModel : ObservableObject
{
    private readonly IConnectionService _connectionService;

    [ObservableProperty]
    private ObservableCollection<GroupViewModel> _groups = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSelectedCommand))]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _searchText = string.Empty;

    private List<ConnectionProfile> _allProfiles = new();

    public ConnectionListViewModel(IConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task LoadAsync()
    {
        _allProfiles = await _connectionService.GetAllAsync();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allProfiles
            : _allProfiles.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var grouped = filtered
            .GroupBy(p => string.IsNullOrEmpty(p.Group) ? TranslationSource.Get("Ungrouped") : p.Group)
            .OrderBy(g => g.Key);

        void RebuildGroups()
        {
            Groups.Clear();
            foreach (var group in grouped)
            {
                Groups.Add(new GroupViewModel
                {
                    Name = group.Key,
                    Profiles = new ObservableCollection<ConnectionProfile>(group.ToList())
                });
            }
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            RebuildGroups();
        else
            Application.Current?.Dispatcher.Invoke(RebuildGroups);
    }

    public void SelectById(int id)
    {
        SelectedProfile = _allProfiles.FirstOrDefault(p => p.Id == id);
    }

    private bool HasSelection() => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelected()
    {
        await _connectionService.DeleteAsync(SelectedProfile!.Id);
        SelectedProfile = null;
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateSelected()
    {
        await _connectionService.DuplicateAsync(SelectedProfile!.Id);
        await LoadAsync();
    }
}

public partial class GroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConnectionProfile> _profiles = new();

    [ObservableProperty]
    private bool _isExpanded = true;
}
