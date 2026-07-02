using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Reusable view model for the shared <c>ProjectSelectorView</c> (see <c>docs/PROJECTS.md</c> §5/§13).
/// Search text, filtered results, and selected project are intentionally separate so async reloads
/// never overwrite what the user typed.
/// </summary>
public sealed class ProjectSelectorViewModel : ObservableObject, IDisposable
{
    public const int DefaultMaxResults = 200;
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private const string LoadingText = "\u05D8\u05D5\u05E2\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD...";
    private const string AllFilterLabel = "\u05D4\u05DB\u05DC";

    private static readonly ProjectFilterOptionDto AllFilterOption = new(null, AllFilterLabel);

    private readonly IProjectQueryService _projectQuery;
    private readonly IProjectFilterOptionsService _filterOptionsService;
    private readonly ICurrentProjectContext _currentProject;
    private readonly TimeSpan _debounce;

    private CancellationTokenSource? _pendingReloadCts;
    private long _reloadRequestId;

    private string _searchText = string.Empty;
    private int? _selectedStatusId;
    private int? _selectedJobTypeId;
    private bool _includeClosed;
    private ProjectSummaryDto? _selectedProject;
    private bool _isBusy;
    private bool _isSyncingFromContext;
    private bool _isResultsOpen;
    private string _statusMessage = string.Empty;

    public ProjectSelectorViewModel()
        : this(new FakeProjectQueryService(), new FakeProjectFilterOptionsService(), new InMemoryCurrentProjectContext())
    {
    }

    public ProjectSelectorViewModel(
        IProjectQueryService projectQuery,
        ICurrentProjectContext currentProject)
        : this(projectQuery, new FakeProjectFilterOptionsService(), currentProject)
    {
    }

    public ProjectSelectorViewModel(
        IProjectQueryService projectQuery,
        ICurrentProjectContext currentProject,
        TimeSpan debounce)
        : this(projectQuery, new FakeProjectFilterOptionsService(), currentProject, debounce)
    {
    }

