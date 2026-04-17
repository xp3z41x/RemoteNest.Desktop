using System.Collections.ObjectModel;
using System.ComponentModel;
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

        // Rebuild groups when the UI language changes so the localized "Ungrouped"
        // key (and any future localized group names) reflect the new culture.
        TranslationSource.Instance.PropertyChanged += OnTranslationChanged;
    }

    private void OnTranslationChanged(object? sender, PropertyChangedEventArgs e) => ApplyFilter();

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

        var ungrouped = TranslationSource.Get("Ungrouped");
        var grouped = filtered
            .GroupBy(p => string.IsNullOrEmpty(p.Group) ? ungrouped : p.Group)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        void RebuildGroups()
        {
            // Preserve IsExpanded state across rebuilds (search filter, language change,
            // profile CRUD). Without this, every reload collapses the tree.
            var prevExpansion = Groups.ToDictionary(g => g.Name, g => g.IsExpanded);

            Groups.Clear();
            foreach (var group in grouped)
            {
                Groups.Add(new GroupViewModel
                {
                    Name = group.Key,
                    Profiles = new ObservableCollection<ConnectionProfile>(group.ToList()),
                    IsExpanded = prevExpansion.TryGetValue(group.Key, out var wasExpanded) ? wasExpanded : true
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
