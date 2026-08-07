using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Projects;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Reusable view model for the shared <c>ProjectSelectorView</c> (see <c>docs/PROJECTS.md</c> §5).
/// Embeddable in any host window; depends only on project query/filter ports and
/// <see cref="ICurrentProjectContext"/>. Contains no Email/Shell/Task/Workflow business logic.
/// </summary>
public sealed class ProjectSelectorViewModel : ObservableObject, IDisposable
{
    public const int DefaultMaxResults = 200;
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private const string LoadingText = "\u05D8\u05D5\u05E2\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD...";

    /// <summary>UI-only sentinel label for the job-type filter (no DB filter when selected).</summary>
    public const string AllJobTypesLabel = "\u05DB\u05DC \u05D4\u05E1\u05D5\u05D2\u05D9\u05DD";

    /// <summary>UI-only sentinel label for the status filter (no DB filter when selected).</summary>
    public const string AllStatusesLabel = "\u05DB\u05DC \u05D4\u05E1\u05D8\u05D8\u05D5\u05E1\u05D9\u05DD";

    private static readonly ProjectFilterOptionDto AllJobTypesFilterOption = new(null, AllJobTypesLabel);
    private static readonly ProjectFilterOptionDto AllStatusesFilterOption = new(null, AllStatusesLabel);

    private readonly IProjectQueryService _projectQuery;
    private readonly IProjectFilterOptionsService _filterOptionsService;
    private readonly ICurrentProjectContext _currentProject;
    private readonly IAppSettingsService? _appSettings;
    private readonly bool _persistSelectorWidths;
    private readonly TimeSpan _debounce;

