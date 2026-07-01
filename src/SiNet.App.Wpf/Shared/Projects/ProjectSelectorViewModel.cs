using System.Collections.ObjectModel;
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
    private readonly IProjectQueryService _projectQuery;
    private readonly ICurrentProjectContext _currentProject;

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
    {
        _projectQuery = projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));

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

    /// <summary>Free-text search across number / name / place / company. Re-queries on change.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                _ = LoadAsync();
            }
        }
    }

    /// <summary>The selected Job Type filter, or <see langword="null"/> for all. Re-queries on change.</summary>
    public string? SelectedJobType
    {
        get => _selectedJobType;
        set
        {
            if (SetField(ref _selectedJobType, value))
            {
                _ = LoadAsync();
            }
        }
    }

    /// <summary>The selected Status filter, or <see langword="null"/> for all. Re-queries on change.</summary>
    public string? SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetField(ref _selectedStatus, value))
            {
                _ = LoadAsync();
            }
        }
    }

    /// <summary>When <see langword="true"/>, closed/inactive projects are included. Re-queries on change.</summary>
    public bool IncludeClosed
    {
        get => _includeClosed;
        set
        {
            if (SetField(ref _includeClosed, value))
            {
                _ = LoadAsync();
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

    /// <summary>True while a load is in progress (drives a busy indicator / disables inputs).</summary>
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
    /// Loads (or reloads) the project list applying the current filters. Safe to call repeatedly; the
    /// last call wins for the displayed collection.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var query = new ProjectSearchQuery(
                SearchText: string.IsNullOrWhiteSpace(_searchText) ? null : _searchText,
                JobType: _selectedJobType,
                Status: _selectedStatus,
                AssignedUserId: null,
                IncludeClosed: _includeClosed);

            var results = await _projectQuery.SearchProjectsAsync(query, cancellationToken).ConfigureAwait(true);

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
        }
        finally
        {
            IsBusy = false;
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

    /// <summary>Unsubscribes from the shared context to avoid leaks when the host view is closed.</summary>
    public void Dispose() => _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;
}
