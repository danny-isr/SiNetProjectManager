using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email.Detail;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using System.Windows;

namespace SiNet.App.Wpf.Surfaces.Email;

public sealed class EmailDetailViewModel : ObservableObject, IDisposable
{
    private readonly EmailListViewModel _emailList;
    private readonly ICurrentProjectContext _currentProject;
    private readonly IEmailFilingService? _filingService;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IEmailMoveToProjectService? _moveToProjectService;
    private readonly IEmailMoveToProjectEligibilityService? _moveEligibility;
    private readonly IEmailWorkflowContextService? _workflowContextService;
    private readonly IEmailSuggestedActionService? _suggestedActionService;
    private readonly IEmailSuggestedActionExecutionService? _actionExecutionService;
    private readonly IEmailAttachmentTaggingService? _attachmentTaggingService;
    private readonly IEmailAttachmentProjectFilePickerHost? _attachmentProjectFilePicker;
    private readonly IEmailFilingProjectPickerHost? _filingProjectPicker;
    private readonly IEmailAlternativeNamePromptHost? _alternativeNamePrompt;
    private readonly ITaskCompletionService? _taskCompletionService;
    private readonly IEmailBodyRenderer? _bodyRenderer;
    private readonly IShellContentHost? _shellContentHost;
    private readonly IEmailInboxQueryService? _inboxQuery;
    private readonly IAccResolvedDocsUrlLauncher? _accLauncher;
    private EmailExternalDownloadHandler? _externalDownloadHandler;

    private EmailListRow? _selectedEmail;
    private WorkSurfaceContext? _workSurfaceContext;
    private int _selectedEmailLoadVersion;
    private string? _loadedBodyMessageId;
    private CancellationTokenSource? _selectionCts;
    private string _selectedEmailBody = string.Empty;
    private string? _selectedEmailHtmlBody;
    private IReadOnlyList<EmailInlineImage> _selectedInlineImages = [];
    private string _selectedAccStatusDisplay = string.Empty;
    private bool _isBusy;
    private string? _inboxAccProjectId;
    private string? _inboxAccFolderId;
    private readonly EmailDetailSelectionCoordinator _selectionCoordinator;

    public EmailDetailViewModel(
        EmailListViewModel emailList,
        IEmailGateway emailGateway,
        ICurrentProjectContext currentProject,
        IEmailFilingService? filingService = null,
        ICurrentUserContext? currentUser = null,
        IEmailMoveToProjectService? moveToProjectService = null,
        IEmailMoveToProjectEligibilityService? moveEligibility = null,
        IEmailWorkflowContextService? workflowContextService = null,
        IEmailSuggestedActionService? suggestedActionService = null,
        IEmailSuggestedActionExecutionService? actionExecutionService = null,
        ITaskCompletionService? taskCompletionService = null,
        IEmailBodyRenderer? bodyRenderer = null,
        IEmailAttachmentTaggingService? attachmentTaggingService = null,
        IEmailAttachmentProjectFilePickerHost? attachmentProjectFilePicker = null,
        IEmailFilingProjectPickerHost? filingProjectPicker = null,
        IEmailAlternativeNamePromptHost? alternativeNamePrompt = null,
        IShellContentHost? shellContentHost = null,
        IEmailInboxQueryService? inboxQuery = null,
        IAccResolvedDocsUrlLauncher? accLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(emailList);
        ArgumentNullException.ThrowIfNull(emailGateway);
        _emailList = emailList;
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _filingService = filingService;
        _currentUser = currentUser;
        _moveToProjectService = moveToProjectService;
        _moveEligibility = moveEligibility;
        _workflowContextService = workflowContextService;
        _suggestedActionService = suggestedActionService;
        _actionExecutionService = actionExecutionService;
        _taskCompletionService = taskCompletionService;
        _bodyRenderer = bodyRenderer;
        _attachmentTaggingService = attachmentTaggingService;
        _attachmentProjectFilePicker = attachmentProjectFilePicker;
        _filingProjectPicker = filingProjectPicker;
        _alternativeNamePrompt = alternativeNamePrompt;
        _shellContentHost = shellContentHost;
        _inboxQuery = inboxQuery;
        _accLauncher = accLauncher;

        AttachmentStrip = new EmailAttachmentStripViewModel(OpenExternalDownloadLink);
        ActionBar = new EmailActionBarViewModel(FileSelectedEmailAsync, MoveSelectedEmailToProjectAsync);
        Workflow = new EmailWorkflowActionsPaneViewModel(ExecuteSelectedWorkflowActionAsync);
        Viewer = new EmailViewerPaneViewModel();

        _selectionCoordinator = new EmailDetailSelectionCoordinator(
            emailGateway,
            _emailList,
            message => StatusMessage = message,
            (body, html, inlineImages) => SetSelectedEmailContent(body, html, inlineImages),
            acc => SelectedAccStatusDisplay = acc,
            AttachmentStrip.Attachments,
            CreateDisplayAttachmentItem,
            () => _selectedEmail,
            () => _selectedEmailLoadVersion,
            () => _selectedEmailLoadVersion++);

        UpdateActiveProjectDisplay(_currentProject.CurrentProject);
    }

    private EmailDetailAttachmentItem CreateDisplayAttachmentItem(string fileName, string kind, string size) =>
        new(
            inboxAttachmentId: 0,
            fileName,
            kind,
            size,
            isTaggable: false,
            TagAttachmentAsync,
            AlternativeChangedAsync,
            OpenAttachmentInAccAsync);

