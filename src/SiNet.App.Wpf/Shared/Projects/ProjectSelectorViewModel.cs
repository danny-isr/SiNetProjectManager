using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Reusable view model for the shared <c>ProjectSelectorView</c> (see <c>docs/PROJECTS.md</c> §5/§13).
/// <para>
/// It is <b>not Email-specific</b>: any window (Email, ProjectWork, Tasks, Workflow, dialogs) can host
/// the selector. It loads projects through <see cref="IProjectQueryService"/> (fake/in-memory in this
/// slice), binds to <see cref="ProjectSummaryDto"/> only (never EF entities), and applies the parity
/// filters — free text, Job Type, Status, User, include-closed. Selecting a project publishes it to the
/// shared <see cref="ICurrentProjectContext"/>; it never mutates workflow or completes tasks.
/// </para>
/// <para>
/// It also observes <see cref="ICurrentProjectContext.CurrentProjectChanged"/> so an external change to
/// the Current Project (e.g. a task-opened surface syncing the shell) is reflected in the selector
/// without feeding back a redundant update.
/// </para>
/// </summary>
public sealed class ProjectSelectorViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Default cap on rows returned to the selector. Keeps a very large project table from flooding the
    /// (non-virtualized-dropdown) ComboBox and keeps typing responsive. The newest projects (highest
    /// numbers) are kept because results are ordered number-descending before the cap is applied.
    /// </summary>
    public const int DefaultMaxResults = 200;

    /// <summary>
    /// Debounce window applied to filter/search text changes. Typing coalesces into a single query after
    /// this idle period instead of querying on every keystroke, which is what kept the UI thread busy.
    /// </summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    // "Loading projects..." — shown immediately on a filter change so the user sees progress while the
    // (still-enabled) search box remains fully responsive.
    private const string LoadingText = "\u05D8\u05D5\u05E2\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD...";

    private readonly IProjectQueryService _projectQuery;
    private readonly ICurrentProjectContext _currentProject;
    private readonly TimeSpan _debounce;

    // Debounce + last-write-wins state: each queued reload cancels the previous pending one, and each
    // in-flight query has a monotonically increasing id so a slow/stale result never overwrites a newer.
    private CancellationTokenSource? _pendingReloadCts;
    private long _reloadRequestId;

    private string _searchText = string.Empty;
    private string? _selectedJobType;
    private string? _selectedStatus;
    private bool _includeClosed;
    private ProjectSummaryDto? _selectedProject;
    private bool _isBusy;
    private bool _isSyncingFromContext;
    private string _statusMessage = string.Empty;

    /// <summary>Design-time constructor: fakes data and an in-memory context so the control renders standalone.</summary>
    public ProjectSelectorViewModel()
        : this(new FakeProjectQueryService(), new InMemoryCurrentProjectContext())
    {
    }

    /// <summary>Primary constructor: binds to the supplied read port and shared current-project context.</summary>
    public ProjectSelectorViewModel(IProjectQueryService projectQuery, ICurrentProjectContext currentProject)
        : this(projectQuery, currentProject, SearchDebounce)
    {
    }

    /// <summary>
    /// Constructor with an explicit debounce window. Tests pass <see cref="TimeSpan.Zero"/> to make the
    /// coalescing/last-write-wins behavior deterministic without waiting on wall-clock time.
    /// </summary>
    public ProjectSelectorViewModel(
        IProjectQueryService projectQuery,
        ICurrentProjectContext currentProject,
        TimeSpan debounce)
    {
        _projectQuery = projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _debounce = debounce < TimeSpan.Zero ? TimeSpan.Zero : debounce;

        Projects = new ObservableCollection<ProjectSummaryDto>();
        JobTypeOptions = new ObservableCollection<string?> { null };
        StatusOptions = new ObservableCollection<string?> { null };

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());

        _selectedProject = _currentProject.CurrentProject;
        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
    }

    /// <summary>The loaded, filtered, sorted projects bound to the selector list.</summary>
    public ObservableCollection<ProjectSummaryDto> Projects { get; }

    /// <summary>Distinct Job Type filter values (first entry <see langword="null"/> = "all").</summary>
    public ObservableCollection<string?> JobTypeOptions { get; }

    /// <summary>Distinct Status filter values (first entry <see langword="null"/> = "all").</summary>
    public ObservableCollection<string?> StatusOptions { get; }

    /// <summary>Free-text search across number / name / place / company. Debounced re-query on change.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                QueueReload();
            }
        }
    }

    /// <summary>The selected Job Type filter, or <see langword="null"/> for all. Debounced re-query on change.</summary>
    public string? SelectedJobType
    {
        get => _selectedJobType;
        set
        {
            if (SetField(ref _selectedJobType, value))
            {
                QueueReload();
            }
        }
    }

    /// <summary>The selected Status filter, or <see langword="null"/> for all. Debounced re-query on change.</summary>
    public string? SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetField(ref _selectedStatus, value))
            {
                QueueReload();
            }
        }
    }

    /// <summary>When <see langword="true"/>, closed/inactive projects are included. Debounced re-query on change.</summary>
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
    /// The currently selected project. Setting it (via user selection) publishes the value to the
    /// shared <see cref="ICurrentProjectContext"/>. Updates originating from the context itself do not
    /// feed back (guarded by <c>_isSyncingFromContext</c>).
    /// </summary>
    public ProjectSummaryDto? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetField(ref _selectedProject, value) && !_isSyncingFromContext)
            {
                _ = _currentProject.SetCurrentProjectAsync(value);
            }
        }
    }

    /// <summary>
    /// True while a load is in progress. Drives a busy indicator only — it must NOT disable the search
    /// box, because typing has to stay responsive while projects load (the view shows a "loading" message
    /// instead). Only the Refresh command's own re-entrancy guard depends on load state.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Short status/loading message for the selector.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    /// <summary>Reloads the project list from <see cref="IProjectQueryService"/> using the current filters.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Schedules a debounced reload. Called by the filter/search setters on every change: it cancels any
    /// pending (not-yet-started) reload, waits the debounce window, and only then runs the query. Rapid
    /// typing therefore coalesces into a single query instead of one query per keystroke, and a query
    /// already cancelled by a newer keystroke never touches the UI. Fire-and-forget by design; failures
    /// other than cancellation surface through <see cref="StatusMessage"/>.
    /// </summary>
    private void QueueReload()
    {
        Debug.WriteLine(
            $"[PERF] ProjectSelector filter changed (search='{_searchText}', jobType='{_selectedJobType}', status='{_selectedStatus}', includeClosed={_includeClosed}) — scheduling debounced reload in {_debounce.TotalMilliseconds:F0} ms.");

        // Cancel the previous pending/in-flight reload so only the latest keystroke wins.
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pendingReloadCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        // Show immediate feedback WITHOUT blocking input (the search box stays enabled).
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
            // Superseded by a newer keystroke — expected; do nothing.
        }
        finally
        {
            // Clear our slot if we are still the current request.
            if (Interlocked.CompareExchange(ref _pendingReloadCts, null, cts) == cts)
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Loads (or reloads) the project list applying the current filters and the <see cref="DefaultMaxResults"/>
    /// cap. Safe to call repeatedly; a monotonic request id enforces <b>last-write-wins</b> so a slow/stale
    /// result never overwrites a newer one, and the query is fully awaited (no UI-thread blocking).
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _reloadRequestId);
        var sw = Stopwatch.StartNew();

        IsBusy = true;
        try
        {
            var query = new ProjectSearchQuery(
                SearchText: string.IsNullOrWhiteSpace(_searchText) ? null : _searchText,
                JobType: _selectedJobType,
                Status: _selectedStatus,
                AssignedUserId: null,
                IncludeClosed: _includeClosed,
                MaxResults: DefaultMaxResults);

            var results = await _projectQuery.SearchProjectsAsync(query, cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            // Last-write-wins: if a newer request started while we were awaiting, drop these results so
            // they never overwrite the newer ones on the UI.
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

            RefreshFilterOptions(results);

            // Keep the selected project aligned with what is now visible / the shared context.
            SyncSelectionFromContext();

            StatusMessage = results.Count == 0
                ? "\u05D0\u05D9\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD \u05EA\u05D5\u05D0\u05DE\u05D9\u05DD" // "No matching projects"
                : $"{results.Count} \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD"; // "{n} projects"

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
            // Only the latest request owns the busy flag, so an old cancelled load can't clear it early.
            if (Interlocked.Read(ref _reloadRequestId) == requestId)
            {
                IsBusy = false;
            }
        }
    }

    private void RefreshFilterOptions(IReadOnlyList<ProjectSummaryDto> source)
    {
        // Job Type options: null ("all") + distinct non-null values in stable order.
        var jobTypes = source
            .Select(p => p.JobType)
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(j => j, StringComparer.Ordinal)
            .ToList();

        JobTypeOptions.Clear();
        JobTypeOptions.Add(null);
        foreach (var jobType in jobTypes)
        {
            JobTypeOptions.Add(jobType);
        }

        var statuses = source
            .Select(p => p.Status)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        StatusOptions.Clear();
        StatusOptions.Add(null);
        foreach (var status in statuses)
        {
            StatusOptions.Add(status);
        }
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

    /// <summary>Unsubscribes from the shared context and cancels any pending reload to avoid leaks when the host view is closed.</summary>
    public void Dispose()
    {
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;

        var pending = Interlocked.Exchange(ref _pendingReloadCts, null);
        pending?.Cancel();
        pending?.Dispose();
    }
}
