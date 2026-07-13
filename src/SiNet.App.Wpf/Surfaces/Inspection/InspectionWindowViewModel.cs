using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using AppInspectionNoteRow = SiNet.Application.Abstractions.Inspection.InspectionNoteRow;
using AppInspectionReportRow = SiNet.Application.Abstractions.Inspection.InspectionReportRow;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// View model for <see cref="InspectionWindowView"/>. Browse mode uses
/// <see cref="ICurrentProjectContext"/>; task mode loads the exact report and completes via
/// <see cref="ITaskCompletionService"/>. Create/export go through host adapters (V2 Google).
/// </summary>
public sealed class InspectionWindowViewModel : ObservableObject
{
    private const string NotWiredYet =
        "\u05E4\u05E2\u05D5\u05DC\u05D4 \u05D6\u05D5 \u05D8\u05E8\u05DD \u05D7\u05D5\u05D1\u05E8\u05D4 (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u05D1\u05DC\u05D1\u05D3).";

    private readonly IInspectionWorkspace? _workspace;
    private readonly ITaskCompletionService? _taskCompletion;
    private readonly ITaskCompletionMetadataResolver? _completionMetadata;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly IInspectionNoteCommandService? _noteCommands;
    private readonly IInspectionReportCommandService? _reportCommands;
    private readonly IInspectionNoteAiReviewer? _aiReviewer;
    private readonly IInspectionTemplateCatalog? _templateCatalog;
    private readonly IInspectionReportExportPort? _exportPort;

    private WorkSurfaceContext? _taskContext;
    private int? _browseProjectId;
    private int? _preferredSeriesId;
    private string? _cachedSourceFileUrn;
    private string _activeProjectDisplay = "\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05D3\u05D5\u05D7\u05D5\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA";
    private bool _isPinned;
    private bool _isDocked;
    private bool _isCollapsed;
    private bool _isBusy;
    private bool _reportLoaded;
    private bool _suppressReportSelectionLoad;
    private InspectionTemplateRow? _selectedTemplate;
    private InspectionReportRow? _selectedReport;
    private string? _selectedResultCode;
    private string _statusMessage = "\u05DE\u05D5\u05DB\u05DF";

    /// <summary>Design-time / standalone constructor with fake data.</summary>
    public InspectionWindowViewModel()
        : this(null)
    {
    }