    private CancellationTokenSource? _pendingReloadCts;
    private CancellationTokenSource? _pendingWidthSaveCts;
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
    private bool _suppressOpenResultsOnNextFocus;
    private string _statusMessage = string.Empty;
    private double _controlWidth = UserAppSettingsDefaults.EmailProjectSelectorControlWidth;
    private double _popupWidth = UserAppSettingsDefaults.EmailProjectSelectorPopupWidth;
    private bool _widthsDirty;

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
        TimeSpan debounce = default,
        IAppSettingsService? appSettings = null,
        bool persistSelectorWidths = true)
    {
        _projectQuery = projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
        _filterOptionsService = filterOptionsService ?? throw new ArgumentNullException(nameof(filterOptionsService));
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _debounce = debounce < TimeSpan.Zero ? TimeSpan.Zero : debounce;
        _appSettings = appSettings;
        // Shared across Email / ProjectWork / filing picker / dialogs (same settings.json keys).
        _persistSelectorWidths = persistSelectorWidths
            && appSettings is not null
            && !string.IsNullOrWhiteSpace(appSettings.UserSettingsFilePath);

        Projects = new ObservableCollection<ProjectSummaryDto>();
        StatusOptions = new ObservableCollection<ProjectFilterOptionDto> { AllStatusesFilterOption };
        JobTypeOptions = new ObservableCollection<ProjectFilterOptionDto> { AllJobTypesFilterOption };

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
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => CanClearSelection);

        // Load widths before first bind so reopen shows last sizes (JsonAppSettings is sync I/O).
        TryLoadPersistedWidthsSync();

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

    /// <summary>Width of the search box + toggle (DEV-017).</summary>
    public double ControlWidth
    {
        get => _controlWidth;
        set
        {
            var clamped = Math.Clamp(value, 160, 900);
            if (SetField(ref _controlWidth, clamped))
            {
                _widthsDirty = true;
                QueuePersistWidths();
            }
        }
    }

    /// <summary>Width of the results popup, independent of <see cref="ControlWidth"/> (DEV-017).</summary>
    public double PopupWidth
    {
        get => _popupWidth;
        set
        {
            var clamped = Math.Clamp(value, 160, 900);
            if (SetField(ref _popupWidth, clamped))
            {
                _widthsDirty = true;
                QueuePersistWidths();
            }
        }
    }

    public bool IsUserFilterAvailable => false;

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

    /// <summary>Display cap: 200 by default; null (no cap) when <see cref="ShowExpandedResults"/> is checked.</summary>
    public int? EffectiveMaxResults => ShowExpandedResults ? null : DefaultMaxResults;

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

    /// <summary>Clears the selected project and updates <see cref="ICurrentProjectContext"/> to null.</summary>
    public ICommand ClearSelectionCommand { get; }

    public bool CanClearSelection =>
        _selectedProject is not null || _currentProject.CurrentProject is not null;

    /// <summary>Clears project selection without auto-selecting another project.</summary>
    public void ClearSelection()
    {
        if (!CanClearSelection)
            return;

        SelectedProject = null;

        if (!_isSyncingFromContext)
            _ = _currentProject.SetCurrentProjectAsync(null);

        ApplySelectedProjectDisplay(null);
        IsResultsOpen = false;
        _suppressOpenResultsOnNextFocus = false;
        NotifyClearSelectionStateChanged();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadPersistedWidthsAsync(cancellationToken).ConfigureAwait(true);
        await LoadFilterOptionsAsync(cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Writes current widths immediately (end of drag / dispose). Cancels any pending debounce.</summary>
    public void FlushPersistWidths()
    {
        if (!_persistSelectorWidths || _appSettings is null)
        {
            return;
        }

        CancelPendingWidthSave();
        try
        {
            var current = _appSettings.GetUserAppSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            _appSettings.SaveUserAppSettingsAsync(
                    current with
                    {
                        EmailProjectSelectorControlWidth = ControlWidth,
                        EmailProjectSelectorPopupWidth = PopupWidth,
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _widthsDirty = false;
        }
        catch
        {
            // Persistence failures are non-fatal for the selector.
        }
    }

    private void TryLoadPersistedWidthsSync()
    {
        if (!_persistSelectorWidths || _appSettings is null)
        {
            return;
        }

        try
        {
            var settings = _appSettings.GetUserAppSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            ApplyPersistedWidths(settings);
        }
        catch
        {
            // Keep defaults — layout persistence must not block selector construction.
        }
    }

    private async Task LoadPersistedWidthsAsync(CancellationToken cancellationToken)
    {
        if (!_persistSelectorWidths || _appSettings is null)
        {
            return;
        }

        try
        {
            var settings = await _appSettings.GetUserAppSettingsAsync(cancellationToken).ConfigureAwait(true);
            ApplyPersistedWidths(settings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Keep defaults — layout persistence must not block selector open.
        }
    }

    private void ApplyPersistedWidths(UserAppSettingsDto settings)
    {
        _controlWidth = Math.Clamp(settings.EmailProjectSelectorControlWidth, 160, 900);
        _popupWidth = Math.Clamp(settings.EmailProjectSelectorPopupWidth, 160, 900);
        OnPropertyChanged(nameof(ControlWidth));
        OnPropertyChanged(nameof(PopupWidth));
    }

    private void QueuePersistWidths()
    {
        if (!_persistSelectorWidths || _appSettings is null)
        {
            return;
        }

        CancelPendingWidthSave();
        var cts = new CancellationTokenSource();
        _pendingWidthSaveCts = cts;
        _ = PersistWidthsDebouncedAsync(cts.Token);
    }

    private void CancelPendingWidthSave()
    {
        var pending = Interlocked.Exchange(ref _pendingWidthSaveCts, null);
        if (pending is null)
        {
            return;
        }

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        pending.Dispose();
    }

    private async Task PersistWidthsDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken).ConfigureAwait(true);
            var current = await _appSettings!.GetUserAppSettingsAsync(cancellationToken).ConfigureAwait(true);
            await _appSettings.SaveUserAppSettingsAsync(
                current with
                {
                    EmailProjectSelectorControlWidth = ControlWidth,
                    EmailProjectSelectorPopupWidth = PopupWidth,
                },
                cancellationToken).ConfigureAwait(true);
            _widthsDirty = false;
        }
        catch (OperationCanceledException)
        {
            // superseded by newer drag / FlushPersistWidths
        }
        catch
        {
            // Persistence failures are non-fatal for the selector.
        }
    }

    public async Task LoadFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var previousStatusId = SelectedStatusId;
        var previousJobTypeId = SelectedJobTypeId;

        try
        {
            var options = await _filterOptionsService
                .GetFilterOptionsAsync(cancellationToken)
                .ConfigureAwait(true);

            ApplyFilterOptions(options, previousStatusId, previousJobTypeId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(LoadFilterOptionsAsync));
        }
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
        _suppressOpenResultsOnNextFocus = false;
        IsResultsOpen = true;
        if (!IsUserTyping)
        {
            QueueReload();
        }
    }

    public void HandleSearchBoxGotFocus()
    {
        if (_suppressOpenResultsOnNextFocus)
        {
            _suppressOpenResultsOnNextFocus = false;
            return;
        }

        OpenResults();
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
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(DebouncedReloadAsync));
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
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(LoadAsync));
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
            JobTypeId: SelectedJobTypeId,
            StatusId: SelectedStatusId,
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
        _suppressOpenResultsOnNextFocus = true;
        NotifyClearSelectionStateChanged();
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
        StatusOptions.Add(AllStatusesFilterOption);
        foreach (var status in options.Statuses)
        {
            StatusOptions.Add(status);
        }

        JobTypeOptions.Clear();
        JobTypeOptions.Add(AllJobTypesFilterOption);
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

        NotifyClearSelectionStateChanged();
    }

    private void NotifyClearSelectionStateChanged()
    {
        OnPropertyChanged(nameof(CanClearSelection));
        (ClearSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static string FormatStatusMessage(IReadOnlyList<ProjectSummaryDto> results, ProjectSearchQuery query)
    {
        if (results.Count == 0)
        {
            return "\u05D0\u05D9\u05DF \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD \u05EA\u05D5\u05D0\u05DE\u05D9\u05DD";
        }

        var cap = query.MaxResults;
        var hasSearch = !string.IsNullOrWhiteSpace(query.SearchText);
        var atCap = cap is int max && max > 0 && results.Count >= max;
        var isUncapped = cap is null or <= 0;

        if (isUncapped && !hasSearch)
        {
            return $"\u05DE\u05D5\u05E6\u05D2\u05EA \u05E8\u05E9\u05D9\u05DE\u05D4 \u05DE\u05DC\u05D0\u05D4 ({results.Count} \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD).";
        }

        if (isUncapped && hasSearch)
        {
            return $"{results.Count} \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8\u05D9\u05DD \u05EA\u05D5\u05D0\u05DE\u05D9\u05DD";
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

        // Flush before dispose so close-app after resize does not lose a pending debounce.
        if (_widthsDirty)
        {
            FlushPersistWidths();
        }
        else
        {
            CancelPendingWidthSave();
        }
    }
}
