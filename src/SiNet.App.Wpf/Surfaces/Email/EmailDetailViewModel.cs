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
    private readonly IEmailGmailModifyService? _gmailModify;
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
        IAccResolvedDocsUrlLauncher? accLauncher = null,
        IEmailGmailModifyService? gmailModify = null)
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
        _gmailModify = gmailModify;

        AttachmentStrip = new EmailAttachmentStripViewModel(OpenExternalDownloadLink);
        ActionBar = new EmailActionBarViewModel(
            FileSelectedEmailAsync,
            MoveSelectedEmailToProjectAsync,
            OpenSelectedEmailInGmail,
            MarkSelectedEmailAsFyiAsync);
        Workflow = new EmailWorkflowActionsPaneViewModel(ExecuteSelectedWorkflowActionAsync);
        Viewer = new EmailViewerPaneViewModel(OpenBodyLink);

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

        if (row is null)
        {
            _loadedBodyMessageId = null;
            _selectionCoordinator.ClearSelectedEmailDetails();
            Viewer.Clear();
            AttachmentStrip.Clear();
            Workflow.Clear();
            RefreshActionBarState();
            return;
        }

        var loadVersion = ++_selectedEmailLoadVersion;

        // Clear body/attachments before updating the header so Subject never pairs with a
        // previous message's body during fast selection changes.
        if (!HasLoadedBodyForCurrentSelection(row.Id))
        {
            _selectionCoordinator.PrepareSelectedEmailDetailsLoading();
            SelectedAccStatusDisplay = string.Empty;
        }

        SyncViewerHeader();

        try
        {
            await RunSelectionPipelineAsync(row, loadVersion, ct).ConfigureAwait(true);
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

    private async Task RunSelectionPipelineAsync(EmailListRow row, int loadVersion, CancellationToken cancellationToken)
    {
        await _selectionCoordinator
            .LoadSelectedEmailWithAccPipelineAsync(
                row,
                loadVersion,
                HasLoadedBodyForCurrentSelection,
                cancellationToken)
            .ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        if (_externalDownloadHandler is not null
            && HasLoadedBodyForCurrentSelection(row.Id)
            && string.Equals(_selectedEmail?.Id, row.Id, StringComparison.Ordinal))
        {
            await _externalDownloadHandler
                .MergeExternalDownloadsIntoViewerAsync(row, loadVersion)
                .ConfigureAwait(true);
        }

        // DEV-016: do not mark as read on body load — only on handling completion.
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
        Viewer.SetLabelChips(EmailListRowMapper.OrderDisplayLabelChips(_selectedEmail.DisplayLabelChips));
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
        var handlerAvailable = _externalDownloadHandler?.IsAvailable == true;
        var bodyUrls = EmailExternalDownloadLinkDetector.ExtractUrls(SelectedEmailBody);
        var htmlUrls = EmailExternalDownloadLinkDetector.ExtractUrls(_selectedEmailHtmlBody);
        // Show chips whenever URLs are found (HTML or plain text). Full Jumbo→ACC pipe
        // still needs the browser host; open falls back to the system browser otherwise.
        var urls = _selectedEmail is null
            ? Array.Empty<string>()
            : MergeDistinctUrls(bodyUrls, htmlUrls);

        AttachmentStrip.SetExternalDownloadLinks(urls);
    }

    /// <summary>
    /// A link clicked inside the rendered body. The renderer already cancelled the in-place
    /// navigation, so the pane keeps showing the message; file-transfer hosts take the same route as
    /// the attachment-strip chips (download window → ACC), anything else goes to the system browser.
    /// </summary>
    private void OpenBodyLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (EmailExternalDownloadLinkDetector.IsExternalDownloadUrl(url))
        {
            OpenExternalDownloadLink(url);
            return;
        }

        OpenInSystemBrowser(url);
    }

    private void OpenExternalDownloadLink(string url)
    {
        if (_selectedEmail is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (_externalDownloadHandler?.IsAvailable == true)
        {
            _externalDownloadHandler.OpenDownloadLink(url, _selectedEmail);
            return;
        }

        if (!EmailExternalDownloadLinkDetector.IsExternalDownloadUrl(url))
        {
            SetStatus("קישור ההורדה אינו תקין.");
            return;
        }

        OpenInSystemBrowser(url);
    }

    private void OpenInSystemBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
            SetStatus($"נפתח בדפדפן: {url}");
        }
        catch (Exception ex)
        {
            SetStatus($"פתיחת קישור נכשלה: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> MergeDistinctUrls(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second)
    {
        if (first.Count == 0 && second.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>(first.Count + second.Count);
        foreach (var url in first.Concat(second))
        {
            if (seen.Add(url))
            {
                merged.Add(url);
            }
        }

        return merged;
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
        if (_workSurfaceContext is { ProjectId: > 0 } taskProjectId)
        {
            // FileMaterial / task-bound email: never open free project picker.
            if (project is null || project.ProjectId != taskProjectId.ProjectId)
            {
                SetStatus($"שיוך למשימה דורש פרויקט {taskProjectId.ProjectId} (הקשר המשימה). רענן את חלון המשימה.");
                WorkflowDebugTrace.Step(
                    "Email.File",
                    $"blocked: WorkSurface ProjectId={taskProjectId.ProjectId} current={project?.ProjectId.ToString() ?? "(null)"}");
                return;
            }

            WorkflowDebugTrace.Step(
                "Email.File",
                $"using WorkSurfaceContext.ProjectId={taskProjectId.ProjectId} (no picker)");
        }
        else if (_filingProjectPicker is { IsAvailable: true })
        {
            // Prefer explicit picker so filing never depends on (or mutates) the shell active project.
            var picked = await _filingProjectPicker.PickProjectAsync().ConfigureAwait(true);
            if (picked is null)
            {
                SetStatus("שיוך בוטל.");
                return;
            }

            project = picked;
        }
        else if (project is null)
        {
            SetStatus("בחר פרויקט לפני שיוך מייל.");
            return;
        }

        if (project is null || project.ProjectId <= 0)
        {
            SetStatus("בחר פרויקט לפני שיוך מייל.");
            return;
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
            SetStatus("העברה כבר רצה — ממתין לסיום. אם זה נמשך דקות, סגור את החלון והפעל מחדש את האפליקציה.");
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
            if (!await TryResolveEmptyAttachmentsPolicyAsync().ConfigureAwait(true))
            {
                return;
            }

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

            var mayDismissFilingSurface = false;
            string? completionBlockReason = null;
            // Close / complete only when EVERY tagged file was transferred.
            if (result.AllFilesTransferred
                && _workSurfaceContext?.TaskId is int taskId
                && _taskCompletionService is not null
                && TryResolveTaskCompletionParams(out var completionEventCode, out var actingUserId, out completionBlockReason))
            {
                var taskResultCode = ResolveMoveCompletionResultCode();
                if (string.Equals(_workSurfaceContext?.TaskTypeCode, "FollowQuoteApproval", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(taskResultCode))
                {
                    var needResult =
                        "הקבצים תויקו, אך למעקב אישור הצעה נדרשת תוצאה (אישור לקוח). תייג PDF כ־אישור_לקוח_להצעה או השלם מ־ProjectWork.";
                    SetStatus(needResult);
                    PresentMoveOutcomeDialog(needResult, succeeded: false);
                    return;
                }

                WorkflowDebugTrace.Step(
                    "Email.Move",
                    $"Complete task={taskId} event={completionEventCode} result={taskResultCode ?? "(null)"}");

                var completion = await _taskCompletionService.CompleteAsync(
                    new CompleteTaskCommand(
                        taskId,
                        completionEventCode,
                        TaskResultCode: taskResultCode,
                        CompletedTaskLinkIds: null,
                        actingUserId),
                    CancellationToken.None).ConfigureAwait(true);

                if (!completion.Success)
                {
                    var failText =
                        $"העברה הצליחה אך השלמת המשימה נכשלה: {completion.ErrorMessage ?? "unknown error"}.";
                    SetStatus(failText);
                    PresentMoveOutcomeDialog(failText, succeeded: false);
                    // Do NOT dismiss — CompleteAsync failed (FileMaterial six decisions).
                }
                else if (completion.WorkflowAdvancePending)
                {
                    var pendingText =
                        "הקבצים תויקו והמשימה נסגרה, אך מעבר ה-workflow ממתין להשלמה.\n" +
                        "החלון נשאר פתוח — ניתן לטפל דרך מסך ה-Ops / שחזור workflow הקיים.";
                    SetStatus(pendingText);
                    PresentMoveOutcomeDialog(pendingText, succeeded: false);
                    // Do NOT dismiss while advance is pending.
                }
                else if (completion.TaskClosed)
                {
                    mayDismissFilingSurface = true;
                    SetStatus("העברה והשלמת משימת התיוק הושלמו.");
                }
                else
                {
                    var incompleteText =
                        "העברה הצליחה אך המשימה לא נסגרה במלואה. החלון נשאר פתוח.";
                    SetStatus(incompleteText);
                    PresentMoveOutcomeDialog(incompleteText, succeeded: false);
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
            else if (result.AllFilesTransferred && _workSurfaceContext?.TaskId is null)
            {
                // Manual Move without a filing task — no work-item window to dismiss.
            }

            if (_selectedEmail is not null)
            {
                await _emailList.LoadAccStatusForRowAsync(_selectedEmail).ConfigureAwait(true);
                await RefreshInboxAttachmentsAsync().ConfigureAwait(true);
            }

            // Dismiss only after files transferred AND CompleteAsync succeeded with TaskClosed
            // (not WorkflowAdvancePending). INACTIVE: dismiss on AllFilesTransferred alone.
            if (mayDismissFilingSurface && _workSurfaceContext?.TaskId is not null)
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

            if (result.Succeeded
                && !string.Equals(action.ActionCode, EmailSuggestedActionCodes.FileOnly, StringComparison.Ordinal)
                && _selectedEmail is { } mailAfterWorkflow)
            {
                await MarkEmailAsReadAfterHandlingAsync(mailAfterWorkflow).ConfigureAwait(true);
            }

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
                     && string.IsNullOrWhiteSpace(ActionBar.MoveBlockReason),
            canOpenInGmail: hasSelection && !string.IsNullOrWhiteSpace(_selectedEmail?.Id),
            canMarkAsFyi: hasSelection
                          && isFiled
                          && _emailList.CanMarkAsFyi(_selectedEmail));
    }

    private void OpenSelectedEmailInGmail()
    {
        if (_selectedEmail is null || string.IsNullOrWhiteSpace(_selectedEmail.Id))
        {
            SetStatus("לא נבחר מייל.");
            return;
        }

        var url = GmailMessageUrlBuilder.Build(
            _selectedEmail.Id,
            _emailList.ConnectedAccountEmail);
        OpenInSystemBrowser(url);
    }

    /// <summary>
    /// DEV-016: remove Gmail <c>UNREAD</c> after handling finishes (Workflow / explicit call).
    /// Optimistic local update with rollback — mailbox remains the source of truth.
    /// </summary>
    private async Task MarkEmailAsReadAfterHandlingAsync(EmailListRow row)
    {
        if (_gmailModify is null
            || !row.IsUnread
            || string.IsNullOrWhiteSpace(row.Id))
        {
            return;
        }

        var patched = _emailList.PatchRowIsUnread(row.Id, isUnread: false);
        if (patched is not null
            && string.Equals(_selectedEmail?.Id, patched.Id, StringComparison.Ordinal))
        {
            _selectedEmail = patched;
        }

        try
        {
            await _gmailModify.MarkAsReadAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            var reverted = _emailList.PatchRowIsUnread(row.Id, isUnread: true);
            if (reverted is not null
                && string.Equals(_selectedEmail?.Id, reverted.Id, StringComparison.Ordinal))
            {
                _selectedEmail = reverted;
            }

            SetStatus($"סימון כנקרא נכשל: {ex.Message}");
        }
    }

    private async Task MarkSelectedEmailAsFyiAsync()
    {
        if (_selectedEmail is null)
        {
            SetStatus("לא נבחר מייל.");
            return;
        }

        await _emailList.MarkAsFyiAsync(_selectedEmail).ConfigureAwait(true);
        RefreshActionBarState();
    }

    private Task RefreshInboxAttachmentsAsync() =>
        _selectedEmail is null
            ? Task.CompletedTask
            : RefreshInboxAttachmentsAsync(_selectedEmailLoadVersion, _selectedEmail.Id);

    /// <summary>
    /// Resolves the SQL <c>EmailInboxMessage</c> id that belongs to the selected Gmail row.
    /// Order: row patch → identity lookup → task primary only when this row is the pending task target.
    /// </summary>
    private async Task<int?> ResolveInboxMessageIdForSelectedAsync(EmailListRow selected)
    {
        if (selected.InboxMessageId is int rowInboxId && rowInboxId > 0)
        {
            return rowInboxId;
        }

        if (_inboxQuery is not null
            && (!string.IsNullOrWhiteSpace(selected.InternetMessageId)
                || !string.IsNullOrWhiteSpace(selected.Id)))
        {
            var byIdentity = await _inboxQuery
                .FindByMessageIdentityAsync(selected.InternetMessageId, selected.Id)
                .ConfigureAwait(true);
            if (byIdentity is not null && byIdentity.Id > 0)
            {
                return byIdentity.Id;
            }
        }

        // Late-reload safety for the task anchor only — never for a sibling reply.
        if (_workSurfaceContext?.PrimaryWorkTargetEntityId is int primary
            && primary > 0
            && IsPendingTaskTargetRow(selected))
        {
            return primary;
        }

        return null;
    }

    private bool IsPendingTaskTargetRow(EmailListRow selected)
    {
        var pending = _emailList.PendingTaskSelection;
        if (pending is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pending.MessageUniqueId)
            || !string.IsNullOrWhiteSpace(pending.InternetMessageId))
        {
            if (EmailMessageIdMatcher.Matches(selected.InternetMessageId, pending.InternetMessageId)
                || EmailMessageIdMatcher.Matches(selected.InternetMessageId, pending.MessageUniqueId))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(pending.MessageUniqueId))
            {
                var selectedUnique = EmailMessageIdentity.GetMessageUniqueId(
                    selected.InternetMessageId,
                    selected.Id);
                if (string.Equals(selectedUnique, pending.MessageUniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// After ACC upload patches the list row (same Gmail id), sync detail state and reload
    /// AccItemId so double-click open-in-ACC works without re-selecting the email.
    /// </summary>
    public async Task SyncSelectedRowFromListAndRefreshAttachmentsAsync()
    {
        var listRow = _emailList.SelectedEmail;
        if (listRow is null
            || _selectedEmail is null
            || !string.Equals(listRow.Id, _selectedEmail.Id, StringComparison.Ordinal))
        {
            return;
        }

        _selectedEmail = listRow;
        await RefreshInboxAttachmentsAsync().ConfigureAwait(true);
    }

    private async Task RefreshInboxAttachmentsAsync(int loadVersion, string messageId)
    {
        // #region agent log
        var taggingServicePresent = _attachmentTaggingService is not null;
        var projectFromContext = _currentProject.CurrentProject?.ProjectId;
        var projectFromRow = _selectedEmail?.ProjectId;
        // #endregion

        // Resolve inbox id for THIS selected Gmail message only. Never borrow the task
        // PrimaryWorkTargetEntityId for a different reply in the same thread (same filename
        // would then tag the anchor's SQL attachment).
        var inboxMessageId = _selectedEmail is null
            ? null
            : await ResolveInboxMessageIdForSelectedAsync(_selectedEmail).ConfigureAwait(true);

        // #region agent log
        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"refresh enter service={taggingServicePresent} inboxId={(inboxMessageId?.ToString() ?? "null")} rowInbox={(_selectedEmail?.InboxMessageId?.ToString() ?? "null")} projectCtx={projectFromContext?.ToString() ?? "null"} projectRow={projectFromRow?.ToString() ?? "null"} stripCount={AttachmentStrip.Attachments.Count} taskPrimary={_workSurfaceContext?.PrimaryWorkTargetEntityId?.ToString() ?? "null"}");
        // #endregion

        if (!IsCurrentSelection(messageId, loadVersion))
        {
            return;
        }

        if (_attachmentTaggingService is null || inboxMessageId is not int resolvedInboxId)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"EARLY_EXIT no-inbox-id-or-service service={taggingServicePresent} inboxId={(inboxMessageId?.ToString() ?? "null")} — tagging chips stay hidden");
            // #endregion
            return;
        }

        if (_selectedEmail is not null
            && _selectedEmail.InboxMessageId != resolvedInboxId
            && !string.IsNullOrWhiteSpace(_selectedEmail.Id))
        {
            var patched = _emailList.PatchRowInboxMessageId(_selectedEmail.Id, resolvedInboxId);
            if (patched is not null)
                _selectedEmail = patched;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail?.ProjectId ?? 0;

        var inboxAttachments = await _attachmentTaggingService
            .LoadInboxAttachmentsAsync(resolvedInboxId)
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
            var inboxMessage = await _inboxQuery.GetByIdAsync(resolvedInboxId).ConfigureAwait(true);
            _inboxAccProjectId = inboxMessage?.InboxAccProjectId;
            _inboxAccFolderId = inboxMessage?.InboxAccFolderId;
        }

        if (!IsCurrentSelection(messageId, loadVersion))
            return;

        // Alternatives/tagging need a project; AccItemId open-in-ACC must still work without one.
        IReadOnlyList<EmailProjectAlternativeOption> alternatives = [];
        if (projectId > 0)
        {
            alternatives = await _attachmentTaggingService
                .LoadAlternativesAsync(projectId)
                .ConfigureAwait(true);

            if (!IsCurrentSelection(messageId, loadVersion))
            {
                // #region agent log
                WorkflowDebugTrace.Step("Email.TagUI", "EARLY_EXIT selection-stale after LoadAlternatives");
                // #endregion
                return;
            }
        }
        else
        {
            // #region agent log
            WorkflowDebugTrace.Step("Email.TagUI", "projectId<=0 — applying AccItemId open state without tagging alternatives");
            // #endregion
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
            $"done inbox={resolvedInboxId} project={projectId} sqlAtt={inboxAttachments.Count} taggableSql={inboxAttachments.Count(a => a.IsTaggable)} altCount={alternatives.Count} applied={appliedTaggable} skippedNotTaggable={skippedNotTaggable} unmatchedStrip={unmatchedStrip} appendedAcc={appendedAcc} showTag={showTagCount} showAlt={showAltCount}");
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
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"H-TAG0 unavailable tagging={_attachmentTaggingService is not null} picker={_attachmentProjectFilePicker?.IsAvailable == true} inbox={_selectedEmail?.InboxMessageId?.ToString() ?? "null"} att={item.InboxAttachmentId}");
            // #endregion
            const string unavailable = "בחירת קובץ פרויקט אינה זמינה ב-host הנוכחי.";
            SetStatus(unavailable);
            MessageBox.Show(unavailable, "תיוג צרופה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            const string needProject = "בחר פרויקט לפני תיוג.";
            SetStatus(needProject);
            MessageBox.Show(needProject, "תיוג צרופה", MessageBoxButton.OK, MessageBoxImage.Information);
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

        if (item.SelectedAlternativeId == EmailProjectAlternativeOption.CreateNewId
            || item.AvailableAlternatives.Any(a => a.IsCreateNew && a.Id == item.SelectedAlternativeId))
        {
            SetStatus("בחר או צור אלטרנטיבה לפני תיוג (אל תשאיר «+ חדש...»).");
            return;
        }

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
        if (string.Equals(_workSurfaceContext?.TaskTypeCode, "FollowQuoteApproval", StringComparison.Ordinal)
            && IsQuoteClientApprovalTitle(targetTitle))
        {
            WorkflowDebugTrace.Step(
                "FollowQuote.Tag",
                $"task={_workSurfaceContext?.TaskId} tagged QuoteClientApproval att={item.InboxAttachmentId}");
        }
        else
        {
            SetStatus("הצרופה תויגה לקובץ הפרויקט.");
        }

        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        RefreshActionBarState();

        if (string.Equals(_workSurfaceContext?.TaskTypeCode, "FollowQuoteApproval", StringComparison.Ordinal)
            && HasTaggedQuoteClientApproval())
        {
            await TryAutoCompleteFollowQuoteWhenReadyAsync().ConfigureAwait(true);
        }
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

        var projectId = _workSurfaceContext is { ProjectId: > 0 } ctxPid
            ? ctxPid.ProjectId
            : _currentProject.CurrentProject?.ProjectId ?? _selectedEmail?.ProjectId ?? 0;
        if (projectId <= 0)
        {
            SetStatus("בחר פרויקט לפני יצירת אלטרנטיבה.");
            return;
        }

        if (_alternativeNamePrompt is null)
        {
            SetStatus("יצירת אלטרנטיבה אינה זמינה (חסר מארח פרומפט).");
            WorkflowDebugTrace.Step("Email.TagUI", "create-alt blocked: alternativeNamePrompt null");
            return;
        }

        if (!_alternativeNamePrompt.IsAvailable)
        {
            SetStatus("יצירת אלטרנטיבה אינה זמינה.");
            WorkflowDebugTrace.Step("Email.TagUI", "create-alt blocked: prompt IsAvailable=false");
            return;
        }

        var existingNames = item.AvailableAlternatives
            .Where(a => !a.IsCreateNew)
            .Select(a => a.Name)
            .ToList();

        WorkflowDebugTrace.Step("Email.TagUI", $"create-alt prompt project={projectId} existing={existingNames.Count}");
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

    /// <summary>
    /// FollowQuoteApproval: Move after tagging QuoteClientApproval completes as client approval.
    /// Other multi-result tasks keep null (caller must not auto-complete without a picker).
    /// </summary>
    private string? ResolveMoveCompletionResultCode()
    {
        if (!string.Equals(_workSurfaceContext?.TaskTypeCode, "FollowQuoteApproval", StringComparison.Ordinal))
        {
            return null;
        }

        return HasTaggedQuoteClientApproval() ? "QuoteApprovedByClient" : null;
    }

    private bool HasTaggedQuoteClientApproval() =>
        AttachmentStrip.Attachments.Any(a =>
            a.IsTagged && IsQuoteClientApprovalTitle(a.TaggedProjectFileTitle));

    private bool AreAllTaggableAttachmentsTagged()
    {
        var taggable = AttachmentStrip.Attachments
            .Where(a => a.IsTaggable && a.InboxAttachmentId > 0)
            .ToList();
        return taggable.Count > 0 && taggable.All(a => a.IsTagged && a.ProjectFileId is > 0);
    }

    /// <summary>
    /// FollowQuote: only after every taggable attachment has a target AND QuoteClientApproval is
    /// among them — File (if needed) → Move → Complete Approved. Partial tagging never unlocks Move.
    /// </summary>
    private async Task TryAutoCompleteFollowQuoteWhenReadyAsync()
    {
        if (_selectedEmail is null || !HasTaggedQuoteClientApproval())
        {
            return;
        }

        if (!AreAllTaggableAttachmentsTagged())
        {
            var remaining = AttachmentStrip.Attachments.Count(a =>
                a.IsTaggable && a.InboxAttachmentId > 0 && !a.IsTagged);
            SetStatus(
                $"אישור לקוח תויג. נותרו {remaining} צרופות בלי יעד — תייג את כולן, ואז ההעברה והשלמת המשימה ימשיכו.");
            WorkflowDebugTrace.Step(
                "FollowQuote.Auto",
                $"waiting — untagged={remaining} task={_workSurfaceContext?.TaskId}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ActionBar.MoveBlockReason))
        {
            SetStatus(ActionBar.MoveBlockReason);
            WorkflowDebugTrace.Step(
                "FollowQuote.Auto",
                $"blocked by eligibility: {ActionBar.MoveBlockReason}");
            return;
        }

        SetStatus("כל הצרופות מתויגות — משייך ומעביר לפרויקט ומשלים את המשימה…");
        if (!_selectedEmail.IsFiledToProject)
        {
            WorkflowDebugTrace.Step("FollowQuote.Auto", "all tagged — ensure Gmail File then Move");
            await FileSelectedEmailAsync().ConfigureAwait(true);
            return;
        }

        WorkflowDebugTrace.Step("FollowQuote.Auto", "all tagged — Move after QuoteClientApproval");
        await MoveSelectedEmailToProjectAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// When there are no business attachments to file, ask: include email body PDF / confirm no material / back.
    /// Returns <c>false</c> when Move should abort (Back, or after "no material" completion path).
    /// </summary>
    private async Task<bool> TryResolveEmptyAttachmentsPolicyAsync()
    {
        static bool IsEmailBodyPdf(EmailDetailAttachmentItem a) =>
            string.Equals(a.FileName, "00_Email.pdf", StringComparison.OrdinalIgnoreCase);

        var strip = AttachmentStrip.Attachments;
        var businessItems = strip.Where(a => a.IsTaggable && a.InboxAttachmentId > 0 && !IsEmailBodyPdf(a)).ToList();
        var bodyItem = strip.FirstOrDefault(a => a.InboxAttachmentId > 0 && IsEmailBodyPdf(a));

        if (businessItems.Count > 0)
        {
            return true;
        }

        // No business attachments — require explicit choice when a filing task is active.
        if (_workSurfaceContext?.TaskId is null)
        {
            return true;
        }

        var choice = MessageBox.Show(
            "אין צרופות עסקיות למייל זה.\n\n" +
            "כן — לכלול את «תוכן המייל (PDF)» בתיוק (יש לבחור יעד לקובץ).\n" +
            "לא — לאשר שאין חומר ולהשלים את המשימה.\n" +
            "ביטול — חזרה בלי פעולה.",
            "תיוק חומר — ללא צרופות",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
        {
            SetStatus("ההעברה בוטלה.");
            return false;
        }

        if (choice == MessageBoxResult.No)
        {
            // Confirm no material → Complete → MaterialCheck without Move.
            if (_taskCompletionService is null)
            {
                SetStatus("לא ניתן לאשר «אין חומר»: שירות השלמה לא זמין.");
                PresentMoveOutcomeDialog(StatusMessage, succeeded: false);
                return false;
            }

            if (!TryResolveTaskCompletionParams(out var eventCode, out var userId, out var blockReason))
            {
                SetStatus($"לא ניתן לאשר «אין חומר»: {blockReason ?? "פרמטרי השלמה חסרים"}.");
                PresentMoveOutcomeDialog(StatusMessage, succeeded: false);
                return false;
            }

            var taskId = _workSurfaceContext.TaskId.Value;
            WorkflowDebugTrace.Step("Email.Move", $"no-material confirm task={taskId}");
            var completion = await _taskCompletionService.CompleteAsync(
                new CompleteTaskCommand(taskId, eventCode, TaskResultCode: null, CompletedTaskLinkIds: null, userId),
                CancellationToken.None).ConfigureAwait(true);

            if (!completion.Success)
            {
                var fail = $"אישור «אין חומר» נכשל: {completion.ErrorMessage ?? "unknown"}.";
                SetStatus(fail);
                PresentMoveOutcomeDialog(fail, succeeded: false);
                return false;
            }

            if (completion.WorkflowAdvancePending)
            {
                var pending =
                    "אושר שאין חומר והמשימה נסגרה, אך מעבר ה-workflow ממתין.\nהחלון נשאר פתוח.";
                SetStatus(pending);
                PresentMoveOutcomeDialog(pending, succeeded: false);
                return false;
            }

            if (completion.TaskClosed)
            {
                SetStatus("אושר שאין חומר — המשימה הושלמה.");
                TryDismissFilingSurface();
            }

            return false;
        }

        // Yes — include body PDF.
        if (bodyItem is null)
        {
            SetStatus("תוכן המייל (PDF) עדיין לא זמין ב-ACC. העלה ל-Inbox ואז בחר יעד לתיוק.");
            PresentMoveOutcomeDialog(StatusMessage, succeeded: false);
            return false;
        }

        if (!bodyItem.IsTagged)
        {
            SetStatus("בחר יעד («בחר קובץ») עבור «תוכן המייל (PDF)» ואז העבר שוב.");
            PresentMoveOutcomeDialog(StatusMessage, succeeded: false);
            return false;
        }

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

    private static bool IsQuoteClientApprovalTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title)
        && (title.Contains("אישור_לקוח", StringComparison.Ordinal)
            || title.Contains("QuoteClientApproval", StringComparison.OrdinalIgnoreCase));

    private bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }
}