    public InspectionWindowViewModel(
        IInspectionWorkspace? workspace,
        ITaskCompletionService? taskCompletion = null,
        ITaskCompletionMetadataResolver? completionMetadata = null,
        ICurrentUserContext? currentUser = null,
        IInspectionNoteCommandService? noteCommands = null,
        IInspectionNoteAiReviewer? aiReviewer = null,
        ICurrentProjectContext? currentProject = null,
        IInspectionTemplateCatalog? templateCatalog = null,
        IInspectionReportCommandService? reportCommands = null,
        IInspectionReportExportPort? exportPort = null)
    {
        _workspace = workspace;
        _taskCompletion = taskCompletion;
        _completionMetadata = completionMetadata;
        _currentUser = currentUser;
        _noteCommands = noteCommands;
        _aiReviewer = aiReviewer;
        _currentProject = currentProject;
        _templateCatalog = templateCatalog;
        _reportCommands = reportCommands;
        _exportPort = exportPort;

        CreateStrip = new InspectionCreateReportStripViewModel();
        Questionnaire = new InspectionQuestionnaireViewModel();
        NoteEditor = new InspectionNoteEditorViewModel();
        DrawingsPanel = new InspectionDrawingsPanelViewModel();
        ReportCards = new InspectionReportCardsViewModel();
        Metadata = new InspectionMetadataViewModel();

        AvailableTemplates = CreateStrip.AvailableTemplates;
        Reports = ReportCards.Reports;
        Notes = new ObservableCollection<InspectionNoteRow>();
        InspectionTree = Questionnaire.Chapters;
        StatusOptions = new ObservableCollection<string>(
        [
            "\u05E4\u05EA\u05D5\u05D7\u05D4",
            "\u05D3\u05D5\u05E8\u05E9 \u05EA\u05D9\u05E7\u05D5\u05DF",
            "\u05D8\u05D5\u05E4\u05DC",
            "\u05DC\u05D0 \u05E8\u05DC\u05D5\u05D5\u05E0\u05D8\u05D9",
        ]);
        AllowedResultCodes = new ObservableCollection<string>();

        Questionnaire.PropertyChanged += OnQuestionnairePropertyChanged;

        if (_workspace is null)
        {
            foreach (var template in InspectionWindowDesignData.SampleTemplates)
                CreateStrip.AvailableTemplates.Add(template);
            foreach (var report in InspectionWindowDesignData.SampleReports)
                Reports.Add(report);
            foreach (var note in InspectionWindowDesignData.SampleNotes)
                Notes.Add(note);
            foreach (var chapter in InspectionWindowDesignData.BuildSampleTree())
                InspectionTree.Add(chapter);
            SelectedTemplate = AvailableTemplates.FirstOrDefault();
            SelectedReport = Reports.FirstOrDefault();
            _reportLoaded = true;
        }

        ToggleCollapseCommand = new AsyncRelayCommand(() =>
        {
            IsCollapsed = !IsCollapsed;
            return Task.CompletedTask;
        });

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RefreshTemplatesCommand = new AsyncRelayCommand(
            () => RefreshTemplatesAsync(),
            () => !IsBusy && _templateCatalog is not null);
        CompleteTaskCommand = new AsyncRelayCommand(
            async () => { _ = await CompleteFromTaskAsync().ConfigureAwait(true); },
            () => CanCompleteTask);

        CreateReportCommand = new AsyncRelayCommand(CreateReportAsync, () => CanCreateReport);
        MarkResponseReceivedCommand = Stub();
        RepullPlannerResponsesCommand = Stub();
        OpenSourceReportCommand = new AsyncRelayCommand(OpenSourceReportAsync, () => !string.IsNullOrWhiteSpace(_cachedSourceFileUrn));
        UnlockReportCommand = new AsyncRelayCommand(UnlockReportAsync, () => SelectedReport is not null && _reportCommands is not null && !IsBusy);
        ShareReportCommand = new AsyncRelayCommand(ShareReportAsync, () => SelectedReport is not null && _exportPort is not null && !IsBusy);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => SelectedReport is not null && _exportPort is not null && !IsBusy);
        SelectReviewedPlanCommand = Stub();
        AddNoteCommand = Stub();
        MoveNoteUpCommand = Stub();
        MoveNoteDownCommand = Stub();
        ScreenshotPrimaryCommand = Stub();
        OpenNoteLinkedFileCommand = Stub();
        SaveNoteCommand = new AsyncRelayCommand(SaveSelectedNoteAsync, () => NoteEditor.NoteId is > 0 && _noteCommands is not null);
        ReviewNoteAiCommand = new AsyncRelayCommand(ReviewSelectedNoteAiAsync, () => _aiReviewer is not null && !string.IsNullOrWhiteSpace(NoteEditor.NoteText));
    }

    public InspectionCreateReportStripViewModel CreateStrip { get; }

    public InspectionQuestionnaireViewModel Questionnaire { get; }

    public InspectionNoteEditorViewModel NoteEditor { get; }

    public InspectionDrawingsPanelViewModel DrawingsPanel { get; }

    public InspectionReportCardsViewModel ReportCards { get; }

    public InspectionMetadataViewModel Metadata { get; }

    public string Title => "\u05D3\u05D5\u05D7\u05D5\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA";

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    public bool IsDocked
    {
        get => _isDocked;
        set => SetField(ref _isDocked, value);
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetField(ref _isCollapsed, value))
                OnPropertyChanged(nameof(IsExpanded));
        }
    }

    public bool IsExpanded => !_isCollapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ObservableCollection<InspectionTemplateRow> AvailableTemplates { get; }
    public ObservableCollection<InspectionReportRow> Reports { get; }
    public ObservableCollection<InspectionNoteRow> Notes { get; }
    public ObservableCollection<InspectionChapterItem> InspectionTree { get; }
    public ObservableCollection<string> StatusOptions { get; }
    public ObservableCollection<string> AllowedResultCodes { get; }

    public InspectionTemplateRow? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetField(ref _selectedTemplate, value))
            {
                CreateStrip.SelectedTemplate = value;
                (CreateReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public InspectionReportRow? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (!SetField(ref _selectedReport, value))
                return;

            OnPropertyChanged(nameof(HasSelectedReport));
            RaiseCommandStates();

            if (_suppressReportSelectionLoad || value is null)
                return;

            var projectId = ResolveActiveProjectId();
            if (projectId is int pid && _workspace is not null)
            {
                _ = LoadReportContentAsync(pid, value.ReportId);
            }
        }
    }

    public bool HasSelectedReport => _selectedReport is not null;

    public bool HasNoteEditor => NoteEditor.NoteId is > 0;

    public string? SelectedResultCode
    {
        get => _selectedResultCode;
        set
        {
            if (SetField(ref _selectedResultCode, value))
                (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsTaskMode => _taskContext is not null;

    public WorkSurfaceContext? TaskContext => _taskContext;

    public bool CanCompleteTask =>
        IsTaskMode
        && !IsBusy
        && _reportLoaded
        && _taskCompletion is not null
        && _taskContext?.TaskId is > 0;

    private bool CanCreateReport =>
        !IsBusy
        && _reportCommands is not null
        && ResolveActiveProjectId() is > 0
        && !string.IsNullOrWhiteSpace(SelectedTemplate?.Url);

    public ICommand ToggleCollapseCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RefreshTemplatesCommand { get; }
    public ICommand CreateReportCommand { get; }
    public ICommand MarkResponseReceivedCommand { get; }
    public ICommand RepullPlannerResponsesCommand { get; }
    public ICommand OpenSourceReportCommand { get; }
    public ICommand UnlockReportCommand { get; }
    public ICommand ShareReportCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand SelectReviewedPlanCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand MoveNoteUpCommand { get; }
    public ICommand MoveNoteDownCommand { get; }
    public ICommand ScreenshotPrimaryCommand { get; }
    public ICommand OpenNoteLinkedFileCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand ReviewNoteAiCommand { get; }
    public ICommand CompleteTaskCommand { get; }

    /// <summary>
    /// Browse-mode entry: bind current project, load series/reports, refresh templates.
    /// Safe to call when already in task mode (no-ops browse load).
    /// </summary>
    public async Task InitializeBrowseAsync(CancellationToken ct = default)
    {
        if (IsTaskMode)
            return;

        if (_currentProject?.CurrentProject is { } project)
        {
            _browseProjectId = project.ProjectId;
            ActiveProjectDisplay = FormatProjectDisplay(project);
        }
        else
        {
            _browseProjectId = null;
            ActiveProjectDisplay = "\u05DC\u05D0 \u05E0\u05D1\u05D7\u05E8 \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8";
            StatusMessage = "בחר פרויקט נוכחי כדי לטעון דוחות ביקורת.";
        }

        await RefreshTemplatesAsync(ct).ConfigureAwait(true);

        if (_browseProjectId is int projectId)
        {
            await LoadBrowseReportsAsync(projectId, selectReportId: null, ct).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Task-mode entry: validates component key + exact report target, then loads data.
    /// No first/last fallback when the target is missing.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context)
    {
        _ = ApplyContextAsync(context);
    }

    public async Task<bool> ApplyContextAsync(WorkSurfaceContext? context, CancellationToken ct = default)
    {
        _taskContext = context;
        _reportLoaded = false;
        OnPropertyChanged(nameof(IsTaskMode));
        OnPropertyChanged(nameof(TaskContext));
        OnPropertyChanged(nameof(CanCompleteTask));
        RaiseCommandStates();

        if (context is null)
            return false;

        if (!string.Equals(context.ComponentKey, WorkSurfaceComponentKeys.InspectionReport, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = $"Task #{context.TaskId} targets '{context.ComponentKey}', which is not the Inspection surface.";
            return false;
        }

        if (context.PrimaryWorkTargetEntityId is not int reportId || reportId <= 0)
        {
            StatusMessage = $"Task #{context.TaskId} has no inspection report target.";
            ActiveProjectDisplay = context.ProjectId > 0
                ? $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId} (\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId})"
                : $"\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId}";
            return false;
        }

        _browseProjectId = context.ProjectId > 0 ? context.ProjectId : null;
        ActiveProjectDisplay =
            $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId} \u2014 \u05D3\u05D5\u05D7 #{reportId} (\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId})";

        AllowedResultCodes.Clear();
        foreach (var code in context.AllowedResultCodes)
            AllowedResultCodes.Add(code);
        SelectedResultCode = AllowedResultCodes.Count == 1 ? AllowedResultCodes[0] : null;

        await RefreshTemplatesAsync(ct).ConfigureAwait(true);
        return await LoadExactReportAsync(context.ProjectId, reportId, ct).ConfigureAwait(true);
    }

    public async Task<bool> CompleteFromTaskAsync(
        string? completionEventCode = null,
        string? taskResultCode = null,
        CancellationToken cancellationToken = default)
    {
        if (_taskContext is not { } context)
        {
            StatusMessage = "Cannot complete: this surface was not opened from a task.";
            return false;
        }

        if (context.TaskId is not { } taskId || taskId <= 0)
        {
            StatusMessage = "Cannot complete: the work context has no task to complete.";
            return false;
        }

        if (_taskCompletion is null)
        {
            StatusMessage = "Cannot complete: ITaskCompletionService is not bound in this host.";
            return false;
        }

        if (!_reportLoaded)
        {
            StatusMessage = "Cannot complete: the inspection report was not loaded.";
            return false;
        }

        var actingUserId = context.ActingUserId ?? _currentUser?.UserId;
        if (actingUserId is not > 0)
        {
            StatusMessage = "Cannot complete: acting user is unknown.";
            return false;
        }

        if (!TryResolveResultCode(context, taskResultCode ?? SelectedResultCode, out var resolvedResultCode, out var resultMessage))
        {
            StatusMessage = resultMessage ?? "Cannot complete: result code could not be resolved.";
            return false;
        }

        if (!TryResolveEffectiveCompletionEventCode(
                context,
                completionEventCode ?? context.CompletionEventCode,
                resolvedResultCode,
                out var effectiveEventCode,
                out var eventMessage))
        {
            StatusMessage = eventMessage ?? "Cannot complete: completion event could not be resolved.";
            return false;
        }

        IsBusy = true;
        try
        {
            var result = await _taskCompletion
                .CompleteAsync(
                    new CompleteTaskCommand(
                        TaskId: taskId,
                        CompletionEventCode: effectiveEventCode,
                        TaskResultCode: resolvedResultCode,
                        CompletedTaskLinkIds: null,
                        UserId: actingUserId.Value),
                    cancellationToken)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                StatusMessage =
                    $"Could not complete task #{taskId}: {result.ErrorMessage ?? "the completion was rejected."}";
                return false;
            }

            var closed = result.TaskClosed ? " Task closed." : string.Empty;
            var advanced = result.WorkflowAdvanced ? " Workflow advanced." : string.Empty;
            StatusMessage = $"Completed task #{taskId}.{closed}{advanced}";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called from the view when the questionnaire TreeView selection changes.</summary>
    public void OnTreeSelectionChanged(object? selectedItem)
    {
        if (selectedItem is InspectionNoteItem note)
        {
            Questionnaire.SelectedNote = note;
            return;
        }

        Questionnaire.SelectedNote = null;
    }

    private void OnQuestionnairePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectionQuestionnaireViewModel.SelectedNote))
            return;

        NoteEditor.ApplyNote(Questionnaire.SelectedNote);
        OnPropertyChanged(nameof(HasNoteEditor));
        (SaveNoteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ReviewNoteAiCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task RefreshAsync()
    {
        if (IsTaskMode && _taskContext?.PrimaryWorkTargetEntityId is int reportId)
        {
            await LoadExactReportAsync(_taskContext.ProjectId, reportId).ConfigureAwait(true);
            return;
        }

        if (ResolveActiveProjectId() is int projectId)
        {
            var keepId = SelectedReport?.ReportId;
            await LoadBrowseReportsAsync(projectId, keepId).ConfigureAwait(true);
        }
    }

    private async Task RefreshTemplatesAsync(CancellationToken ct = default)
    {
        if (_templateCatalog is null)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "סורק תבניות...";
            var items = await _templateCatalog.ListTemplatesAsync(ct).ConfigureAwait(true);
            AvailableTemplates.Clear();
            foreach (var item in items)
            {
                AvailableTemplates.Add(new InspectionTemplateRow(item.Name, item.SpreadsheetId, item.Url));
            }

            SelectedTemplate = AvailableTemplates.FirstOrDefault();
            StatusMessage = items.Count > 0
                ? $"{items.Count} תבניות נמצאו"
                : "לא נמצאו תבניות — בדוק תיקיית Drive בהגדרות";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינת תבניות: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateReportAsync()
    {
        if (_reportCommands is null)
        {
            StatusMessage = "יצירת דוח אינה זמינה ב-Host זה.";
            return;
        }

        if (ResolveActiveProjectId() is not int projectId)
        {
            StatusMessage = "בחר פרויקט נוכחי לפני יצירת דוח.";
            return;
        }

        var template = SelectedTemplate;
        if (template is null || string.IsNullOrWhiteSpace(template.Url))
        {
            StatusMessage = "יש לבחור תבנית לפני יצירת דוח.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "יוצר דוח ביקורת...";
            var seriesId = _preferredSeriesId;
            if (seriesId is null or <= 0 && _workspace is not null)
            {
                var series = await _workspace.GetSeriesAsync(projectId).ConfigureAwait(true);
                if (series.Count > 0)
                    seriesId = series[0].SeriesId;
            }

            var result = await _reportCommands
                .CreateReportAsync(
                    projectId,
                    template.Url!,
                    seriesId,
                    inspectorName: null,
                    inspectorId: _currentUser?.UserId,
                    spreadsheetId: template.SpreadsheetId)
                .ConfigureAwait(true);

            if (!result.Succeeded || result.ReportId is not int newReportId)
            {
                StatusMessage = result.ErrorMessage ?? "יצירת הדוח נכשלה.";
                return;
            }

            StatusMessage = $"דוח #{newReportId} נוצר — טוען...";
            await LoadBrowseReportsAsync(projectId, newReportId, manageBusy: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה ביצירת דוח: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UnlockReportAsync()
    {
        if (_reportCommands is null || SelectedReport is null)
            return;

        IsBusy = true;
        try
        {
            var result = await _reportCommands
                .UnlockReportAsync(SelectedReport.ReportId)
                .ConfigureAwait(true);
            if (result.Succeeded)
            {
                Metadata.IsLocked = false;
                StatusMessage = "הנעילה שוחררה.";
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "שחרור נעילה נכשל.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportReportAsync()
    {
        if (_exportPort is null || SelectedReport is null)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "מייצא דוח...";
            var result = await _exportPort.ExportAsync(SelectedReport.ReportId).ConfigureAwait(true);
            StatusMessage = result.Succeeded
                ? (string.IsNullOrWhiteSpace(result.SpreadsheetUrl)
                    ? "הדוח יוצא בהצלחה."
                    : $"יוצא: {result.SpreadsheetUrl}")
                : (result.ErrorMessage ?? "ייצוא נכשל.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShareReportAsync()
    {
        if (_exportPort is null || SelectedReport is null)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "משתף דוח...";
            var result = await _exportPort.ShareAsync(SelectedReport.ReportId).ConfigureAwait(true);
            StatusMessage = result.Succeeded
                ? (result.SpreadsheetUrl ?? "הדוח שותף.")
                : (result.ErrorMessage ?? "שיתוף נכשל.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OpenSourceReportAsync()
    {
        if (string.IsNullOrWhiteSpace(_cachedSourceFileUrn))
            return Task.CompletedTask;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _cachedSourceFileUrn,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"לא ניתן לפתוח תבנית: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private async Task LoadBrowseReportsAsync(
        int projectId,
        int? selectReportId,
        CancellationToken ct = default,
        bool manageBusy = true)
    {
        if (_workspace is null)
            return;

        if (manageBusy)
            IsBusy = true;
        try
        {
            var all = new List<InspectionReportRow>();
            var seriesList = await _workspace.GetSeriesAsync(projectId, ct).ConfigureAwait(true);
            _preferredSeriesId = seriesList.Count > 0 ? seriesList[0].SeriesId : null;

            foreach (var series in seriesList)
            {
                var rows = await _workspace.GetReportsAsync(projectId, series.SeriesId, ct).ConfigureAwait(true);
                foreach (var row in rows)
                {
                    all.Add(new InspectionReportRow(
                        row.ReportId,
                        row.ReportNumber,
                        row.InspectorName ?? string.Empty,
                        row.InspectionDate));
                }
            }

            all = all.OrderByDescending(r => r.ReportNumber).ToList();

            _suppressReportSelectionLoad = true;
            try
            {
                Reports.Clear();
                foreach (var row in all)
                    Reports.Add(row);

                var target = selectReportId is int id
                    ? Reports.FirstOrDefault(r => r.ReportId == id)
                    : Reports.FirstOrDefault();
                SelectedReport = target;
            }
            finally
            {
                _suppressReportSelectionLoad = false;
            }

            if (SelectedReport is { } selected)
            {
                await LoadReportContentAsync(projectId, selected.ReportId, ct, manageBusy: false).ConfigureAwait(true);
            }
            else
            {
                ClearReportContent();
                StatusMessage = all.Count == 0
                    ? "אין דוחות בפרויקט — בחר תבנית וצור דוח חדש."
                    : StatusMessage;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינת דוחות: {ex.Message}";
        }
        finally
        {
            if (manageBusy)
                IsBusy = false;
        }
    }

    private async Task<bool> LoadExactReportAsync(int projectId, int reportId, CancellationToken ct = default)
    {
        if (_workspace is null)
        {
            StatusMessage = "Cannot load report: IInspectionWorkspace is not bound in this host.";
            return false;
        }

        IsBusy = true;
        try
        {
            var seriesList = await _workspace.GetSeriesAsync(projectId, ct).ConfigureAwait(true);
            AppInspectionReportRow? found = null;
            var cardRows = new List<InspectionReportRow>();

            foreach (var series in seriesList)
            {
                var rows = await _workspace.GetReportsAsync(projectId, series.SeriesId, ct).ConfigureAwait(true);
                var match = rows.FirstOrDefault(r => r.ReportId == reportId);
                if (match.ReportId == reportId)
                {
                    found = match;
                    _preferredSeriesId = series.SeriesId;
                    foreach (var row in rows)
                    {
                        cardRows.Add(new InspectionReportRow(
                            row.ReportId,
                            row.ReportNumber,
                            row.InspectorName ?? string.Empty,
                            row.InspectionDate));
                    }

                    break;
                }
            }

            if (found is null || found.Value.ReportId != reportId)
            {
                ClearReportContent();
                _suppressReportSelectionLoad = true;
                try
                {
                    Reports.Clear();
                    SelectedReport = null;
                }
                finally
                {
                    _suppressReportSelectionLoad = false;
                }

                StatusMessage =
                    $"Inspection report #{reportId} was not found in project {projectId}. No fallback.";
                OnPropertyChanged(nameof(CanCompleteTask));
                return false;
            }

            _suppressReportSelectionLoad = true;
            try
            {
                Reports.Clear();
                foreach (var row in cardRows)
                    Reports.Add(row);
                SelectedReport = Reports.FirstOrDefault(r => r.ReportId == reportId);
            }
            finally
            {
                _suppressReportSelectionLoad = false;
            }

            await LoadReportContentCoreAsync(reportId, ct).ConfigureAwait(true);
            _reportLoaded = true;
            StatusMessage = IsTaskMode
                ? $"Opened inspection report #{reportId} for task #{_taskContext?.TaskId}."
                : $"נפתח דוח #{reportId}.";
            OnPropertyChanged(nameof(CanCompleteTask));
            RaiseCommandStates();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load inspection report #{reportId}: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadReportContentAsync(
        int projectId,
        int reportId,
        CancellationToken ct = default,
        bool manageBusy = true)
    {
        if (_workspace is null)
            return;

        if (manageBusy)
            IsBusy = true;
        try
        {
            await LoadReportContentCoreAsync(reportId, ct).ConfigureAwait(true);
            _reportLoaded = true;
            StatusMessage = $"נפתח דוח #{reportId}.";
            OnPropertyChanged(nameof(CanCompleteTask));
            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינת דוח #{reportId}: {ex.Message}";
            _reportLoaded = false;
        }
        finally
        {
            if (manageBusy)
                IsBusy = false;
        }
    }

    private async Task LoadReportContentCoreAsync(int reportId, CancellationToken ct)
    {
        Notes.Clear();
        NoteEditor.ApplyNote(null);
        OnPropertyChanged(nameof(HasNoteEditor));

        var notes = await _workspace!.GetNotesAsync(reportId, ct).ConfigureAwait(true);
        foreach (var note in notes)
        {
            Notes.Add(new InspectionNoteRow(
                note.Number ?? note.NoteId.ToString(),
                note.Text ?? string.Empty,
                note.Status ?? string.Empty));
        }

        var tree = await _workspace.GetQuestionnaireTreeAsync(reportId, ct).ConfigureAwait(true);
        Questionnaire.ReplaceTree(InspectionQuestionnaireViewModel.MapFromWorkspace(tree));

        var detail = await _workspace.GetReportDetailAsync(reportId, ct).ConfigureAwait(true);
        Metadata.ApplyDetail(detail);
        _cachedSourceFileUrn = detail?.SourceFileUrn;
        if (detail?.SeriesId is int sid)
            _preferredSeriesId = sid;

        var reviewed = await _workspace.GetReviewedFilesAsync(reportId, ct).ConfigureAwait(true);
        Metadata.ReplaceReviewedFiles(reviewed);
        var drawings = await _workspace.GetDrawingsAsync(reportId, ct).ConfigureAwait(true);
        DrawingsPanel.Replace(drawings);

        (OpenSourceReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ClearReportContent()
    {
        Notes.Clear();
        Questionnaire.ReplaceTree([]);
        NoteEditor.ApplyNote(null);
        Metadata.ApplyDetail(null);
        Metadata.ReplaceReviewedFiles([]);
        DrawingsPanel.Replace([]);
        _cachedSourceFileUrn = null;
        _reportLoaded = false;
        OnPropertyChanged(nameof(HasNoteEditor));
    }

    private int? ResolveActiveProjectId()
    {
        if (_taskContext?.ProjectId is > 0 and var taskProjectId)
            return taskProjectId;
        if (_browseProjectId is > 0)
            return _browseProjectId;
        return _currentProject?.CurrentProject?.ProjectId is > 0 and var id ? id : null;
    }

    private static string FormatProjectDisplay(ProjectSummaryDto project)
    {
        var number = string.IsNullOrWhiteSpace(project.ProjectNumber) ? null : project.ProjectNumber.Trim();
        var name = string.IsNullOrWhiteSpace(project.ProjectName) ? null : project.ProjectName.Trim();
        if (number is not null && name is not null)
            return $"{number} — {name}";
        return name ?? number ?? $"פרויקט {project.ProjectId}";
    }

    private void RaiseCommandStates()
    {
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RefreshTemplatesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CreateReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (UnlockReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ShareReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ExportReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (OpenSourceReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanCompleteTask));
    }

    private bool TryResolveEffectiveCompletionEventCode(
        WorkSurfaceContext context,
        string? explicitCompletionEventCode,
        string? resolvedResultCode,
        [NotNullWhen(true)] out string? completionEventCode,
        out string? message)
    {
        message = null;

        if (!string.IsNullOrWhiteSpace(explicitCompletionEventCode))
        {
            completionEventCode = explicitCompletionEventCode.Trim();
            return true;
        }

        if (_completionMetadata is not null
            && context.TaskTypeCode is { } taskTypeCode
            && !string.IsNullOrWhiteSpace(taskTypeCode))
        {
            var resolved = _completionMetadata.ResolveCompletionEventCode(taskTypeCode, resolvedResultCode);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                completionEventCode = resolved;
                return true;
            }
        }

        completionEventCode = null;
        message = string.IsNullOrWhiteSpace(resolvedResultCode)
            ? "Cannot complete: no completion event could be resolved for this task. Select a result first."
            : $"Cannot complete: no single completion event maps to result '{resolvedResultCode}' for this task.";
        return false;
    }

    private static bool TryResolveResultCode(
        WorkSurfaceContext context,
        string? requestedResultCode,
        out string? resolvedResultCode,
        out string? message)
    {
        var allowed = context.AllowedResultCodes;
        message = null;

        if (!string.IsNullOrWhiteSpace(requestedResultCode))
        {
            if (allowed.Contains(requestedResultCode, StringComparer.Ordinal))
            {
                resolvedResultCode = requestedResultCode;
                return true;
            }

            resolvedResultCode = null;
            message =
                $"Cannot complete: result '{requestedResultCode}' is not one of the allowed results " +
                $"({string.Join(", ", allowed)}).";
            return false;
        }

        switch (allowed.Count)
        {
            case 0:
                resolvedResultCode = null;
                return true;
            case 1:
                resolvedResultCode = allowed[0];
                return true;
            default:
                resolvedResultCode = null;
                message =
                    "Cannot complete: this task allows multiple results. Select one before completing.";
                return false;
        }
    }

    private async Task SaveSelectedNoteAsync()
    {
        if (_noteCommands is null || NoteEditor.NoteId is not long noteId)
        {
            return;
        }

        var result = await _noteCommands
            .SaveNoteTextAsync(noteId, NoteEditor.NoteText)
            .ConfigureAwait(true);
        StatusMessage = result.Succeeded ? "ההערה נשמרה." : (result.ErrorMessage ?? "שמירת ההערה נכשלה.");
    }

    private async Task ReviewSelectedNoteAiAsync()
    {
        if (_aiReviewer is null)
        {
            StatusMessage = "בדיקת AI אינה זמינה.";
            return;
        }

        NoteEditor.IsAiBusy = true;
        try
        {
            var result = await _aiReviewer.ReviewAsync(NoteEditor.NoteText).ConfigureAwait(true);
            if (result.HasError)
            {
                StatusMessage = result.ErrorMessage ?? "בדיקת AI נכשלה.";
                NoteEditor.ClearAi();
                return;
            }

            NoteEditor.GrammarSuggestion = result.GrammarCorrected;
            NoteEditor.RephraseSuggestion = result.Rephrased;
            StatusMessage = "בדיקת AI הושלמה.";
        }
        finally
        {
            NoteEditor.IsAiBusy = false;
        }
    }

    private AsyncRelayCommand Stub() => new(() =>
    {
        StatusMessage = NotWiredYet;
        return Task.CompletedTask;
    });
}
