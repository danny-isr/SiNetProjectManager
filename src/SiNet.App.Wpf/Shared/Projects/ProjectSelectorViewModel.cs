using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Reusable view model for the shared <c>ProjectSelectorView</c> (see <c>docs/PROJECTS.md</c> §5).
/// Supports UserTyping vs SelectedProjectDisplay editor modes; search source is always the full catalog.
/// </summary>
public sealed class ProjectSelectorViewModel : ObservableObject, IDisposable
{
    public const int DefaultMaxResults = 200;
    public const int ExpandedMaxResults = 1000;
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

    private string _editorText = string.Empty;
    private string _searchQueryText = string.Empty;
    private bool _isUserTyping = true;
    private bool _isUpdatingEditorText;
    private int? _selectedStatusId;
    private int? _selectedJobTypeId;
    private bool _includeClosed;
    private bool _showExpandedResults;
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
        ToggleResultsCommand = new RelayCommand(_ => ToggleResults());
        SelectProjectCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is ProjectSummaryDto project)
                {
                    SelectProject(project);
                }
            });

        _selectedProject = _currentProject.CurrentProject;
        if (_selectedProject is not null)
        {
            ApplySelectedProjectDisplay(_selectedProject);
        }

        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
    }

    public ObservableCollection<ProjectSummaryDto> Projects { get; }

    public ObservableCollection<ProjectFilterOptionDto> JobTypeOptions { get; }

    public ObservableCollection<ProjectFilterOptionDto> StatusOptions { get; }

    public bool IsUserFilterAvailable => false;

    /// <summary>TextBox content — user typing or selected-project display (see docs/PROJECTS.md §5).</summary>
    public string EditorText
    {
        get => _editorText;
        set
        {
            if (!SetField(ref _editorText, value))
            {
                return;
            }

            if (_isUpdatingEditorText)
            {
                return;
            }

            EnterUserTypingMode(value);
        }
    }

    /// <summary>Backward-compatible alias used by tests; maps to editor typing mode.</summary>
    public string SearchText
    {
        get => IsUserTyping ? _searchQueryText : EditorText;
        set => EditorText = value;
    }

    public bool IsUserTyping
    {
        get => _isUserTyping;
        private set => SetField(ref _isUserTyping, value);
    }

    public int EffectiveMaxResults => ShowExpandedResults ? ExpandedMaxResults : DefaultMaxResults;

    public bool ShowExpandedResults
    {
        get => _showExpandedResults;
        set
        {
            if (SetField(ref _showExpandedResults, value))
            {
                OnPropertyChanged(nameof(EffectiveMaxResults));
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

    public string? SelectedProjectDisplay =>
        SelectedProject is null ? null : FormatCompact(SelectedProject);

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

    public ICommand ToggleResultsCommand { get; }

    public ICommand SelectProjectCommand { get; }

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

    public void ToggleResults()
    {
        if (IsResultsOpen)
        {
            IsResultsOpen = false;
            return;
        }

        OpenResults();
    }

    public void OpenResults()
    {
        IsResultsOpen = true;
        if (!IsUserTyping)
        {
            QueueReload();
        }
    }

    public void CloseResults() => IsResultsOpen = false;

    private void EnterUserTypingMode(string queryText)
    {
        IsUserTyping = true;
        _searchQueryText = queryText;
        IsResultsOpen = true;
        QueueReload();
    }

    private void QueueReload()
    {
        Debug.WriteLine(
            $"[PERF] ProjectSelector filter changed (typing={IsUserTyping}, editor='{_editorText}', jobTypeId={_selectedJobTypeId}, statusId={_selectedStatusId}, includeClosed={_includeClosed}, expanded={ShowExpandedResults}) — scheduling debounced reload in {_debounce.TotalMilliseconds:F0} ms.");

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
            var query = BuildSearchQuery();

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

            StatusMessage = FormatStatusMessage(results, query);

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

    private ProjectSearchQuery BuildSearchQuery()
    {
        var searchText = IsUserTyping && !string.IsNullOrWhiteSpace(_searchQueryText)
            ? _searchQueryText.Trim()
            : null;

        return new ProjectSearchQuery(
            SearchText: searchText,
            JobType: ResolveJobTypeDisplayName(),
            Status: ResolveStatusDisplayName(),
            AssignedUserId: null,
            IncludeClosed: _includeClosed,
            MaxResults: EffectiveMaxResults);
    }

    private void SelectProject(ProjectSummaryDto? project)
    {
        if (project is null)
        {
            return;
        }

        SelectedProject = project;

        if (!_isSyncingFromContext)
        {
            _ = _currentProject.SetCurrentProjectAsync(project);
        }

        ApplySelectedProjectDisplay(project);
        IsResultsOpen = false;
    }

    private void ApplySelectedProjectDisplay(ProjectSummaryDto? project)
    {
        IsUserTyping = false;
        _searchQueryText = string.Empty;

        _isUpdatingEditorText = true;
        try
        {
            _editorText = project is null ? string.Empty : FormatCompact(project);
            OnPropertyChanged(nameof(EditorText));
            OnPropertyChanged(nameof(SearchText));
        }
        finally
        {
            _isUpdatingEditorText = false;
        }
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
            ApplySelectedProjectDisplay(match);
        }
        finally
        {
            _isSyncingFromContext = false;
        }
    }

    private static string FormatStatusMessage(IReadOnlyList<ProjectSummaryDto> results, ProjectSearchQuery query)
    {
        if (results.Count == 0)
        {
            return "\u05D0\u05D9\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD \u05EA\u05D5\u05D0\u05DE\u05D9\u05DD";
        }

        var cap = query.MaxResults ?? DefaultMaxResults;
        var hasSearch = !string.IsNullOrWhiteSpace(query.SearchText);
        var atCap = query.MaxResults is int max && max > 0 && results.Count >= max;
        var isExpanded = cap > DefaultMaxResults;

        if (isExpanded && !hasSearch)
        {
            return "\u05DE\u05D5\u05E6\u05D2\u05D5\u05EA \u05EA\u05D5\u05E6\u05D0\u05D5\u05EA \u05DE\u05D5\u05E8\u05D7\u05D1\u05D5\u05EA.";
        }

        if (!hasSearch)
        {
            return $"\u05DE\u05D5\u05E6\u05D2\u05D9\u05DD \u05E2\u05D3 {DefaultMaxResults} \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD. \u05D4\u05E7\u05DC\u05D3 \u05DB\u05D3\u05D9 \u05DC\u05D7\u05E4\u05E9 \u05D1\u05DB\u05DC \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD.";
        }

        if (atCap)
        {
            return $"\u05DE\u05D5\u05E6\u05D2\u05D5\u05EA \u05E2\u05D3 {cap} \u05EA\u05D5\u05E6\u05D0\u05D5\u05EA \u05DE\u05EA\u05D0\u05D9\u05DE\u05D5\u05EA. \u05D4\u05DE\u05E9\u05DA \u05DC\u05D4\u05E7\u05DC\u05D9\u05D3 \u05DB\u05D3\u05D9 \u05DC\u05E6\u05DE\u05E6\u05DD.";
        }

        return $"{results.Count} \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD";
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

    internal static string FormatCompact(ProjectSummaryDto project)
        => $"{project.ProjectNumber} \u2014 {project.ProjectName}";

    public void Dispose()
    {
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;

        var pending = Interlocked.Exchange(ref _pendingReloadCts, null);
        pending?.Cancel();
        pending?.Dispose();
    }
}
