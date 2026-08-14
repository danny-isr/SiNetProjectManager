using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Identity;
using SiNet.Application.ProjectWork;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Projects.Dashboard;

/// <summary>
/// ViewModel for the Projects Overview Dashboard — see <c>docs/PROJECTS_DASHBOARD.md</c>.
/// </summary>
public sealed class ProjectsDashboardViewModel : ObservableObject
{
    private const string AllOption = "(הכל)";

    private readonly IProjectDashboardQueryService _dashboardQuery;
    private readonly IProjectFilterOptionsService _filterOptions;
    private readonly ICurrentProjectContext _currentProject;
    private readonly IProjectWorkSurfaceHost? _projectWorkHost;
    private readonly IPlaceCatalogService? _placeCatalog;
    private readonly IProjectEditDialogFactory? _editDialogFactory;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly IAppLogger? _logger;

    private CancellationTokenSource? _loadCts;
    private bool _isBusy;
    private bool _includeClosed;
    private bool _onlyWithOpenWorkflow;
    private bool _onlyWithOpenTasks;
    private string _filterText = string.Empty;
    private ProjectFilterOptionDto? _statusFilter;
    private ProjectFilterOptionDto? _jobTypeFilter;
    private string? _placeFilter = AllOption;
    private DateTime? _startFrom;
    private DateTime? _startTo;
    private DateTime? _createdFrom;
    private DateTime? _createdTo;
    private string _totalCountText = "—";
    private string _activeCountText = "—";
    private string _closedCountText = "—";
    private string _withOpenWorkflowText = "—";
    private string _withoutOpenWorkflowText = "—";
    private string _openTasksSumText = "—";
    private string _lastRefreshText = "—";
    private string _statusMessage = string.Empty;
    private ProjectsDashboardRowVm? _selected;
    private IReadOnlyList<ProjectsDashboardRowVm> _allRows = [];

    public ProjectsDashboardViewModel(
        IProjectDashboardQueryService dashboardQuery,
        IProjectFilterOptionsService filterOptions,
        ICurrentProjectContext currentProject,
        IProjectWorkSurfaceHost? projectWorkHost = null,
        IPlaceCatalogService? placeCatalog = null,
        IProjectEditDialogFactory? editDialogFactory = null,
        IAuthorizationQueryService? authorization = null,
        IAppLogger? logger = null)
    {
        _dashboardQuery = dashboardQuery ?? throw new ArgumentNullException(nameof(dashboardQuery));
        _filterOptions = filterOptions ?? throw new ArgumentNullException(nameof(filterOptions));
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _projectWorkHost = projectWorkHost;
        _placeCatalog = placeCatalog;
        _editDialogFactory = editDialogFactory;
        _authorization = authorization;
        _logger = logger;

        Rows = new ObservableCollection<ProjectsDashboardRowVm>();
        StatusFilterOptions = new ObservableCollection<ProjectFilterOptionDto>();
        JobTypeFilterOptions = new ObservableCollection<ProjectFilterOptionDto>();
        PlaceFilterOptions = new ObservableCollection<string> { AllOption };

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenSelectedCommand = new AsyncRelayCommand(OpenSelectedAsync, () => Selected is not null);
        EditSelectedCommand = new AsyncRelayCommand(EditSelectedAsync, () => Selected is not null);
    }