    public EmailViewerPaneViewModel Viewer { get; }
    public EmailAttachmentStripViewModel AttachmentStrip { get; }
    public EmailActionBarViewModel ActionBar { get; }
    public EmailWorkflowActionsPaneViewModel Workflow { get; }

    public WorkSurfaceContext? WorkSurfaceContext => _workSurfaceContext;

    internal void SetExternalDownloadHandler(EmailExternalDownloadHandler handler) =>
        _externalDownloadHandler = handler;

    public bool HasSelectedEmail => _selectedEmail is not null;

    internal int GetLoadVersion() => _selectedEmailLoadVersion;

    internal void BumpLoadVersion() => _selectedEmailLoadVersion++;

    internal EmailDetailSelectionCoordinator SelectionCoordinator => _selectionCoordinator;

    private string _statusMessage = string.Empty;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    private void SetSelectedEmailContent(
        string bodyText,
        string? htmlBody,
        IReadOnlyList<EmailInlineImage> inlineImages)
    {
        _selectedEmailHtmlBody = htmlBody;
        _selectedInlineImages = inlineImages;
        SelectedEmailBody = bodyText;
        if (string.IsNullOrWhiteSpace(bodyText)
            || string.Equals(bodyText, "טוען תוכן מייל...", StringComparison.Ordinal))
        {
            _loadedBodyMessageId = null;
        }
        else
        {
            _loadedBodyMessageId = _selectedEmail?.Id;
        }
    }

    public string SelectedEmailBody
    {
        get => _selectedEmailBody;
        private set
        {
            if (SetField(ref _selectedEmailBody, value))
            {
                Viewer.SyncFromBody(value, _selectedEmailHtmlBody, _bodyRenderer, _selectedEmail?.Id, _selectedInlineImages);
                RefreshExternalDownloadLinks();
            }
        }
    }

    public string SelectedAccStatusDisplay
    {
        get => _selectedAccStatusDisplay;
        private set
        {
            if (SetField(ref _selectedAccStatusDisplay, value))
            {
                Viewer.AccStatusDisplay = value;
            }
        }
    }

    public event EventHandler<string>? StatusMessageChanged;

    public async Task ApplySelectionAsync(EmailListRow? row)
    {
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;

        _selectedEmail = row;
        OnPropertyChanged(nameof(HasSelectedEmail));
        SyncViewerHeader();

        if (row is null)
        {
            _loadedBodyMessageId = null;
            _selectionCoordinator.ClearSelectedEmailDetails();
            AttachmentStrip.Clear();
            Workflow.Clear();
            RefreshActionBarState();
            return;
        }

        var loadVersion = ++_selectedEmailLoadVersion;

        // Always reset viewer details when the loaded body belongs to a different message.
        // HasLoadedBodyForCurrentSelection must key off _loadedBodyMessageId (not only
        // _selectedEmail.Id, which is already updated above).
        if (!HasLoadedBodyForCurrentSelection(row.Id))
        {
            _selectionCoordinator.PrepareSelectedEmailDetailsLoading();
            SelectedAccStatusDisplay = string.Empty;
        }

        try
        {
            await RunSelectionPipelineAsync(row, loadVersion).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !IsCurrentSelection(row.Id, loadVersion))
            {
                return;
            }

            await RefreshInboxAttachmentsAsync(loadVersion, row.Id).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !IsCurrentSelection(row.Id, loadVersion))
            {
                return;
            }

            await RefreshWorkflowContextAsync().ConfigureAwait(true);
            if (ct.IsCancellationRequested || !IsCurrentSelection(row.Id, loadVersion))
            {
                return;
            }

            await RefreshMoveEligibilityAsync().ConfigureAwait(true);
            if (ct.IsCancellationRequested || !IsCurrentSelection(row.Id, loadVersion))
            {
                return;
            }

