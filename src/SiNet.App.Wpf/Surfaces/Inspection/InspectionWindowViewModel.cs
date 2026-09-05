using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Identity;
using SiNet.Application.Inspection;
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
    private readonly IInspectionNoteScreenshotHost? _screenshotHost;
    private readonly IInspectionNoteLinkedFileHost? _linkedFileHost;
    private readonly IInspectionFileTreePickerHost? _fileTreePicker;
    private readonly IInspectionReportTaskLinkService? _reportTaskLinks;

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

    // Single-flight + debounce for note status auto-save: bursty StatusText edits must not race
    // into overlapping, uncoalesced writes (last-write-wins data loss). Each note keeps at most one
    // in-flight save; a newer edit cancels the prior one.
    private static readonly TimeSpan StatusSaveDebounce = TimeSpan.FromMilliseconds(300);
    private readonly Dictionary<long, CancellationTokenSource> _statusSaveCts = new();
    private readonly object _statusSaveGate = new();

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
        IInspectionReportExportPort? exportPort = null,
        IInspectionNoteScreenshotHost? screenshotHost = null,
        IInspectionNoteLinkedFileHost? linkedFileHost = null,
        IInspectionFileTreePickerHost? fileTreePicker = null,
        IInspectionReportTaskLinkService? reportTaskLinks = null)
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
        _screenshotHost = screenshotHost;
        _linkedFileHost = linkedFileHost;
        _fileTreePicker = fileTreePicker;
        _reportTaskLinks = reportTaskLinks;

        CreateStrip = new InspectionCreateReportStripViewModel();
        Questionnaire = new InspectionQuestionnaireViewModel();
        NoteEditor = new InspectionNoteEditorViewModel();
        DrawingsPanel = new InspectionDrawingsPanelViewModel();
        ReportCards = new InspectionReportCardsViewModel();
        Metadata = new InspectionMetadataViewModel();

        AvailableTemplates = CreateStrip.AvailableTemplates;
        Reports = ReportCards.Reports;
        Notes = new ObservableCollection<InspectionNoteRow>();
        InspectionTree = Questionnaire.RootItems;
        StatusOptions = new ObservableCollection<InspectionStatusOption>(
            InspectionWindowDesignData.DefaultStatusOptions);
        AllowedResultCodes = new ObservableCollection<string>();

        Questionnaire.PropertyChanged += OnQuestionnairePropertyChanged;
        Metadata.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(InspectionMetadataViewModel.IsLocked))
            {
                OnPropertyChanged(nameof(IsReportEditable));
                OnPropertyChanged(nameof(IsSelectedReportLocked));
                RaiseCommandStates();
            }
        };

        if (_workspace is null)
        {
            foreach (var template in InspectionWindowDesignData.SampleTemplates)
                CreateStrip.AvailableTemplates.Add(template);
            foreach (var report in InspectionWindowDesignData.SampleReports)
                Reports.Add(report);
            foreach (var note in InspectionWindowDesignData.SampleNotes)
                Notes.Add(note);
            Questionnaire.ReplaceTree(InspectionWindowDesignData.BuildSampleTree());
            AttachNoteHandlers();
            AttachGeneralFieldHandlers();
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
        ExportReportCommand = new AsyncRelayCommand(
            ExportReportAsync,
            () => SelectedReport is not null && _exportPort is not null && !IsBusy);
        SelectReviewedPlanCommand = new AsyncRelayCommand(
            SelectReviewedPlansAsync,
            () => SelectedReport is not null
                && _fileTreePicker is not null
                && _reportCommands is not null
                && IsReportEditable
                && !IsBusy);
        AddNoteCommand = new AsyncRelayCommand<InspectionSectionItem>(
            AddNoteToSectionAsync,
            section => section is not null
                && SelectedReport is not null
                && _noteCommands is not null
                && IsReportEditable
                && !IsBusy);
        MoveNoteUpCommand = new AsyncRelayCommand<InspectionNoteItem>(
            note => MoveNoteAsync(note, direction: -1),
            note => CanMoveNote(note, direction: -1));
        MoveNoteDownCommand = new AsyncRelayCommand<InspectionNoteItem>(
            note => MoveNoteAsync(note, direction: 1),
            note => CanMoveNote(note, direction: 1));
        ScreenshotPrimaryCommand = new AsyncRelayCommand<InspectionNoteItem>(
            ScreenshotPrimaryAsync,
            note => note is not null
                && ((note.HasAttachments && !string.IsNullOrWhiteSpace(note.LastAttachmentUrl) && _screenshotHost is not null)
                    || (note.NoteId is > 0 && _screenshotHost is not null && IsReportEditable && !IsBusy)));
        AttachScreenshotCommand = new AsyncRelayCommand<InspectionNoteItem>(
            UploadScreenshotAsync,
            note => note?.NoteId is > 0 && _screenshotHost is not null && IsReportEditable && !IsBusy);
        OpenLastAttachmentCommand = new AsyncRelayCommand<InspectionNoteItem>(
            OpenLastAttachmentAsync,
            note => note is not null
                && note.HasAttachments
                && !string.IsNullOrWhiteSpace(note.LastAttachmentUrl)
                && _screenshotHost is not null
                && !IsBusy);
        OpenNoteLinkedFileCommand = new AsyncRelayCommand<InspectionNoteItem>(
            OpenOrSetLinkedFileAsync,
            note => note is not null
                && SelectedReport is not null
                && !IsBusy
                && (note.HasLinkedFile
                    ? _linkedFileHost is not null
                    : _fileTreePicker is not null && _noteCommands is not null && IsReportEditable));
        SetNoteLinkedFileCommand = new AsyncRelayCommand<InspectionNoteItem>(
            SetNoteLinkedFileAsync,
            note => note?.NoteId is > 0
                && SelectedReport is not null
                && _fileTreePicker is not null
                && _noteCommands is not null
                && IsReportEditable
                && !IsBusy);
        ClearNoteLinkedFileCommand = new AsyncRelayCommand<InspectionNoteItem>(
            ClearNoteLinkedFileAsync,
            note => note is not null
                && note.HasLinkedFile
                && note.NoteId is > 0
                && SelectedReport is not null
                && _noteCommands is not null
                && IsReportEditable
                && !IsBusy);
        SaveNoteCommand = new AsyncRelayCommand(
            SaveSelectedNoteAsync,
            () => Questionnaire.SelectedNote?.NoteId is > 0 && _noteCommands is not null && IsReportEditable);
        ReviewNoteAiCommand = new AsyncRelayCommand(
            ReviewSelectedNoteAiAsync,
            () => _aiReviewer is not null
                && Questionnaire.SelectedNote is not null
                && !string.IsNullOrWhiteSpace(Questionnaire.SelectedNote.NoteText));
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
    public ObservableCollection<object> InspectionTree { get; }
    public ObservableCollection<InspectionStatusOption> StatusOptions { get; }
    public ObservableCollection<string> AllowedResultCodes { get; }

    /// <summary>True when the task allows more than one completion result and the operator must choose.</summary>
    public bool HasMultipleAllowedResultCodes => AllowedResultCodes.Count > 1;

    /// <summary>True when a report is selected and not locked after send.</summary>
    public bool IsReportEditable => HasSelectedReport && !Metadata.IsLocked;

    /// <summary>True when the selected report is locked after send (read-only fill).</summary>
    public bool IsSelectedReportLocked => Metadata.IsLocked;

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

    public bool HasSelectedNote => Questionnaire.SelectedNote is not null;

    public string ValidationSummary => Questionnaire.ValidationSummary;

    public bool HasValidationBlockingExport => HasSelectedReport && !Questionnaire.CanExport;

    public string ExportTooltip => HasValidationBlockingExport
        ? ValidationSummary
        : "ייצא דוח ל-Google Sheets";


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
        && SelectedTemplate is { SpreadsheetId: { Length: > 0 } };

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
    public ICommand AttachScreenshotCommand { get; }
    public ICommand OpenLastAttachmentCommand { get; }
    public ICommand OpenNoteLinkedFileCommand { get; }
    public ICommand SetNoteLinkedFileCommand { get; }
    public ICommand ClearNoteLinkedFileCommand { get; }
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

        if (!WorkSurfaceComponentKeys.IsInspectionReportSurface(context.ComponentKey))
        {
            StatusMessage = $"Task #{context.TaskId} targets '{context.ComponentKey}', which is not the Inspection surface.";
            return false;
        }

        if (context.PrimaryWorkTargetEntityId is not int reportId || reportId <= 0)
        {
            if (!AllowsInspectionReportCreationWhenMissing(context.TaskTypeCode))
            {
                StatusMessage =
                    "המשימה אינה מקושרת לדוח בדיקה קיים ולכן לא ניתן לפתוח אותה מתוך ה־Workflow.";
                ActiveProjectDisplay = context.ProjectId > 0
                    ? $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId} (\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId})"
                    : $"\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId}";
                return false;
            }

            if (context.ProjectId <= 0)
            {
                StatusMessage = "המשימה אינה מקושרת לפרויקט ולכן לא ניתן לפתוח אותה.";
                return false;
            }

            _browseProjectId = context.ProjectId;
            ActiveProjectDisplay =
                $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId} (\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId} — יצירת דוח)";
            AllowedResultCodes.Clear();
            foreach (var code in context.AllowedResultCodes)
                AllowedResultCodes.Add(code);
            SelectedResultCode = AllowedResultCodes.Count == 1 ? AllowedResultCodes[0] : null;
            OnPropertyChanged(nameof(HasMultipleAllowedResultCodes));
            _reportLoaded = false;
            OnPropertyChanged(nameof(CanCompleteTask));
            RaiseCommandStates();

            await RefreshTemplatesAsync(ct).ConfigureAwait(true);
            await LoadBrowseReportsAsync(context.ProjectId, selectReportId: null, ct).ConfigureAwait(true);
            StatusMessage =
                "משימת בדיקת דוח: צור או בחר דוח לטיפול. הקישור למשימה ייווצר עם יצירת הדוח.";
            return true;
        }

        _browseProjectId = context.ProjectId > 0 ? context.ProjectId : null;
        ActiveProjectDisplay =
            $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId} \u2014 \u05D3\u05D5\u05D7 #{reportId} (\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId})";

        AllowedResultCodes.Clear();
        foreach (var code in context.AllowedResultCodes)
            AllowedResultCodes.Add(code);
        SelectedResultCode = AllowedResultCodes.Count == 1 ? AllowedResultCodes[0] : null;
        OnPropertyChanged(nameof(HasMultipleAllowedResultCodes));

        await RefreshTemplatesAsync(ct).ConfigureAwait(true);
        return await LoadExactReportAsync(context.ProjectId, reportId, ct).ConfigureAwait(true);
    }

    private static bool AllowsInspectionReportCreationWhenMissing(string? taskTypeCode) =>
        string.Equals(taskTypeCode, "PerformProfessionalReview", StringComparison.Ordinal);

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
            IReadOnlyList<int>? completedLinkIds = null;
            if (_reportTaskLinks is not null
                && context.PrimaryWorkTargetEntityId is int reportId
                && reportId > 0)
            {
                var linkId = await _reportTaskLinks
                    .TryGetReportWorkTargetLinkIdAsync(taskId, reportId, cancellationToken)
                    .ConfigureAwait(true);
                if (linkId is int id)
                    completedLinkIds = [id];
            }

            var result = await _taskCompletion
                .CompleteAsync(
                    new CompleteTaskCommand(
                        TaskId: taskId,
                        CompletionEventCode: effectiveEventCode,
                        TaskResultCode: resolvedResultCode,
                        CompletedTaskLinkIds: completedLinkIds,
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

    /// <summary>Persists a general field after LostFocus / auto-manual toggle.</summary>
    public async Task SaveGeneralFieldAsync(InspectionGeneralFieldItem? field)
    {
        if (field is null || _noteCommands is null || !IsReportEditable || !field.IsDirty)
            return;

        var text = field.IsAutomatic && !field.IsManualOverride ? null : field.Value;
        var status = field.IsManualOverride ? InspectionQuestionnaireRules.ManualStatus : null;

        var textResult = await _noteCommands
            .SaveNoteTextAsync(field.NoteId, text)
            .ConfigureAwait(true);
        if (!textResult.Succeeded)
        {
            StatusMessage = textResult.ErrorMessage ?? "שמירת שדה כללי נכשלה.";
            return;
        }

        var statusResult = await _noteCommands
            .SaveNoteStatusAsync(field.NoteId, statusId: null, statusText: status)
            .ConfigureAwait(true);
        if (!statusResult.Succeeded)
        {
            StatusMessage = statusResult.ErrorMessage ?? "שמירת מצב שדה כללי נכשלה.";
            return;
        }

        field.ClearDirty();
        StatusMessage = "שדה כללי נשמר.";
        RaiseCommandStates();
    }

    /// <summary>Persists note text after the inline rich editor exits edit mode.</summary>
    public async Task SaveNoteTextAsync(InspectionNoteItem? note)
    {
        if (note?.NoteId is not long noteId || _noteCommands is null || !IsReportEditable)
            return;

        if (!note.IsDirty)
            return;

        var textResult = await _noteCommands
            .SaveNoteTextAsync(noteId, note.NoteText)
            .ConfigureAwait(true);
        if (!textResult.Succeeded)
        {
            StatusMessage = textResult.ErrorMessage ?? "שמירת ההערה נכשלה.";
            return;
        }

        if (!string.IsNullOrEmpty(note.StatusText))
        {
            await _noteCommands
                .SaveNoteStatusAsync(noteId, note.StatusId, note.StatusText)
                .ConfigureAwait(true);
        }

        note.ClearDirty();
        NoteEditor.ApplyNote(note);
        StatusMessage = "ההערה נשמרה.";
        RaiseCommandStates();
    }

    /// <summary>
    /// Runs AI review for a note: fills grammar/rephrase cache without auto-applying text.
    /// </summary>
    public async Task ReviewNoteAiAsync(InspectionNoteItem? note)
    {
        if (note is null || string.IsNullOrWhiteSpace(note.NoteText) || _aiReviewer is null)
            return;

        var (plain, _) = RichTextCodec.Parse(note.NoteText);
        if (string.IsNullOrWhiteSpace(plain))
            return;

        if (!await _aiReviewer.IsAvailableAsync().ConfigureAwait(true))
        {
            StatusMessage = "שירות AI אינו זמין.";
            return;
        }

        note.AiReviewInProgress = true;
        NoteEditor.IsAiBusy = true;
        try
        {
            var result = await _aiReviewer.ReviewAsync(plain).ConfigureAwait(true);
            if (result.HasError)
            {
                note.ClearAiResults();
                StatusMessage = result.ErrorMessage ?? "בדיקת AI נכשלה.";
                return;
            }

            note.AiOriginalText = result.OriginalText;
            note.AiGrammarResult = result.GrammarCorrected ?? result.OriginalText;
            note.AiRephraseResult = result.Rephrased;
            NoteEditor.GrammarSuggestion = note.AiGrammarResult;
            NoteEditor.RephraseSuggestion = note.AiRephraseResult;
            StatusMessage = note.HasAiGrammarChanges
                ? "בדיקת AI הושלמה — לחץ ימני על ההערה להחלת הצעה."
                : "בדיקת AI הושלמה — אין שגיאות תחביריות.";
        }
        catch (Exception ex)
        {
            note.ClearAiResults();
            StatusMessage = $"בדיקת AI נכשלה: {ex.Message}";
        }
        finally
        {
            note.AiReviewInProgress = false;
            NoteEditor.IsAiBusy = false;
        }
    }

    /// <summary>
    /// Applies an AI suggestion from the note rich-editor context menu, then re-saves and re-reviews.
    /// </summary>
    public async Task ApplyAiSuggestionAsync(InspectionNoteItem? note, string reviewType, string suggestedText)
    {
        if (note is null || string.IsNullOrWhiteSpace(suggestedText))
            return;

        note.NoteText = suggestedText;
        note.ClearAiResults();
        await SaveNoteTextAsync(note).ConfigureAwait(true);

        var displayType = reviewType == "grammar" ? "בדיקת שגיאות" : "בדיקת ניסוח";
        StatusMessage = $"✓ {displayType} הוחל בהצלחה";

        await ReviewNoteAiAsync(note).ConfigureAwait(true);
    }

    private void OnQuestionnairePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectionQuestionnaireViewModel.SelectedNote))
            return;

        NoteEditor.ApplyNote(Questionnaire.SelectedNote);
        OnPropertyChanged(nameof(HasSelectedNote));
        (SaveNoteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ReviewNoteAiCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void AttachNoteHandlers()
    {
        foreach (var note in Questionnaire.EnumerateNotes())
            note.PropertyChanged += OnNoteItemPropertyChanged;
    }

    private void DetachNoteHandlers()
    {
        foreach (var note in Questionnaire.EnumerateNotes())
            note.PropertyChanged -= OnNoteItemPropertyChanged;
    }

    private void AttachGeneralFieldHandlers()
    {
        foreach (var field in Questionnaire.EnumerateGeneralFields())
            field.PropertyChanged += OnGeneralFieldPropertyChanged;
    }

    private void DetachGeneralFieldHandlers()
    {
        foreach (var field in Questionnaire.EnumerateGeneralFields())
            field.PropertyChanged -= OnGeneralFieldPropertyChanged;
    }

    private void OnGeneralFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InspectionGeneralFieldItem.Value)
            or nameof(InspectionGeneralFieldItem.HasValidationError)
            or nameof(InspectionGeneralFieldItem.IsManualOverride))
        {
            RaiseCommandStates();
        }
    }

    private void OnNoteItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not InspectionNoteItem note)
            return;

        if (e.PropertyName is nameof(InspectionNoteItem.HasValidationError)
            or nameof(InspectionNoteItem.StatusText)
            or nameof(InspectionNoteItem.NoteText))
        {
            RaiseCommandStates();
        }

        if (!IsReportEditable || _noteCommands is null)
            return;

        if (e.PropertyName != nameof(InspectionNoteItem.StatusText) || note.NoteId is not long noteId)
            return;

        QueueNoteStatusSave(note, noteId);
    }

    /// <summary>
    /// Coalesces status auto-saves per note: cancels any prior in-flight save for the same note and
    /// schedules a debounced atomic save. Replaces the previous <c>async void</c> handler that fired
    /// overlapping, unobserved <c>SaveNoteStatusAsync</c> calls (last-write-wins data loss).
    /// </summary>
    private void QueueNoteStatusSave(InspectionNoteItem note, long noteId)
    {
        CancellationTokenSource cts;
        lock (_statusSaveGate)
        {
            if (_statusSaveCts.TryGetValue(noteId, out var prior))
            {
                prior.Cancel();
                prior.Dispose();
            }

            cts = new CancellationTokenSource();
            _statusSaveCts[noteId] = cts;
        }

        _ = RunNoteStatusSaveAsync(note, noteId, cts);
    }

    private async Task RunNoteStatusSaveAsync(InspectionNoteItem note, long noteId, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            // Debounce: collapse a burst of StatusText changes into a single write.
            await Task.Delay(StatusSaveDebounce, token).ConfigureAwait(true);

            if (_noteCommands is null)
                return;

            // Route text + status through the atomic SaveNoteAsync so the two fields cannot drift.
            var result = await _noteCommands
                .SaveNoteAsync(noteId, note.NoteText, note.StatusId, note.StatusText, token)
                .ConfigureAwait(true);
            if (!result.Succeeded)
                StatusMessage = result.ErrorMessage ?? "שמירת סטטוס נכשלה.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit; the newer save owns the final state.
        }
        catch (Exception ex)
        {
            StatusMessage = $"שמירת סטטוס נכשלה: {ex.Message}";
        }
        finally
        {
            lock (_statusSaveGate)
            {
                if (_statusSaveCts.TryGetValue(noteId, out var current) && current == cts)
                    _statusSaveCts.Remove(noteId);
            }

            cts.Dispose();
        }
    }

    private bool CanMoveNote(InspectionNoteItem? note, int direction)
    {
        if (note is null || !IsReportEditable || _noteCommands is null || IsBusy)
            return false;
        if (!InspectionQuestionnaireRules.IsNumberedSubNote(note.NoteNumber))
            return false;

        var section = Questionnaire.FindSectionContaining(note);
        if (section is null)
            return false;

        var idx = section.Notes.IndexOf(note);
        var targetIdx = idx + direction;
        if (idx < 0 || targetIdx < 0 || targetIdx >= section.Notes.Count)
            return false;

        return InspectionQuestionnaireRules.IsNumberedSubNote(section.Notes[targetIdx].NoteNumber);
    }

    private async Task MoveNoteAsync(InspectionNoteItem? note, int direction)
    {
        if (note is null || !CanMoveNote(note, direction) || _noteCommands is null)
            return;

        var section = Questionnaire.FindSectionContaining(note);
        if (section is null)
            return;

        var idx = section.Notes.IndexOf(note);
        var targetIdx = idx + direction;
        section.Notes.Move(idx, targetIdx);

        var renumberings = new List<(long NoteId, string SubIndex)>();
        var ordinal = 1;
        foreach (var n in section.Notes)
        {
            if (!InspectionQuestionnaireRules.IsNumberedSubNote(n.NoteNumber) || n.NoteId is not long noteId)
                continue;

            var newIndex = $"{section.SectionNumber}.{ordinal}";
            if (!string.Equals(n.NoteNumber, newIndex, StringComparison.Ordinal))
            {
                n.NoteNumber = newIndex;
                renumberings.Add((noteId, newIndex));
            }

            ordinal++;
        }

        if (renumberings.Count > 0)
        {
            var result = await _noteCommands.RenumberNotesAsync(renumberings).ConfigureAwait(true);
            if (result.Succeeded)
            {
                StatusMessage = "סדר ההערות עודכן.";
            }
            else
            {
                var err = result.ErrorMessage ?? "עדכון סדר ההערות נכשל.";
                if (SelectedReport is not null && ResolveActiveProjectId() is int projectId)
                    await LoadReportContentAsync(projectId, SelectedReport.ReportId).ConfigureAwait(true);
                StatusMessage = err;
            }
        }

        RaiseCommandStates();
    }

    private async Task ScreenshotPrimaryAsync(InspectionNoteItem? note)
    {
        if (note is null)
            return;

        if (note.HasAttachments && !string.IsNullOrWhiteSpace(note.LastAttachmentUrl))
        {
            await OpenLastAttachmentAsync(note).ConfigureAwait(true);
            return;
        }

        await UploadScreenshotAsync(note).ConfigureAwait(true);
    }

    private async Task UploadScreenshotAsync(InspectionNoteItem? note)
    {
        if (note?.NoteId is not long noteId || _screenshotHost is null)
            return;

        StatusMessage = "מעלה צילום מסך...";
        var result = await _screenshotHost.UploadFromClipboardAsync(noteId).ConfigureAwait(true);
        if (result.Succeeded)
        {
            note.AttachmentCount += 1;
            if (!string.IsNullOrWhiteSpace(result.AttachmentUrl))
                note.LastAttachmentUrl = result.AttachmentUrl;
            StatusMessage = string.IsNullOrWhiteSpace(result.AttachmentUrl)
                ? "צילום המסך הועלה."
                : $"צילום הועלה: {result.AttachmentUrl}";
            RaiseCommandStates();
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "העלאת צילום מסך נכשלה.";
        }
    }

    private async Task OpenLastAttachmentAsync(InspectionNoteItem? note)
    {
        if (note?.NoteId is not long noteId || _screenshotHost is null)
            return;

        var result = await _screenshotHost.OpenLastAsync(noteId).ConfigureAwait(true);
        StatusMessage = result.Message;
    }

    private async Task OpenOrSetLinkedFileAsync(InspectionNoteItem? note)
    {
        if (note is null)
            return;

        if (note.HasLinkedFile)
        {
            await OpenLinkedFileAsync(note).ConfigureAwait(true);
            return;
        }

        await SetNoteLinkedFileAsync(note).ConfigureAwait(true);
    }

    private async Task SetNoteLinkedFileAsync(InspectionNoteItem? note)
    {
        if (note?.NoteId is not long noteId
            || _fileTreePicker is null
            || _noteCommands is null
            || ResolveActiveProjectId() is not int projectId)
        {
            StatusMessage = "לא ניתן לבחור קובץ מקושר — חסר פרויקט או בורר קבצים.";
            return;
        }

        var picked = await _fileTreePicker
            .PickNoteLinkedFileAsync(projectId)
            .ConfigureAwait(true);
        if (picked is null)
            return;

        var result = await _noteCommands
            .SetNoteLinkedFileAsync(noteId, picked.FileName, picked.Alternative, picked.Version)
            .ConfigureAwait(true);
        if (!result.Succeeded)
        {
            StatusMessage = result.ErrorMessage ?? "שמירת קובץ מקושר נכשלה.";
            return;
        }

        note.LinkedFileName = picked.FileName;
        note.LinkedAlternative = picked.Alternative;
        note.LinkedVersion = picked.Version;
        note.HasLinkedFile = true;
        StatusMessage = $"הקובץ המקושר עודכן: {picked.FileName}";
        RaiseCommandStates();
    }

    private async Task SelectReviewedPlansAsync()
    {
        if (SelectedReport is null
            || _fileTreePicker is null
            || _reportCommands is null
            || ResolveActiveProjectId() is not int projectId)
        {
            StatusMessage = "לא ניתן לבחור תוכניות שנבדקו — חסר דוח, פרויקט או בורר קבצים (פתח סביבת עבודה).";
            return;
        }

        var picked = await _fileTreePicker
            .PickReviewedPlansAsync(projectId)
            .ConfigureAwait(true);
        if (picked is null)
            return;

        var rows = picked
            .Select((p, i) => new InspectionReviewedFileRow(
                Id: 0,
                FileName: p.FileName,
                Alternative: p.Alternative,
                SortOrder: i))
            .ToList();

        var result = await _reportCommands
            .ReplaceReviewedFilesAsync(SelectedReport.ReportId, rows)
            .ConfigureAwait(true);
        if (!result.Succeeded)
        {
            StatusMessage = result.ErrorMessage ?? "שמירת תוכניות שנבדקו נכשלה.";
            return;
        }

        Metadata.ReplaceReviewedFiles(rows);
        StatusMessage = rows.Count == 0
            ? "רשימת התוכניות שנבדקו רוקנה."
            : $"עודכנו {rows.Count} תוכניות שנבדקו.";
        RaiseCommandStates();
    }

    private async Task ClearNoteLinkedFileAsync(InspectionNoteItem? note)
    {
        if (note?.NoteId is not long noteId || _noteCommands is null || !note.HasLinkedFile)
            return;

        var result = await _noteCommands
            .SetNoteLinkedFileAsync(noteId, null, null, null)
            .ConfigureAwait(true);
        if (!result.Succeeded)
        {
            StatusMessage = result.ErrorMessage ?? "ניקוי קובץ מקושר נכשל.";
            return;
        }

        note.LinkedFileName = null;
        note.LinkedAlternative = null;
        note.LinkedVersion = null;
        note.HasLinkedFile = false;
        StatusMessage = "הקובץ המקושר נוקה.";
        RaiseCommandStates();
    }

    private async Task OpenLinkedFileAsync(InspectionNoteItem? note)
    {
        if (note is null || SelectedReport is null || _linkedFileHost is null)
            return;

        if (string.IsNullOrWhiteSpace(note.LinkedFileName) && Metadata.ReviewedFiles.Count == 0)
        {
            StatusMessage = "אין קובץ מקושר להערה זו.";
            return;
        }

        var result = await _linkedFileHost.OpenAsync(
            new InspectionLinkedFileOpenRequest(
                note.NoteId ?? 0,
                note.LinkedFileName,
                note.LinkedAlternative,
                note.LinkedVersion,
                SelectedReport.ReportId,
                Metadata.ReviewedVersion,
                Metadata.ReviewedFiles.ToList()))
            .ConfigureAwait(true);
        StatusMessage = result.Message;
    }

    private async Task AddNoteToSectionAsync(InspectionSectionItem? section)
    {
        if (section is null || SelectedReport is null || _noteCommands is null || !IsReportEditable)
            return;

        var result = await _noteCommands
            .AddNoteAsync(SelectedReport.ReportId, section.SectionId, text: null)
            .ConfigureAwait(true);
        if (!result.Succeeded || result.NoteId is not long newNoteId)
        {
            StatusMessage = result.ErrorMessage ?? "הוספת הערה נכשלה.";
            return;
        }

        // Reload tree so NoteSubIndex / ordering match SQL.
        if (ResolveActiveProjectId() is int projectId)
            await LoadReportContentAsync(projectId, SelectedReport.ReportId).ConfigureAwait(true);
        else
            StatusMessage = $"הערה #{newNoteId} נוספה.";
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
        if (template is null
            || string.IsNullOrWhiteSpace(template.SpreadsheetId)
            || string.IsNullOrWhiteSpace(template.Url))
        {
            StatusMessage = "יש לבחור תבנית Google תקינה לפני יצירת דוח.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "יוצר דוח ביקורת מהתבנית...";
            // Series is resolved inside CreateReportAsync via ProjectId + SpreadsheetId
            // (EnsureSeries). Do not attach the new report to an arbitrary series[0].
            var result = await _reportCommands
                .CreateReportAsync(
                    projectId,
                    template.Url!,
                    seriesId: null,
                    inspectorName: null,
                    inspectorId: _currentUser?.UserId,
                    spreadsheetId: template.SpreadsheetId)
                .ConfigureAwait(true);

            if (!result.Succeeded || result.ReportId is not int newReportId)
            {
                StatusMessage = result.ErrorMessage ?? "יצירת הדוח נכשלה.";
                return;
            }

            if (_taskContext is { } taskCtx
                && taskCtx.TaskId is int linkTaskId and > 0
                && _reportTaskLinks is not null
                && AllowsInspectionReportCreationWhenMissing(taskCtx.TaskTypeCode))
            {
                var userId = taskCtx.ActingUserId ?? _currentUser?.UserId;
                if (userId is not int uid || uid <= 0)
                {
                    StatusMessage = $"דוח #{newReportId} נוצר, אך לא ניתן לקשר למשימה — משתמש לא ידוע.";
                    await LoadBrowseReportsAsync(projectId, newReportId, manageBusy: false).ConfigureAwait(true);
                    return;
                }

                await _reportTaskLinks
                    .EnsureReportWorkTargetLinkAsync(linkTaskId, newReportId, uid)
                    .ConfigureAwait(true);

                // Re-resolve so PrimaryWorkTargetEntityId reflects the new report for completion.
                _taskContext = taskCtx with { PrimaryWorkTargetEntityId = newReportId };
                OnPropertyChanged(nameof(TaskContext));
            }

            StatusMessage = $"דוח #{newReportId} נוצר — טוען...";
            await LoadBrowseReportsAsync(projectId, newReportId, manageBusy: false).ConfigureAwait(true);
            if (_taskContext?.PrimaryWorkTargetEntityId == newReportId)
                _reportLoaded = SelectedReport is not null;
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

        if (!Questionnaire.CanExport)
        {
            var summary = Questionnaire.ValidationSummary;
            StatusMessage = string.IsNullOrWhiteSpace(summary)
                ? "לא ניתן לייצא — יש למלא את כל השדות וההערות."
                : summary;
            NotifyValidationUi();
            return;
        }

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

            // Also include unscoped reports (SeriesId null) — native create without template sync.
            var unscoped = await _workspace.GetReportsAsync(projectId, seriesId: 0, ct).ConfigureAwait(true);
            foreach (var row in unscoped)
            {
                if (all.Any(r => r.ReportId == row.ReportId))
                    continue;
                all.Add(new InspectionReportRow(
                    row.ReportId,
                    row.ReportNumber,
                    row.InspectorName ?? string.Empty,
                    row.InspectionDate));
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

            async Task<bool> TryLoadFromSeriesAsync(int seriesId)
            {
                var rows = await _workspace.GetReportsAsync(projectId, seriesId, ct).ConfigureAwait(true);
                var match = rows.FirstOrDefault(r => r.ReportId == reportId);
                if (match.ReportId != reportId)
                    return false;

                found = match;
                _preferredSeriesId = seriesId > 0 ? seriesId : null;
                foreach (var row in rows)
                {
                    cardRows.Add(new InspectionReportRow(
                        row.ReportId,
                        row.ReportNumber,
                        row.InspectorName ?? string.Empty,
                        row.InspectionDate));
                }

                return true;
            }

            foreach (var series in seriesList)
            {
                if (await TryLoadFromSeriesAsync(series.SeriesId).ConfigureAwait(true))
                    break;
            }

            if (found is null)
                await TryLoadFromSeriesAsync(0).ConfigureAwait(true);

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
        DetachNoteHandlers();
        DetachGeneralFieldHandlers();
        Notes.Clear();
        NoteEditor.ApplyNote(null);
        OnPropertyChanged(nameof(HasSelectedNote));

        var notes = await _workspace!.GetNotesAsync(reportId, ct).ConfigureAwait(true);
        foreach (var note in notes)
        {
            Notes.Add(new InspectionNoteRow(
                note.Number ?? note.NoteId.ToString(),
                note.Text ?? string.Empty,
                note.Status ?? string.Empty));
        }

        var detail = await _workspace.GetReportDetailAsync(reportId, ct).ConfigureAwait(true);
        Metadata.ApplyDetail(detail);
        _cachedSourceFileUrn = detail?.SourceFileUrn;
        if (detail?.SeriesId is int sid)
            _preferredSeriesId = sid;

        var autoValues = BuildAutoFieldValues(detail);
        var generalRows = await _workspace.GetGeneralFieldsAsync(reportId, ct).ConfigureAwait(true);
        var general = InspectionQuestionnaireViewModel.MapGeneralFields(generalRows, autoValues);

        var tree = await _workspace.GetQuestionnaireTreeAsync(reportId, ct).ConfigureAwait(true);
        var chapters = InspectionQuestionnaireViewModel.MapFromWorkspace(tree);
        Questionnaire.ReplaceTree(general, chapters);
        AttachNoteHandlers();
        AttachGeneralFieldHandlers();

        var reviewed = await _workspace.GetReviewedFilesAsync(reportId, ct).ConfigureAwait(true);
        Metadata.ReplaceReviewedFiles(reviewed);
        var drawings = await _workspace.GetDrawingsAsync(reportId, ct).ConfigureAwait(true);
        DrawingsPanel.Replace(drawings);

        OnPropertyChanged(nameof(IsReportEditable));
        OnPropertyChanged(nameof(IsSelectedReportLocked));
        (OpenSourceReportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        RaiseCommandStates();
    }

    private Dictionary<string, string> BuildAutoFieldValues(InspectionReportDetail? detail)
    {
        var auto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var project = _currentProject?.CurrentProject;
        if (project is not null)
        {
            auto["שם פרויקט"] = project.ProjectName ?? string.Empty;
            auto["מספר פרויקט"] = project.ProjectNumber ?? string.Empty;
            auto["ישוב"] = project.PlaceName ?? string.Empty;
            auto["רשות מקומית"] = project.PlaceName ?? string.Empty;
        }

        if (detail is not null)
        {
            var dateStr = detail.InspectionDate.ToString("dd/MM/yyyy");
            var userName = detail.InspectorName ?? string.Empty;
            var reportNum = detail.ReportNumber.ToString();
            auto["תאריך"] = dateStr;
            auto["Today"] = dateStr;
            auto["ממלא דוח"] = userName;
            auto["User"] = userName;
            auto["מספר דוח"] = reportNum;
            auto["כתובת מייל"] = string.Empty;
            auto["Email"] = string.Empty;
        }

        return auto;
    }

    private void ClearReportContent()
    {
        DetachNoteHandlers();
        DetachGeneralFieldHandlers();
        Notes.Clear();
        Questionnaire.ReplaceTree([]);
        NoteEditor.ApplyNote(null);
        Metadata.ApplyDetail(null);
        Metadata.ReplaceReviewedFiles([]);
        DrawingsPanel.Replace([]);
        _cachedSourceFileUrn = null;
        _reportLoaded = false;
        OnPropertyChanged(nameof(HasSelectedNote));
        OnPropertyChanged(nameof(IsReportEditable));
        OnPropertyChanged(nameof(IsSelectedReportLocked));
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
        (AddNoteCommand as AsyncRelayCommand<InspectionSectionItem>)?.RaiseCanExecuteChanged();
        (SaveNoteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (MoveNoteUpCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (MoveNoteDownCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (ScreenshotPrimaryCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (AttachScreenshotCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (OpenLastAttachmentCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (OpenNoteLinkedFileCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (SetNoteLinkedFileCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        (ClearNoteLinkedFileCommand as AsyncRelayCommand<InspectionNoteItem>)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanCompleteTask));
        NotifyValidationUi();
    }

    private void NotifyValidationUi()
    {
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasValidationBlockingExport));
        OnPropertyChanged(nameof(ExportTooltip));
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
        await SaveNoteTextAsync(Questionnaire.SelectedNote).ConfigureAwait(true);
    }

    private async Task ReviewSelectedNoteAiAsync()
    {
        if (_aiReviewer is null || Questionnaire.SelectedNote is null)
        {
            StatusMessage = "בדיקת AI אינה זמינה.";
            return;
        }

        await ReviewNoteAiAsync(Questionnaire.SelectedNote).ConfigureAwait(true);
    }

    private AsyncRelayCommand Stub() => new(() =>
    {
        StatusMessage = NotWiredYet;
        return Task.CompletedTask;
    });
}
