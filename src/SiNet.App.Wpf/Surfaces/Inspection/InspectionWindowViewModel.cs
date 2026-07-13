using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using AppInspectionNoteRow = SiNet.Application.Abstractions.Inspection.InspectionNoteRow;
using AppInspectionReportRow = SiNet.Application.Abstractions.Inspection.InspectionReportRow;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// View model for <see cref="InspectionWindowView"/> — the visual clone of the legacy
/// <c>FloatingInspectionView</c>. Task mode loads the exact report via
/// <see cref="IInspectionWorkspace"/> and completes through <see cref="ITaskCompletionService"/>.
/// Heavy write actions (generate/share/ACC) remain stubbed until later slices.
/// </summary>
public sealed class InspectionWindowViewModel : ObservableObject
{
    private const string NotWiredYet =
        "\u05E4\u05E2\u05D5\u05DC\u05D4 \u05D6\u05D5 \u05D8\u05E8\u05DD \u05D7\u05D5\u05D1\u05E8\u05D4 (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u05D1\u05DC\u05D1\u05D3).";

    private readonly IInspectionWorkspace? _workspace;
    private readonly ITaskCompletionService? _taskCompletion;
    private readonly ITaskCompletionMetadataResolver? _completionMetadata;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IInspectionNoteCommandService? _noteCommands;
    private readonly IInspectionNoteAiReviewer? _aiReviewer;

    private WorkSurfaceContext? _taskContext;
    private string _activeProjectDisplay = "\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05D3\u05D5\u05D7\u05D5\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA";
    private bool _isPinned;
    private bool _isDocked;
    private bool _isCollapsed;
    private bool _isBusy;
    private bool _reportLoaded;
    private InspectionTemplateRow? _selectedTemplate;
    private InspectionReportRow? _selectedReport;
    private string? _selectedResultCode;
    private string _statusMessage = "\u05DE\u05D5\u05DB\u05DF (\u05E9\u05DC\u05D3 \u05D5\u05D9\u05D6\u05D5\u05D0\u05DC\u05D9 \u2014 \u05DC\u05DC\u05D0 \u05D7\u05D9\u05D1\u05D5\u05E8 \u05E0\u05EA\u05D5\u05E0\u05D9\u05DD)";

    /// <summary>Design-time / standalone constructor with fake data.</summary>
    public InspectionWindowViewModel()
        : this(null, null, null, null)
    {
    }

    public InspectionWindowViewModel(
        IInspectionWorkspace? workspace,
        ITaskCompletionService? taskCompletion = null,
        ITaskCompletionMetadataResolver? completionMetadata = null,
        ICurrentUserContext? currentUser = null,
        IInspectionNoteCommandService? noteCommands = null,
        IInspectionNoteAiReviewer? aiReviewer = null)
    {
        _workspace = workspace;
        _taskCompletion = taskCompletion;
        _completionMetadata = completionMetadata;
        _currentUser = currentUser;
        _noteCommands = noteCommands;
        _aiReviewer = aiReviewer;

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
        else
        {
            foreach (var template in InspectionWindowDesignData.SampleTemplates)
                CreateStrip.AvailableTemplates.Add(template);
            SelectedTemplate = AvailableTemplates.FirstOrDefault();
        }

        _selectedTemplate = AvailableTemplates.FirstOrDefault();

        ToggleCollapseCommand = new AsyncRelayCommand(() =>
        {
            IsCollapsed = !IsCollapsed;
            return Task.CompletedTask;
        });

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && IsTaskMode);
        CompleteTaskCommand = new AsyncRelayCommand(
            () => CompleteFromTaskAsync(),
            () => CanCompleteTask);

        CreateReportCommand = Stub();
        MarkResponseReceivedCommand = Stub();
        RepullPlannerResponsesCommand = Stub();
        OpenSourceReportCommand = Stub();
        UnlockReportCommand = Stub();
        ShareReportCommand = Stub();
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
                (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
        set => SetField(ref _selectedTemplate, value);
    }

    public InspectionReportRow? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetField(ref _selectedReport, value))
                OnPropertyChanged(nameof(HasSelectedReport));
        }
    }

    public bool HasSelectedReport => _selectedReport is not null;

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

    public ICommand ToggleCollapseCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CreateReportCommand { get; }
    public ICommand MarkResponseReceivedCommand { get; }
    public ICommand RepullPlannerResponsesCommand { get; }
    public ICommand OpenSourceReportCommand { get; }
    public ICommand UnlockReportCommand { get; }
    public ICommand ShareReportCommand { get; }
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
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

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

        ActiveProjectDisplay =
            $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId} \u2014 \u05D3\u05D5\u05D7 #{reportId} (\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId})";

        AllowedResultCodes.Clear();
        foreach (var code in context.AllowedResultCodes)
            AllowedResultCodes.Add(code);
        SelectedResultCode = AllowedResultCodes.Count == 1 ? AllowedResultCodes[0] : null;

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

    private async Task RefreshAsync()
    {
        if (_taskContext?.PrimaryWorkTargetEntityId is not int reportId)
            return;

        await LoadExactReportAsync(_taskContext.ProjectId, reportId).ConfigureAwait(true);
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
            Reports.Clear();
            Notes.Clear();
            InspectionTree.Clear();
            SelectedReport = null;
            _reportLoaded = false;

            var seriesList = await _workspace.GetSeriesAsync(projectId, ct).ConfigureAwait(true);
            AppInspectionReportRow? found = null;

            foreach (var series in seriesList)
            {
                var rows = await _workspace.GetReportsAsync(projectId, series.SeriesId, ct).ConfigureAwait(true);
                found = rows.FirstOrDefault(r => r.ReportId == reportId);
                if (found is { ReportId: var id } && id == reportId)
                {
                    foreach (var row in rows)
                    {
                        Reports.Add(new InspectionReportRow(
                            row.ReportId,
                            row.ReportNumber,
                            row.InspectorName ?? string.Empty,
                            row.InspectionDate));
                    }

                    SelectedReport = Reports.FirstOrDefault(r => r.ReportId == reportId);
                    break;
                }

                found = null;
            }

            if (found is null || found.Value.ReportId != reportId)
            {
                StatusMessage =
                    $"Inspection report #{reportId} was not found in project {projectId}. No fallback.";
                OnPropertyChanged(nameof(CanCompleteTask));
                return false;
            }

            var notes = await _workspace.GetNotesAsync(reportId, ct).ConfigureAwait(true);
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
            var reviewed = await _workspace.GetReviewedFilesAsync(reportId, ct).ConfigureAwait(true);
            Metadata.ReplaceReviewedFiles(reviewed);
            var drawings = await _workspace.GetDrawingsAsync(reportId, ct).ConfigureAwait(true);
            DrawingsPanel.Replace(drawings);

            _reportLoaded = true;
            StatusMessage = $"Opened inspection report #{reportId} for task #{_taskContext?.TaskId}.";
            OnPropertyChanged(nameof(CanCompleteTask));
            (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