    public ObservableCollection<ProjectsDashboardRowVm> Rows { get; }
    public ObservableCollection<ProjectFilterOptionDto> StatusFilterOptions { get; }
    public ObservableCollection<ProjectFilterOptionDto> JobTypeFilterOptions { get; }
    public ObservableCollection<string> PlaceFilterOptions { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value ?? string.Empty))
                return;
            ApplyFilters();
        }
    }

    public ProjectFilterOptionDto? StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (!SetField(ref _statusFilter, value))
                return;
            ApplyFilters();
        }
    }

    public ProjectFilterOptionDto? JobTypeFilter
    {
        get => _jobTypeFilter;
        set
        {
            if (!SetField(ref _jobTypeFilter, value))
                return;
            ApplyFilters();
        }
    }

    public string? PlaceFilter
    {
        get => _placeFilter;
        set
        {
            if (!SetField(ref _placeFilter, value))
                return;
            ApplyFilters();
        }
    }

    public bool IncludeClosed
    {
        get => _includeClosed;
        set
        {
            if (!SetField(ref _includeClosed, value))
                return;
            _ = RefreshAsync();
        }
    }

    public bool OnlyWithOpenWorkflow
    {
        get => _onlyWithOpenWorkflow;
        set
        {
            if (!SetField(ref _onlyWithOpenWorkflow, value))
                return;
            ApplyFilters();
        }
    }

    public bool OnlyWithOpenTasks
    {
        get => _onlyWithOpenTasks;
        set
        {
            if (!SetField(ref _onlyWithOpenTasks, value))
                return;
            ApplyFilters();
        }
    }

    public DateTime? StartFrom
    {
        get => _startFrom;
        set
        {
            if (!SetField(ref _startFrom, value))
                return;
            ApplyFilters();
        }
    }

    public DateTime? StartTo
    {
        get => _startTo;
        set
        {
            if (!SetField(ref _startTo, value))
                return;
            ApplyFilters();
        }
    }

    public DateTime? CreatedFrom
    {
        get => _createdFrom;
        set
        {
            if (!SetField(ref _createdFrom, value))
                return;
            ApplyFilters();
        }
    }

    public DateTime? CreatedTo
    {
        get => _createdTo;
        set
        {
            if (!SetField(ref _createdTo, value))
                return;
            ApplyFilters();
        }
    }

    public ProjectsDashboardRowVm? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value))
                return;
            (OpenSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (EditSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string TotalCountText
    {
        get => _totalCountText;
        private set => SetField(ref _totalCountText, value);
    }

    public string ActiveCountText
    {
        get => _activeCountText;
        private set => SetField(ref _activeCountText, value);
    }

    public string ClosedCountText
    {
        get => _closedCountText;
        private set => SetField(ref _closedCountText, value);
    }

    public string WithOpenWorkflowText
    {
        get => _withOpenWorkflowText;
        private set => SetField(ref _withOpenWorkflowText, value);
    }

    public string WithoutOpenWorkflowText
    {
        get => _withoutOpenWorkflowText;
        private set => SetField(ref _withoutOpenWorkflowText, value);
    }

    public string OpenTasksSumText
    {
        get => _openTasksSumText;
        private set => SetField(ref _openTasksSumText, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetField(ref _lastRefreshText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenSelectedCommand { get; }

    public ICommand EditSelectedCommand { get; }

    public async Task LoadAsync()
    {
        await EnsureFilterOptionsAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    internal async Task RefreshAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsBusy = true;
        StatusMessage = "טוען…";
        try
        {
            var rows = await _dashboardQuery
                .GetRowsAsync(new ProjectDashboardQuery(IncludeClosed), ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested)
                return;

            _allRows = rows.Select(r => new ProjectsDashboardRowVm(r)).ToList();
            RebuildPlaceFilters(_allRows);
            ApplyFilters(preserveSelectionId: Selected?.ProjectId);
            LastRefreshText = DateTime.Now.ToString("HH:mm:ss");
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // superseded load
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
            _logger?.Warn($"[ProjectsDashboard] outcome=Failed op=Refresh detail={ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task OpenSelectedAsync()
    {
        if (Selected is null)
            return;

        try
        {
            await _currentProject
                .SetCurrentProjectAsync(Selected.ToSummaryDto(), CancellationToken.None)
                .ConfigureAwait(true);

            if (_projectWorkHost is null)
            {
                StatusMessage = "Current Project עודכן (אין מארח בעבודה 2).";
                return;
            }

            var opened = await _projectWorkHost.TryOpenBrowseAsync(CancellationToken.None)
                .ConfigureAwait(true);
            if (!opened)
            {
                MessageBox.Show(
                    "לא ניתן לפתוח את סביבת העבודה בתוך המעטפת.",
                    "ריכוז פרויקטים",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[ProjectsDashboard] outcome=Failed op=OpenSelected detail={ex.Message}");
            MessageBox.Show(
                $"שגיאה בפתיחת הפרויקט: {ex.Message}",
                "ריכוז פרויקטים",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    internal async Task EditSelectedAsync()
    {
        if (Selected is null)
            return;

        if (_editDialogFactory is null)
        {
            StatusMessage = "דיאלוג עדכון פרויקט אינו זמין.";
            return;
        }

        try
        {
            if (_authorization is not null)
            {
                var allowed = await _authorization
                    .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.ProjectUpdate, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!allowed)
                {
                    MessageBox.Show(
                        "אין הרשאה לעדכון פרויקט (Project.Update).",
                        "ריכוז פרויקטים",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? System.Windows.Application.Current?.MainWindow;
            var result = await _editDialogFactory
                .ShowDialogAsync(owner, Selected.ProjectId, CancellationToken.None)
                .ConfigureAwait(true);
            if (result.Confirmed)
                await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[ProjectsDashboard] outcome=Failed op=EditSelected detail={ex.Message}");
            MessageBox.Show(
                $"שגיאה בפתיחת עדכון פרויקט: {ex.Message}",
                "ריכוז פרויקטים",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task EnsureFilterOptionsAsync()
    {
        try
        {
            var options = await _filterOptions.GetFilterOptionsAsync(CancellationToken.None)
                .ConfigureAwait(true);

            StatusFilterOptions.Clear();
            StatusFilterOptions.Add(new ProjectFilterOptionDto(null, AllOption));
            foreach (var s in options.Statuses)
                StatusFilterOptions.Add(s);
            _statusFilter = StatusFilterOptions[0];
            OnPropertyChanged(nameof(StatusFilter));

            JobTypeFilterOptions.Clear();
            JobTypeFilterOptions.Add(new ProjectFilterOptionDto(null, AllOption));
            foreach (var j in options.JobTypes)
                JobTypeFilterOptions.Add(j);
            _jobTypeFilter = JobTypeFilterOptions[0];
            OnPropertyChanged(nameof(JobTypeFilter));

            if (_placeCatalog is not null)
            {
                var places = await _placeCatalog.ListAsync(CancellationToken.None).ConfigureAwait(true);
                PlaceFilterOptions.Clear();
                PlaceFilterOptions.Add(AllOption);
                foreach (var place in places
                             .Select(p => p.Title)
                             .Where(t => !string.IsNullOrWhiteSpace(t))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                {
                    PlaceFilterOptions.Add(place!);
                }

                _placeFilter = AllOption;
                OnPropertyChanged(nameof(PlaceFilter));
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[ProjectsDashboard] outcome=Failed op=FilterOptions detail={ex.Message}");
        }
    }

    private void RebuildPlaceFilters(IReadOnlyList<ProjectsDashboardRowVm> rows)
    {
        if (_placeCatalog is not null && PlaceFilterOptions.Count > 1)
            return;

        var previous = PlaceFilter;
        var places = rows
            .Select(r => r.PlaceName)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PlaceFilterOptions.Clear();
        PlaceFilterOptions.Add(AllOption);
        foreach (var place in places)
            PlaceFilterOptions.Add(place!);

        if (previous is not null && PlaceFilterOptions.Contains(previous))
            _placeFilter = previous;
        else
            _placeFilter = AllOption;
        OnPropertyChanged(nameof(PlaceFilter));
    }

    private void ApplyFilters(int? preserveSelectionId = null)
    {
        IEnumerable<ProjectsDashboardRowVm> query = _allRows;

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var text = FilterText.Trim();
            query = query.Where(r =>
                Contains(r.ProjectNumber, text)
                || Contains(r.ProjectName, text)
                || Contains(r.PlaceName, text)
                || Contains(r.CompanyName, text)
                || Contains(r.AssignedUserName, text)
                || Contains(r.JobTypesDisplay, text)
                || Contains(r.Status, text)
                || Contains(r.OpenWorkflowSummary, text));
        }

        if (StatusFilter?.Id is int statusId)
            query = query.Where(r => r.StatusId == statusId);

        if (JobTypeFilter?.Id is int jobTypeId)
            query = query.Where(r => r.JobTypeIds.Contains(jobTypeId));

        if (!string.IsNullOrWhiteSpace(PlaceFilter) && PlaceFilter != AllOption)
            query = query.Where(r => string.Equals(r.PlaceName, PlaceFilter, StringComparison.OrdinalIgnoreCase));

        if (StartFrom is { } startFrom)
            query = query.Where(r => r.Start is { } s && s.Date >= startFrom.Date);

        if (StartTo is { } startTo)
            query = query.Where(r => r.Start is { } s && s.Date <= startTo.Date);

        if (CreatedFrom is { } createdFrom)
            query = query.Where(r => r.Created is { } c && c.Date >= createdFrom.Date);

        if (CreatedTo is { } createdTo)
            query = query.Where(r => r.Created is { } c && c.Date <= createdTo.Date);

        if (OnlyWithOpenWorkflow)
            query = query.Where(r => r.OpenWorkflowCount > 0);

        if (OnlyWithOpenTasks)
            query = query.Where(r => r.OpenTaskCount > 0);

        var filtered = query.ToList();

        Rows.Clear();
        foreach (var row in filtered)
            Rows.Add(row);

        ApplySummary(filtered);

        if (preserveSelectionId is int id)
            Selected = Rows.FirstOrDefault(r => r.ProjectId == id);
        else if (Selected is not null && Rows.All(r => r.ProjectId != Selected.ProjectId))
            Selected = null;
    }

    private void ApplySummary(IReadOnlyList<ProjectsDashboardRowVm> filtered)
    {
        TotalCountText = filtered.Count.ToString();
        ActiveCountText = filtered.Count(r => r.IsActive).ToString();
        ClosedCountText = filtered.Count(r => !r.IsActive).ToString();
        WithOpenWorkflowText = filtered.Count(r => r.OpenWorkflowCount > 0).ToString();
        WithoutOpenWorkflowText = filtered.Count(r => r.OpenWorkflowCount == 0).ToString();
        OpenTasksSumText = filtered.Sum(r => r.OpenTaskCount).ToString();
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