    public ProjectSelectorViewModel(
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptionsService,
        ICurrentProjectContext currentProject,
        TimeSpan debounce = default)
    {
        _projectQuery = projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
        _filterOptionsService = filterOptionsService ?? throw new ArgumentNullException(nameof(filterOptionsService));
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _debounce = debounce < TimeSpan.Zero ? TimeSpan.Zero : debounce;

        Projects = new ObservableCollection<ProjectSummaryDto>();
        StatusOptions = new ObservableCollection<ProjectFilterOptionDto> { AllFilterOption };
        JobTypeOptions = new ObservableCollection<ProjectFilterOptionDto> { AllFilterOption };

        RefreshCommand = new AsyncRelayCommand(() => InitializeAsync());
        SelectProjectCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is ProjectSummaryDto project)
                {
                    SelectProject(project);
                }
            });

        _selectedProject = _currentProject.CurrentProject;
        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
    }

    public ObservableCollection<ProjectSummaryDto> Projects { get; }

    public ObservableCollection<ProjectFilterOptionDto> JobTypeOptions { get; }

    public ObservableCollection<ProjectFilterOptionDto> StatusOptions { get; }

    /// <summary>User filter is deferred until user semantics are defined — always hidden in the UI.</summary>
    public bool IsUserFilterAvailable => false;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                IsResultsOpen = true;
                QueueReload();
            }
        }
    }

    public int? SelectedJobTypeId
    {
        get => _selectedJobTypeId;
        set
        {
            if (SetField(ref _selectedJobTypeId, value))
            {
                QueueReload();
            }
        }
    }

    public int? SelectedStatusId
    {
        get => _selectedStatusId;
        set
        {
            if (SetField(ref _selectedStatusId, value))
            {
                QueueReload();
            }
        }
    }

    public bool IncludeClosed
    {
        get => _includeClosed;
        set
        {
            if (SetField(ref _includeClosed, value))
            {
                QueueReload();
            }
        }
    }

    /// <summary>
    /// The currently selected project. Updated only by explicit user selection or external context sync.
    /// Never derived from search text.
    /// </summary>
    public ProjectSummaryDto? SelectedProject
    {
        get => _selectedProject;
        private set
        {
            if (SetField(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(SelectedProjectDisplay));
            }
        }
    }

    /// <summary>Formatted label for the explicitly selected project (separate from search text).</summary>
    public string? SelectedProjectDisplay =>
        SelectedProject is null
            ? null
            : FormatProjectLine(SelectedProject);

    public bool IsResultsOpen
    {
        get => _isResultsOpen;
        set => SetField(ref _isResultsOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand SelectProjectCommand { get; }

    /// <summary>Loads filter options and the initial project list. Call after the view is loaded.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadFilterOptionsAsync(cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var previousStatusId = SelectedStatusId;
        var previousJobTypeId = SelectedJobTypeId;

        var options = await _filterOptionsService
            .GetFilterOptionsAsync(cancellationToken)
            .ConfigureAwait(true);

        ApplyFilterOptions(options, previousStatusId, previousJobTypeId);
    }

    private void QueueReload()
    {
        Debug.WriteLine(
            $"[PERF] ProjectSelector filter changed (search='{_searchText}', jobTypeId={_selectedJobTypeId}, statusId={_selectedStatusId}, includeClosed={_includeClosed}) — scheduling debounced reload in {_debounce.TotalMilliseconds:F0} ms.");

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pendingReloadCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        StatusMessage = LoadingText;

        _ = DebouncedReloadAsync(cts);
    }

    private async Task DebouncedReloadAsync(CancellationTokenSource cts)
    {
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await Task.Delay(_debounce, cts.Token).ConfigureAwait(true);
            }

            await LoadAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _pendingReloadCts, null, cts) == cts)
            {
                cts.Dispose();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _reloadRequestId);
        var sw = Stopwatch.StartNew();

        IsBusy = true;
        try
        {
            var query = new ProjectSearchQuery(
                SearchText: string.IsNullOrWhiteSpace(_searchText) ? null : _searchText,
                JobType: ResolveJobTypeDisplayName(),
                Status: ResolveStatusDisplayName(),
                AssignedUserId: null,
                IncludeClosed: _includeClosed,
                MaxResults: DefaultMaxResults);

            var results = await _projectQuery.SearchProjectsAsync(query, cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Read(ref _reloadRequestId) != requestId)
            {
                Debug.WriteLine(
                    $"[PERF] ProjectSelector LoadAsync #{requestId} superseded after {sw.ElapsedMilliseconds} ms (dropped).");
                return;
            }

            Projects.Clear();
            foreach (var project in results)
            {
                Projects.Add(project);
            }

            SyncSelectionFromContext();

            StatusMessage = results.Count == 0
                ? "\u05D0\u05D9\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD \u05EA\u05D5\u05D0\u05DE\u05D9\u05DD"
                : $"{results.Count} \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD";

            Debug.WriteLine(
                $"[PERF] ProjectSelector LoadAsync #{requestId} loaded {results.Count} project(s) in {sw.ElapsedMilliseconds} ms.");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine(
                $"[PERF] ProjectSelector LoadAsync #{requestId} cancelled after {sw.ElapsedMilliseconds} ms.");
            throw;
        }
        finally
        {
            if (Interlocked.Read(ref _reloadRequestId) == requestId)
            {
                IsBusy = false;
            }
        }
    }

    private void SelectProject(ProjectSummaryDto? project)
    {
        if (project is null)
        {
            return;
        }

        if (_isSyncingFromContext)
        {
            SelectedProject = project;
            return;
        }

        SelectedProject = project;
        _ = _currentProject.SetCurrentProjectAsync(project);
        IsResultsOpen = false;
    }

    private void ApplyFilterOptions(
        ProjectFilterOptionsDto options,
        int? previousStatusId,
        int? previousJobTypeId)
    {
        StatusOptions.Clear();
        StatusOptions.Add(AllFilterOption);
        foreach (var status in options.Statuses)
        {
            StatusOptions.Add(status);
        }

        JobTypeOptions.Clear();
        JobTypeOptions.Add(AllFilterOption);
        foreach (var jobType in options.JobTypes)
        {
            JobTypeOptions.Add(jobType);
        }

        SelectedStatusId = previousStatusId is int statusId && StatusOptions.Any(o => o.Id == statusId)
            ? statusId
            : null;

        SelectedJobTypeId = previousJobTypeId is int jobTypeId && JobTypeOptions.Any(o => o.Id == jobTypeId)
            ? jobTypeId
            : null;
    }

    private string? ResolveStatusDisplayName()
    {
        if (SelectedStatusId is not int statusId)
        {
            return null;
        }

        return StatusOptions.FirstOrDefault(o => o.Id == statusId)?.DisplayName;
    }

    private string? ResolveJobTypeDisplayName()
    {
        if (SelectedJobTypeId is not int jobTypeId)
        {
            return null;
        }

        return JobTypeOptions.FirstOrDefault(o => o.Id == jobTypeId)?.DisplayName;
    }

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
        => SyncSelectionFromContext();

    private void SyncSelectionFromContext()
    {
        var current = _currentProject.CurrentProject;
        var match = current is null
            ? null
            : Projects.FirstOrDefault(p => p.ProjectId == current.ProjectId) ?? current;

        if (SameProject(_selectedProject, match))
        {
            return;
        }

        _isSyncingFromContext = true;
        try
        {
            SelectedProject = match;
        }
        finally
        {
            _isSyncingFromContext = false;
        }
    }

    private static bool SameProject(ProjectSummaryDto? a, ProjectSummaryDto? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.ProjectId == b.ProjectId;
    }

    private static string FormatProjectLine(ProjectSummaryDto project)
    {
        var place = string.IsNullOrWhiteSpace(project.PlaceName) ? null : project.PlaceName;
        var company = string.IsNullOrWhiteSpace(project.CompanyName) ? null : project.CompanyName;

        var details = string.Join(
            " · ",
            new[] { place, company }.Where(s => s is not null));

        return string.IsNullOrEmpty(details)
            ? $"{project.ProjectNumber} — {project.ProjectName}"
            : $"{project.ProjectNumber} — {project.ProjectName} · {details}";
    }

    public void Dispose()
    {
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;

        var pending = Interlocked.Exchange(ref _pendingReloadCts, null);
        pending?.Cancel();
        pending?.Dispose();
    }
}
