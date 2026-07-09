using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Surfaces.Email.Detail;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

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
    private readonly ITaskCompletionService? _taskCompletionService;
    private readonly IEmailBodyRenderer? _bodyRenderer;
    private EmailExternalDownloadHandler? _externalDownloadHandler;
    private readonly EmailDetailSelectionCoordinator _selectionCoordinator;

    private EmailListRow? _selectedEmail;
    private WorkSurfaceContext? _workSurfaceContext;
    private int _selectedEmailLoadVersion;
    private string _selectedEmailBody = string.Empty;
    private string _selectedAccStatusDisplay = string.Empty;
    private bool _isBusy;

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
        IEmailBodyRenderer? bodyRenderer = null)
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

        AttachmentStrip = new EmailAttachmentStripViewModel(OpenExternalDownloadLink);
        ActionBar = new EmailActionBarViewModel(FileSelectedEmailAsync, MoveSelectedEmailToProjectAsync);
        Workflow = new EmailWorkflowActionsPaneViewModel(ExecuteSelectedWorkflowActionAsync);
        Viewer = new EmailViewerPaneViewModel();

        _selectionCoordinator = new EmailDetailSelectionCoordinator(
            emailGateway,
            _emailList,
            message => StatusMessage = message,
            body => SelectedEmailBody = body,
            acc => SelectedAccStatusDisplay = acc,
            AttachmentStrip.Attachments,
            () => _selectedEmail,
            () => _selectedEmailLoadVersion,
            () => _selectedEmailLoadVersion++);

        UpdateActiveProjectDisplay(_currentProject.CurrentProject);
    }

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

    public string StatusMessage { get; private set; } = string.Empty;

    public string SelectedEmailBody
    {
        get => _selectedEmailBody;
        private set
        {
            if (SetField(ref _selectedEmailBody, value))
            {
                Viewer.SyncFromBody(value, _bodyRenderer, _selectedEmail?.Id);
                RefreshExternalDownloadActionVisibility();
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
        _selectedEmail = row;
        OnPropertyChanged(nameof(HasSelectedEmail));
        SyncViewerHeader();

        if (row is null)
        {
            _selectionCoordinator.ClearSelectedEmailDetails();
            Workflow.Clear();
            RefreshActionBarState();
            return;
        }

        var loadVersion = ++_selectedEmailLoadVersion;

        if (!HasLoadedBodyForCurrentSelection(row.Id))
        {
            _selectionCoordinator.PrepareSelectedEmailDetailsLoading();
            SelectedAccStatusDisplay = string.Empty;
        }

        await RunSelectionPipelineAsync(row, loadVersion).ConfigureAwait(true);
        await RefreshWorkflowContextAsync().ConfigureAwait(true);
        await RefreshMoveEligibilityAsync().ConfigureAwait(true);
        RefreshActionBarState();
    }

    public void ApplyWorkSurfaceContext(WorkSurfaceContext? context) =>
        _workSurfaceContext = context;

    public void ClearOnDisconnect()
    {
        _selectedEmail = null;
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
        RefreshActionBarState();
    }

    public Task OpenSelectedEmailAsync() => _selectionCoordinator.OpenSelectedEmailAsync();

    public void Dispose()
    {
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
        string.Equals(_selectedEmail?.Id, messageId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedEmailBody)
        && !string.Equals(SelectedEmailBody, "טוען תוכן מייל...", StringComparison.Ordinal);

    private void RefreshExternalDownloadActionVisibility() =>
        AttachmentStrip.ShowExternalDownloadLinkAction =
            EmailExternalDownloadLinkDetector.HasExternalDownloadLink(SelectedEmailBody)
            && _selectedEmail is not null
            && _externalDownloadHandler?.IsAvailable == true;

    private void OpenExternalDownloadLink()
    {
        if (_selectedEmail is null || _externalDownloadHandler is null)
        {
            return;
        }

        _externalDownloadHandler.OpenFirstDownloadLink(SelectedEmailBody, _selectedEmail);
    }

    private async Task FileSelectedEmailAsync()
    {
        if (_filingService is null || _selectedEmail is null)
        {
            SetStatus("שיוך לפרויקט אינו זמין.");
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            SetStatus("בחר פרויקט לפני שיוך.");
            return;
        }

        var userId = _currentUser?.UserId ?? 0;
        IsBusy = true;
        try
        {
            var result = await _filingService.FileToProjectAsync(
                new FileEmailToProjectCommand(
                    projectId,
                    userId,
                    _selectedEmail.Id,
                    _selectedEmail.InboxMessageId,
                    _selectedEmail.ThreadId,
                    _selectedEmail.InternetMessageId,
                    _workSurfaceContext?.TaskId)).ConfigureAwait(true);

            SetStatus(result.Succeeded
                ? "המייל שויך לפרויקט."
                : result.ErrorMessage ?? "שיוך המייל נכשל.");
        }
        finally
        {
            IsBusy = false;
            RefreshActionBarState();
        }
    }

    private async Task MoveSelectedEmailToProjectAsync()
    {
        if (_moveToProjectService is null || _selectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            SetStatus("MoveToProject אינו זמין.");
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            SetStatus("בחר פרויקט לפני העברה.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _moveToProjectService.MoveAsync(
                new EmailMoveToProjectDetailCommand(
                    inboxMessageId,
                    projectId,
                    _workSurfaceContext?.TaskId,
                    _workSurfaceContext?.CompletionEventCode)).ConfigureAwait(true);

            SetStatus(result.Message);

            if (result.Succeeded && _workSurfaceContext?.TaskId is int taskId && _taskCompletionService is not null)
            {
                await _taskCompletionService.CompleteAsync(
                    new CompleteTaskCommand(
                        taskId,
                        _workSurfaceContext.CompletionEventCode ?? "ReviewMaterialFiled",
                        TaskResultCode: null,
                        CompletedTaskLinkIds: null,
                        _currentUser?.UserId ?? 0),
                    CancellationToken.None).ConfigureAwait(true);
            }

            if (_selectedEmail is not null)
            {
                await _emailList.LoadAccStatusForRowAsync(_selectedEmail).ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshMoveEligibilityAsync().ConfigureAwait(true);
            RefreshActionBarState();
        }
    }

    private async Task RefreshMoveEligibilityAsync()
    {
        if (_moveEligibility is null || _selectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            ActionBar.MoveBlockReason = null;
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail.ProjectId ?? 0;
        var eligibility = await _moveEligibility.EvaluateAsync(
            new EmailMoveToProjectEligibilityQuery(
                inboxMessageId,
                projectId,
                _selectedEmail.AttachmentCount,
                _selectedEmail.ProjectId is > 0)).ConfigureAwait(true);

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
        try
        {
            var context = await _workflowContextService.AnalyzeAsync(
                new EmailWorkflowContextQuery(
                    _selectedEmail.InboxMessageId,
                    _selectedEmail.Id,
                    _workSurfaceContext?.ProjectId ?? _currentProject.CurrentProject?.ProjectId)).ConfigureAwait(true);

            var actions = context is null
                ? Array.Empty<EmailSuggestedActionDto>()
                : _suggestedActionService.BuildActions(context);

            Workflow.ApplyContext(context, actions);
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
                    _currentUser?.UserId ?? 0)).ConfigureAwait(true);

            Workflow.StatusMessage = result.Message ?? (result.Succeeded ? "הפעולה הושלמה." : "הפעולה נכשלה.");
            await RefreshWorkflowContextAsync().ConfigureAwait(true);
        }
        finally
        {
            Workflow.IsLoading = false;
        }
    }

    private void RefreshActionBarState()
    {
        var hasSelection = _selectedEmail is not null;
        var projectId = _currentProject.CurrentProject?.ProjectId ?? _selectedEmail?.ProjectId ?? 0;
        ActionBar.RefreshCommandStates(
            canFile: hasSelection && projectId > 0 && _filingService is not null,
            canMove: hasSelection
                     && _moveToProjectService?.IsAvailable == true
                     && _selectedEmail?.InboxMessageId is > 0
                     && string.IsNullOrWhiteSpace(ActionBar.MoveBlockReason));
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