            RefreshActionBarState();
        }
        catch (OperationCanceledException)
        {
            // Newer selection superseded this one.
        }
    }

    public void ApplyWorkSurfaceContext(WorkSurfaceContext? context) =>
        _workSurfaceContext = context;

    public void ClearOnDisconnect()
    {
        _selectionCts?.Cancel();
        _selectedEmail = null;
        _loadedBodyMessageId = null;
        OnPropertyChanged(nameof(HasSelectedEmail));
        _selectionCoordinator.ClearSelectedEmailDetails();
        Viewer.Clear();
        AttachmentStrip.Clear();
        Workflow.Clear();
        RefreshActionBarState();
    }

    public void UpdateActiveProjectDisplay(ProjectSummaryDto? project)
    {
        ActionBar.ActiveProjectDisplay = project is null
            ? "לא נבחר פרויקט"
            : $"{project.ProjectNumber} — {project.ProjectName}";
        _ = RefreshMoveEligibilityThenActionBarAsync();
        _ = RefreshWorkflowContextAsync();
    }

    private async Task RefreshMoveEligibilityThenActionBarAsync()
    {
        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        RefreshActionBarState();
    }

    public Task OpenSelectedEmailAsync() => _selectionCoordinator.OpenSelectedEmailAsync();

    public void Dispose()
    {
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = null;
        _bodyRenderer?.Clear();
    }

    private async Task RunSelectionPipelineAsync(EmailListRow row, int loadVersion)
    {
        await _selectionCoordinator
            .LoadSelectedEmailWithAccPipelineAsync(row, loadVersion, HasLoadedBodyForCurrentSelection)
            .ConfigureAwait(true);

        if (_externalDownloadHandler is not null
            && HasLoadedBodyForCurrentSelection(row.Id)
            && string.Equals(_selectedEmail?.Id, row.Id, StringComparison.Ordinal))
        {
            await _externalDownloadHandler
                .MergeExternalDownloadsIntoViewerAsync(row, loadVersion)
                .ConfigureAwait(true);
        }
    }

    private void SyncViewerHeader()
    {
        if (_selectedEmail is null)
        {
            Viewer.Clear();
            return;
        }

        Viewer.Subject = _selectedEmail.Subject;
        Viewer.Sender = _selectedEmail.Sender;
        Viewer.ReceivedDisplay = _selectedEmail.ReceivedDisplay;
    }

    private bool HasLoadedBodyForCurrentSelection(string messageId) =>
        string.Equals(_loadedBodyMessageId, messageId, StringComparison.Ordinal)
        && string.Equals(_selectedEmail?.Id, messageId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedEmailBody)
        && !string.Equals(SelectedEmailBody, "טוען תוכן מייל...", StringComparison.Ordinal);

    private bool IsCurrentSelection(string messageId, int loadVersion) =>
        loadVersion == _selectedEmailLoadVersion
        && string.Equals(_selectedEmail?.Id, messageId, StringComparison.Ordinal);

    private void RefreshExternalDownloadLinks()
    {
        var urls = _selectedEmail is not null && _externalDownloadHandler?.IsAvailable == true
            ? EmailExternalDownloadLinkDetector.ExtractUrls(SelectedEmailBody)
            : Array.Empty<string>();
        AttachmentStrip.SetExternalDownloadLinks(urls);
    }

    private void OpenExternalDownloadLink(string url)
    {
        if (_selectedEmail is null || _externalDownloadHandler is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _externalDownloadHandler.OpenDownloadLink(url, _selectedEmail);
    }

    private async Task FileSelectedEmailAsync()
    {
        if (_selectedEmail is null)
        {
            SetStatus("לא נבחר מייל.");
            return;
        }

        if (!_emailList.CanAttemptFileEmailToProject(_selectedEmail))
        {
            SetStatus("שיוך לפרויקט אינו זמין כרגע.");
            return;
        }

        var project = _currentProject.CurrentProject;
        if (project is null)
        {
            if (_filingProjectPicker is null || !_filingProjectPicker.IsAvailable)
            {
                SetStatus("בחר פרויקט לפני שיוך מייל.");
                return;
            }

            project = await _filingProjectPicker.PickProjectAsync().ConfigureAwait(true);
            if (project is null)
            {
                SetStatus("שיוך בוטל.");
                return;
            }
        }

        var selectedId = _selectedEmail.Id;

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step(
            "Email.File",
            $"start gmailFiled={_selectedEmail.IsFiledToProject} target={project.ProjectId} inbox={_selectedEmail.InboxMessageId?.ToString() ?? "(none)"}");

        // Only Gmail project-label filing counts as "משויך". Capture the returned row so we
        // do not depend on list filters that may hide the message after the label is applied.
        var filedRow = await _emailList.FileEmailToProjectAsync(_selectedEmail, project).ConfigureAwait(true);
        _selectedEmail = filedRow
                         ?? _emailList.FindRowById(selectedId)
                         ?? _selectedEmail;

        if (_selectedEmail is null || !_selectedEmail.IsFiledToProject)
        {
            var warning = _emailList.LoadWarning;
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.File",
                $"FAILED — no Gmail project label on row. warning={warning ?? "(none)"}");
            SetStatus(string.IsNullOrWhiteSpace(warning)
                ? "שיוך המייל לפרויקט נכשל (תווית Gmail לא עודכנה)."
                : warning);
            OnPropertyChanged(nameof(HasSelectedEmail));
            SyncViewerHeader();
            await RefreshMoveEligibilityAsync().ConfigureAwait(true);
            RefreshActionBarState();
            return;
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step(
            "Email.File",
            $"ok — Gmail filed label={_selectedEmail.FiledProjectLabelPath ?? "(path pending)"}");

        OnPropertyChanged(nameof(HasSelectedEmail));
        SyncViewerHeader();
        await RefreshInboxAttachmentsAsync().ConfigureAwait(true);
        await EnsureDefaultAlternativesPersistedAsync().ConfigureAwait(true);
        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        await RefreshWorkflowContextAsync().ConfigureAwait(true);
        RefreshActionBarState();

        // Filing task UX: assign (Gmail label) + move in one click.
        if (_moveToProjectService?.IsAvailable == true
            && _selectedEmail.InboxMessageId is > 0
            && string.IsNullOrWhiteSpace(ActionBar.MoveBlockReason))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.File", "auto-move after Gmail label");
            await MoveSelectedEmailToProjectAsync().ConfigureAwait(true);
        }
        else
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.File",
                $"auto-move skipped: moveAvail={_moveToProjectService?.IsAvailable == true} inbox={_selectedEmail.InboxMessageId?.ToString() ?? "(none)"} block={ActionBar.MoveBlockReason ?? "(none)"}");
        }
    }

    private async Task MoveSelectedEmailToProjectAsync()
    {
        if (_moveToProjectService is null || _selectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            SetStatus("MoveToProject אינו זמין.");
            return;
        }

        // The ACC move can take a minute; a second click while the first move is in flight would
        // start a duplicate move + duplicate task completion (observed in manual QA logs).
        if (IsBusy)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.Move", "re-entry blocked — move already in progress");
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            SetStatus("בחר פרויקט לפני העברה.");
            return;
        }

        IsBusy = true;
        SetStatus("מעביר את הקבצים לפרויקט… הפעולה עשויה להימשך עד דקה.");
        try
        {
            await RefreshMoveEligibilityAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(ActionBar.MoveBlockReason))
            {
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Email.Move", $"blocked: {ActionBar.MoveBlockReason}");
                SetStatus(ActionBar.MoveBlockReason);
                RefreshActionBarState();
                return;
            }

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.Move",
                $"start inbox={inboxMessageId} project={projectId} task={_workSurfaceContext?.TaskId?.ToString() ?? "(none)"}");
            var result = await _moveToProjectService.MoveAsync(
                new EmailMoveToProjectDetailCommand(
                    inboxMessageId,
                    projectId,
                    _workSurfaceContext?.TaskId,
                    _workSurfaceContext?.CompletionEventCode)).ConfigureAwait(true);

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.Move",
                $"result ok={result.Succeeded} moved={result.MovedCount} failures={result.AttachmentFailures?.Count ?? 0} message={result.Message.Replace("\r\n", " | ").Replace('\n', '|').Replace('\r', '|')}");

            PresentMoveOutcomeToUser(result);

            var taskCompleted = false;
            string? completionBlockReason = null;
            // Close / complete only when EVERY tagged file was transferred.
            if (result.AllFilesTransferred
                && _workSurfaceContext?.TaskId is int taskId
                && _taskCompletionService is not null
                && TryResolveTaskCompletionParams(out var completionEventCode, out var actingUserId, out completionBlockReason))
            {
                var completion = await _taskCompletionService.CompleteAsync(
                    new CompleteTaskCommand(
                        taskId,
                        completionEventCode,
                        TaskResultCode: null,
                        CompletedTaskLinkIds: null,
                        actingUserId),
                    CancellationToken.None).ConfigureAwait(true);

                if (!completion.Success)
                {
                    var failText =
                        $"העברה הצליחה אך השלמת המשימה נכשלה: {completion.ErrorMessage ?? "unknown error"}.";
                    SetStatus(failText);
                    PresentMoveOutcomeDialog(failText, succeeded: false);
                }
                else
                {
                    taskCompleted = true;
                    SetStatus("העברה והשלמת משימת התיוק הושלמו.");
                }
            }
            else if (result.AllFilesTransferred
                     && _workSurfaceContext?.TaskId is int
                     && _taskCompletionService is not null
                     && !TryResolveTaskCompletionParams(out _, out _, out completionBlockReason))
            {
                var blocked = $"העברה הצליחה אך השלמת המשימה נחסמה: {completionBlockReason}.";
                SetStatus(blocked);
                PresentMoveOutcomeDialog(blocked, succeeded: false);
            }

            if (_selectedEmail is not null)
            {
                await _emailList.LoadAccStatusForRowAsync(_selectedEmail).ConfigureAwait(true);
                await RefreshInboxAttachmentsAsync().ConfigureAwait(true);
            }

            // Window closes only after all files transferred — never on partial/empty/failed.
            if (result.AllFilesTransferred && _workSurfaceContext?.TaskId is not null)
            {
                TryDismissFilingSurface();
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshMoveEligibilityAsync().ConfigureAwait(true);
            RefreshActionBarState();
        }
    }

    /// <summary>
    /// Raised when a task-driven filing flow finished and the hosting surface should close.
    /// Subscribed by <c>EmailWorkItemWindow</c> (popup host); the shell-hosted surface is
    /// dismissed directly via <see cref="IShellContentHost"/>.
    /// </summary>
    public event Action? WorkItemDismissRequested;

    private void TryDismissFilingSurface()
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.Move",
            $"dismiss surface — shellHostAttached={_shellContentHost is { IsAttached: true }} windowSubscribers={WorkItemDismissRequested is not null}");

        // Popup host (task-driven EmailWorkItemWindow): ask the window to close.
        WorkItemDismissRequested?.Invoke();

        if (_shellContentHost is not { IsAttached: true })
        {
            return;
        }

        // Leave the email work surface so the operator is not left staring at a half-finished action bar.
        _shellContentHost.NavigateTo(null);
    }

    /// <summary>
    /// Persists alternative «1» (or IsDefault) for tagged attachments that still have no alternative id.
    /// </summary>
    private async Task EnsureDefaultAlternativesPersistedAsync()
    {
        if (_attachmentTaggingService is null
            || _selectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            return;
        }

        foreach (var item in AttachmentStrip.Attachments)
        {
            if (item.ProjectFileId is not int projectFileId || projectFileId <= 0)
            {
                continue;
            }

            var alternativeId = item.SelectedAlternativeId is > 0
                ? item.SelectedAlternativeId
                : EmailProjectAlternativeOption.ResolveDefaultId(item.AvailableAlternatives);

            if (alternativeId is not > 0)
            {
                continue;
            }

            item.SelectedAlternativeId = alternativeId;
            item.RememberCurrentAlternativeAsPrevious();

            var result = await _attachmentTaggingService.SetTagAsync(
                new EmailAttachmentTagCommand(
                    item.InboxAttachmentId,
                    projectFileId,
                    alternativeId,
                    _currentUser?.UserId ?? 0)).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                SetStatus(result.ErrorMessage ?? "שמירת אלטרנטיבת ברירת מחדל נכשלה.");
            }
        }
    }

    private async Task RefreshMoveEligibilityAsync()
    {
        if (_moveEligibility is null || _selectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            ActionBar.MoveBlockReason = _selectedEmail?.AttachmentCount > 0
                ? "בחר יעד תיוק לכל הצרופות לפני העברה."
                : null;
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        var eligibility = await _moveEligibility.EvaluateAsync(
            new EmailMoveToProjectEligibilityQuery(
                inboxMessageId,
                projectId,
                _selectedEmail.AttachmentCount,
                _selectedEmail.IsFiledToProject)).ConfigureAwait(true);

        ActionBar.MoveBlockReason = eligibility.CanMove ? null : eligibility.BlockReason;
    }

    private async Task RefreshWorkflowContextAsync()
    {
        if (_workflowContextService is null || _suggestedActionService is null || _selectedEmail is null)
        {
            Workflow.Clear();
            return;
        }

        Workflow.IsLoading = true;
        Workflow.StatusMessage = "מנתח הקשר...";
        try
        {
            // Resolve by inbox id and/or Gmail id — after CreatePriceQuote the inbox row may
            // have been materialized even when the list row still has InboxMessageId=null.
            var context = await _workflowContextService.AnalyzeAsync(
                    new EmailWorkflowContextQuery(
                        _selectedEmail.InboxMessageId,
                        _selectedEmail.Id,
                        OverrideProjectId: null,
                        InternetMessageId: _selectedEmail.InternetMessageId))
                .ConfigureAwait(true);

            if (context is null)
            {
                context = new EmailWorkflowContextDto(
                    HasContext: true,
                    ProjectDisplay: "לא משויך לפרויקט",
                    WorkflowFamilyDisplay: null,
                    ConfidenceDisplay: null,
                    ActiveWorkflowCount: 0,
                    AttachmentCount: _selectedEmail.AttachmentCount,
                    IsAssociatedToProject: false);
            }

            var actions = _suggestedActionService.BuildActions(context);

            Workflow.ApplyContext(context, actions);
            if (context.HasActiveProposalForEmail
                && !string.IsNullOrWhiteSpace(context.ActiveProposalSummary))
            {
                Workflow.StatusMessage = context.ActiveProposalSummary!;
            }
            else if (string.Equals(Workflow.StatusMessage, "מנתח הקשר...", StringComparison.Ordinal))
            {
                Workflow.StatusMessage = string.Empty;
            }
        }
        finally
        {
            Workflow.IsLoading = false;
        }
    }

    private async Task ExecuteSelectedWorkflowActionAsync()
    {
        if (_actionExecutionService is null || Workflow.SelectedAction is not { } action)
        {
            return;
        }

        Workflow.IsLoading = true;
        try
        {
            var result = await _actionExecutionService.ExecuteAsync(
                new EmailSuggestedActionExecutionCommand(
                    action.ActionCode,
                    _selectedEmail?.InboxMessageId,
                    _currentUser?.UserId ?? 0,
                    BuildGmailSource(_selectedEmail))).ConfigureAwait(true);

            var feedback = result.Message ?? (result.Succeeded ? "הפעולה הושלמה." : "הפעולה נכשלה.");
            Workflow.StatusMessage = feedback;
            SetStatus(feedback);

            if (result.InboxMessageId is int materializedInboxId
                && materializedInboxId > 0
                && _selectedEmail is { } selected)
            {
                var patched = _emailList.PatchRowInboxMessageId(selected.Id, materializedInboxId);
                if (patched is not null)
                {
                    _selectedEmail = patched;
                    OnPropertyChanged(nameof(HasSelectedEmail));
                }
            }

            await RefreshWorkflowContextAsync().ConfigureAwait(true);

            // Keep a clear top-of-pane message after refresh (banner + status line).
            if (result.Succeeded
                && (string.Equals(action.ActionCode, EmailSuggestedActionCodes.CreatePriceQuote, StringComparison.Ordinal)
                    || string.Equals(action.ActionCode, EmailSuggestedActionCodes.RejectPriceQuote, StringComparison.Ordinal)))
            {
                if (!Workflow.ShowProposalBanner)
                    Workflow.StatusMessage = feedback;
                SetStatus(Workflow.ActiveProposalSummary ?? feedback);
            }
        }
        finally
        {
            Workflow.IsLoading = false;
        }
    }

    /// <summary>
    /// Builds the Gmail message identity carried alongside a suggested-action command so that a
    /// workflow-starting action (e.g. CreatePriceQuote) can materialize an inbox row on demand when the
    /// email has not been ingested yet (no <see cref="EmailListRow.InboxMessageId"/>). Returns null when
    /// there is no selected email.
    /// </summary>
    private static EmailGmailSourceIdentity? BuildGmailSource(EmailListRow? row)
    {
        if (row is null)
        {
            return null;
        }

        return new EmailGmailSourceIdentity(
            GmailMessageId: row.Id,
            InternetMessageId: row.InternetMessageId,
            References: null,
            InReplyTo: null,
            Subject: row.Subject,
            FromAddress: row.Sender,
            ReceivedUtc: row.ReceivedOn == DateTime.MinValue ? null : row.ReceivedOn.ToUniversalTime(),
            GmailThreadId: row.ThreadId);
    }

    private void RefreshActionBarState()
    {
        var hasSelection = _selectedEmail is not null;
        var isFiled = _selectedEmail?.IsFiledToProject == true;

        ActionBar.ShowUnassignedLayout = hasSelection && !isFiled;
        ActionBar.ShowAssignedLayout = hasSelection && isFiled;
        ActionBar.AssignedHint = isFiled
            ? "משויך לפרויקט — ניתן להעביר קבצים מתויקים ל-ACC"
            : null;

        ActionBar.RefreshCommandStates(
            canFile: hasSelection
                     && !isFiled
                     && _emailList.CanAttemptFileEmailToProject(_selectedEmail!)
                     && (_currentProject.CurrentProject is not null
                         || _filingProjectPicker?.IsAvailable == true),
            canMove: hasSelection
                     && isFiled
                     && _moveToProjectService?.IsAvailable == true
                     && _selectedEmail?.InboxMessageId is > 0
                     && string.IsNullOrWhiteSpace(ActionBar.MoveBlockReason));
    }

    private Task RefreshInboxAttachmentsAsync() =>
        _selectedEmail is null
            ? Task.CompletedTask
            : RefreshInboxAttachmentsAsync(_selectedEmailLoadVersion, _selectedEmail.Id);

    private async Task RefreshInboxAttachmentsAsync(int loadVersion, string messageId)
    {
        // #region agent log
        // TEMP WF-DEBUG — tagging UI visibility (ShowTagSelector)
        var taggingServicePresent = _attachmentTaggingService is not null;
        var inboxIdOnRow = _selectedEmail?.InboxMessageId;
        var projectFromContext = _currentProject.CurrentProject?.ProjectId;
        var projectFromRow = _selectedEmail?.ProjectId;
        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"refresh enter service={taggingServicePresent} inboxId={(inboxIdOnRow?.ToString() ?? "null")} projectCtx={projectFromContext?.ToString() ?? "null"} projectRow={projectFromRow?.ToString() ?? "null"} stripCount={AttachmentStrip.Attachments.Count} taskPrimary={_workSurfaceContext?.PrimaryWorkTargetEntityId?.ToString() ?? "null"}");
        // #endregion

        if (_attachmentTaggingService is null || _selectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"EARLY_EXIT no-inbox-id-or-service service={taggingServicePresent} inboxId={(inboxIdOnRow?.ToString() ?? "null")} — tagging chips stay hidden");
            // #endregion
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            // #region agent log
            WorkflowDebugTrace.Step("Email.TagUI", "EARLY_EXIT projectId<=0 — tagging chips stay hidden");
            // #endregion
            return;
        }

        var inboxAttachments = await _attachmentTaggingService
            .LoadInboxAttachmentsAsync(inboxMessageId)
            .ConfigureAwait(true);

        if (!IsCurrentSelection(messageId, loadVersion))
        {
            // #region agent log
            WorkflowDebugTrace.Step("Email.TagUI", "EARLY_EXIT selection-stale after LoadInboxAttachments");
            // #endregion
            return;
        }

        if (_inboxQuery is not null)
        {
            var inboxMessage = await _inboxQuery.GetByIdAsync(inboxMessageId).ConfigureAwait(true);
            _inboxAccProjectId = inboxMessage?.InboxAccProjectId;
            _inboxAccFolderId = inboxMessage?.InboxAccFolderId;
        }

        if (!IsCurrentSelection(messageId, loadVersion))
            return;

        var alternatives = await _attachmentTaggingService
            .LoadAlternativesAsync(projectId)
            .ConfigureAwait(true);

        if (!IsCurrentSelection(messageId, loadVersion))
        {
            // #region agent log
            WorkflowDebugTrace.Step("Email.TagUI", "EARLY_EXIT selection-stale after LoadAlternatives");
            // #endregion
            return;
        }

        var matchedInboxIds = new HashSet<int>();
        var appliedTaggable = 0;
        var skippedNotTaggable = 0;
        var unmatchedStrip = 0;
        foreach (var item in AttachmentStrip.Attachments.ToList())
        {
            if (string.Equals(item.Kind, "Loading", StringComparison.Ordinal)
                || string.Equals(item.Kind, "Unavailable", StringComparison.Ordinal))
            {
                continue;
            }

            var match = inboxAttachments.FirstOrDefault(a =>
                string.Equals(a.FileName, item.FileName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                unmatchedStrip++;
                continue;
            }

            matchedInboxIds.Add(match.InboxAttachmentId);
            if (!match.IsTaggable)
            {
                skippedNotTaggable++;
                item.ApplyInboxTagState(
                    match.InboxAttachmentId,
                    match.IsTaggable,
                    match.ProjectFileId,
                    match.ProjectFileTitle,
                    match.ProjectAlternativeId,
                    alternatives,
                    match.AccItemId);
                continue;
            }

            item.ApplyInboxTagState(
                match.InboxAttachmentId,
                match.IsTaggable,
                match.ProjectFileId,
                match.ProjectFileTitle,
                match.ProjectAlternativeId,
                alternatives,
                match.AccItemId);
            appliedTaggable++;
        }

        var appendedAcc = 0;
        foreach (var attachment in inboxAttachments.Where(a => a.IsTaggable && !matchedInboxIds.Contains(a.InboxAttachmentId)))
        {
            var item = CreateDisplayAttachmentItem(attachment.FileName, "ACC", string.Empty);
            item.ApplyInboxTagState(
                attachment.InboxAttachmentId,
                attachment.IsTaggable,
                attachment.ProjectFileId,
                attachment.ProjectFileTitle,
                attachment.ProjectAlternativeId,
                alternatives,
                attachment.AccItemId);
            AttachmentStrip.Attachments.Add(item);
            appendedAcc++;
        }

        // #region agent log
        var showTagCount = AttachmentStrip.Attachments.Count(a => a.ShowTagSelector);
        var showAltCount = AttachmentStrip.Attachments.Count(a => a.ShowAlternativeSelector);
        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"done inbox={inboxMessageId} project={projectId} sqlAtt={inboxAttachments.Count} taggableSql={inboxAttachments.Count(a => a.IsTaggable)} altCount={alternatives.Count} applied={appliedTaggable} skippedNotTaggable={skippedNotTaggable} unmatchedStrip={unmatchedStrip} appendedAcc={appendedAcc} showTag={showTagCount} showAlt={showAltCount}");
        // #endregion
    }

    private async Task OpenAttachmentInAccAsync(EmailDetailAttachmentItem item)
    {
        if (!item.CanOpenInAcc)
        {
            MessageBox.Show("הקובץ עדיין לא הועלה ל-ACC.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_inboxAccProjectId))
        {
            MessageBox.Show("מזהה פרויקט ACC לא נמצא למייל זה.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_accLauncher is null)
        {
            MessageBox.Show("שירות פתיחת ACC אינו זמין.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var url = AccResolvedDocsUrlBuilder.Build(
                _inboxAccProjectId,
                _inboxAccFolderId ?? string.Empty,
                item.AccItemId!);
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"OpenInAcc att={item.InboxAttachmentId} canOpen={item.CanOpenInAcc} projectLen={_inboxAccProjectId.Length}");
            // #endregion
            _accLauncher.Open(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בפתיחת הקובץ: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task TagAttachmentAsync(EmailDetailAttachmentItem item)
    {
        if (_attachmentTaggingService is null
            || _attachmentProjectFilePicker?.IsAvailable != true
            || _selectedEmail?.InboxMessageId is not int inboxMessageId
            || item.InboxAttachmentId <= 0)
        {
            SetStatus("בחירת קובץ פרויקט אינה זמינה.");
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            SetStatus("בחר פרויקט לפני תיוג.");
            return;
        }

        if (item.AvailableAlternatives.Count == 0)
        {
            var alternatives = await _attachmentTaggingService.LoadAlternativesAsync(projectId).ConfigureAwait(true);
            item.SetAlternatives(alternatives);
        }

        var pickedProjectFileId = await _attachmentProjectFilePicker
            .PickProjectFileAsync(projectId, item.ProjectFileId)
            .ConfigureAwait(true);

        // #region agent log
        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"H-TAG1 pick att={item.InboxAttachmentId} picked={pickedProjectFileId?.ToString() ?? "null"} selectedAlt={item.SelectedAlternativeId?.ToString() ?? "null"} alts={item.AvailableAlternatives.Count}");
        // #endregion

        if (pickedProjectFileId is not int projectFileId || projectFileId <= 0)
        {
            return;
        }

        var alternativeId = item.SelectedAlternativeId is > 0
            ? item.SelectedAlternativeId
            : EmailProjectAlternativeOption.ResolveDefaultId(item.AvailableAlternatives);

        if (alternativeId is not > 0)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"H-ALT2 no-default-alt att={item.InboxAttachmentId} pf={projectFileId}");
            // #endregion
            SetStatus("לא נמצאה אלטרנטיבה לפרויקט (צפוי «1»). רענן ונסה שוב.");
            return;
        }

        var validation = await _attachmentTaggingService.ValidateTagAsync(
            new EmailAttachmentTagValidationQuery(
                inboxMessageId,
                item.InboxAttachmentId,
                projectFileId,
                alternativeId)).ConfigureAwait(true);

        if (!validation.IsAllowed)
        {
            SetStatus(validation.BlockReason ?? "לא ניתן לתייג ליעד זה.");
            return;
        }

        if (validation.WillCreateNewVersion)
        {
            SetStatus("שים לב: קיים כבר קובץ ביעד זה — ההעברה תיצור גרסה חדשה.");
        }

        var targets = await _attachmentTaggingService.LoadTagTargetsAsync(projectId).ConfigureAwait(true);
        var targetTitle = targets.FirstOrDefault(t => t.ProjectFileId == projectFileId)?.DisplayName;

        var result = await _attachmentTaggingService.SetTagAsync(
            new EmailAttachmentTagCommand(
                item.InboxAttachmentId,
                projectFileId,
                alternativeId,
                _currentUser?.UserId ?? 0)).ConfigureAwait(true);

        // #region agent log
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"SetTag att={item.InboxAttachmentId} file='{item.FileName}' projectFileId={projectFileId} alt={alternativeId} ok={result.Succeeded} err={result.ErrorMessage ?? "(none)"}");
        // #endregion

        if (!result.Succeeded)
        {
            SetStatus(result.ErrorMessage ?? "תיוג הצרופה נכשל.");
            PresentMoveOutcomeDialog(result.ErrorMessage ?? "תיוג הצרופה נכשל.", succeeded: false);
            return;
        }

        item.ApplyTag(projectFileId, targetTitle, alternativeId);
        item.RememberCurrentAlternativeAsPrevious();
        // #region agent log
        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"H-TAG2 after-ApplyTag att={item.InboxAttachmentId} pf={item.ProjectFileId} selectedAlt={item.SelectedAlternativeId?.ToString() ?? "null"} showAlt={item.ShowAlternativeSelector}");
        // #endregion
        SetStatus("הצרופה תויגה לקובץ הפרויקט.");
        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        RefreshActionBarState();
    }

    private async Task AlternativeChangedAsync(EmailDetailAttachmentItem item)
    {
        if (item.SelectedAlternativeId == EmailProjectAlternativeOption.CreateNewId
            || item.AvailableAlternatives.Any(a => a.IsCreateNew && a.Id == item.SelectedAlternativeId))
        {
            await HandleCreateNewAlternativeAsync(item).ConfigureAwait(true);
            return;
        }

        if (item.SelectedAlternativeId is > 0)
        {
            item.RememberCurrentAlternativeAsPrevious();
        }

        if (_attachmentTaggingService is null
            || _selectedEmail?.InboxMessageId is not int inboxMessageId
            || item.ProjectFileId is not int projectFileId
            || item.InboxAttachmentId <= 0
            || item.SelectedAlternativeId is not > 0)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"H-ALT3 alt-change-skip att={item.InboxAttachmentId} pf={item.ProjectFileId?.ToString() ?? "null"} selectedAlt={item.SelectedAlternativeId?.ToString() ?? "null"} hasSvc={_attachmentTaggingService is not null}");
            // #endregion
            return;
        }

        var validation = await _attachmentTaggingService.ValidateTagAsync(
            new EmailAttachmentTagValidationQuery(
                inboxMessageId,
                item.InboxAttachmentId,
                projectFileId,
                item.SelectedAlternativeId)).ConfigureAwait(true);

        if (!validation.IsAllowed)
        {
            SetStatus(validation.BlockReason ?? "לא ניתן לשנות אלטרנטיבה ליעד זה.");
            item.RestorePreviousAlternativeSelection();
            return;
        }

        var result = await _attachmentTaggingService.SetTagAsync(
            new EmailAttachmentTagCommand(
                item.InboxAttachmentId,
                projectFileId,
                item.SelectedAlternativeId,
                _currentUser?.UserId ?? 0)).ConfigureAwait(true);

        if (!result.Succeeded)
        {
            SetStatus(result.ErrorMessage ?? "עדכון האלטרנטיבה נכשל.");
            item.RestorePreviousAlternativeSelection();
            return;
        }

        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        RefreshActionBarState();
    }

    private async Task HandleCreateNewAlternativeAsync(EmailDetailAttachmentItem item)
    {
        item.RestorePreviousAlternativeSelection();

        if (_attachmentTaggingService is null)
        {
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail?.ProjectId ?? 0;
        if (projectId <= 0)
        {
            SetStatus("בחר פרויקט לפני יצירת אלטרנטיבה.");
            return;
        }

        if (_alternativeNamePrompt?.IsAvailable != true)
        {
            SetStatus("יצירת אלטרנטיבה אינה זמינה.");
            return;
        }

        var existingNames = item.AvailableAlternatives
            .Where(a => !a.IsCreateNew)
            .Select(a => a.Name)
            .ToList();

        var name = await _alternativeNamePrompt
            .PromptForNewAlternativeNameAsync(existingNames)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var created = await _attachmentTaggingService
            .CreateAlternativeAsync(projectId, name.Trim())
            .ConfigureAwait(true);

        if (created is null)
        {
            SetStatus("יצירת האלטרנטיבה נכשלה (שם לא תקין או כבר קיים).");
            return;
        }

        var alternatives = await _attachmentTaggingService
            .LoadAlternativesAsync(projectId)
            .ConfigureAwait(true);

        foreach (var stripItem in AttachmentStrip.Attachments.Where(a => a.IsTaggable))
        {
            var keepId = stripItem == item
                ? created.Id
                : stripItem.SelectedAlternativeId;
            stripItem.SetAlternatives(alternatives);
            if (keepId is > 0 && alternatives.Any(a => a.Id == keepId))
            {
                stripItem.SelectedAlternativeId = keepId;
            }
        }

        item.SelectedAlternativeId = created.Id;
        item.RememberCurrentAlternativeAsPrevious();

        if (item.ProjectFileId is int projectFileId
            && item.InboxAttachmentId > 0
            && _selectedEmail?.InboxMessageId is int inboxMessageId)
        {
            var result = await _attachmentTaggingService.SetTagAsync(
                new EmailAttachmentTagCommand(
                    item.InboxAttachmentId,
                    projectFileId,
                    created.Id,
                    _currentUser?.UserId ?? 0)).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                SetStatus(result.ErrorMessage ?? "האלטרנטיבה נוצרה אך עדכון התיוג נכשל.");
                return;
            }
        }

        SetStatus($"נוצרה אלטרנטיבה '{created.Name}'.");
        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        RefreshActionBarState();
    }

    private bool TryResolveTaskCompletionParams(
        out string completionEventCode,
        out int actingUserId,
        out string? blockReason)
    {
        completionEventCode = string.Empty;
        actingUserId = 0;
        blockReason = null;

        if (_workSurfaceContext is null)
        {
            blockReason = "missing work surface context";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_workSurfaceContext.CompletionEventCode))
        {
            blockReason = "completion event code is not configured for this task";
            return false;
        }

        if (_currentUser?.UserId is not int userId || userId <= 0)
        {
            blockReason = "acting user is unknown";
            return false;
        }

        completionEventCode = _workSurfaceContext.CompletionEventCode;
        actingUserId = userId;
        return true;
    }

    private void PresentMoveOutcomeToUser(EmailMoveToProjectResult result)
    {
        SetStatus(result.Message);
        PresentMoveOutcomeDialog(result.Message, result.AllFilesTransferred);
    }

    private void PresentMoveOutcomeDialog(string message, bool succeeded)
    {
        // Always show a modal summary for task-driven filing so the operator cannot miss
        // deferred/failed reasons (status strip alone was closed with the work-item window).
        if (_workSurfaceContext?.TaskId is null)
            return;

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.Move",
            $"outcome dialog succeeded={succeeded} len={message.Length}");

        MessageBox.Show(
            message,
            succeeded ? "תוצאת העברה לפרויקט" : "ההעברה לא הושלמה",
            MessageBoxButton.OK,
            succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        StatusMessageChanged?.Invoke(this, message);
    }

    private bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }
}
