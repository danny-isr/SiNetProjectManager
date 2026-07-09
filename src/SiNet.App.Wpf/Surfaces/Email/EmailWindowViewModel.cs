using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.WorkSurfaces;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// View model for <see cref="EmailWindowView"/> — the first real read-only New System slice of the
/// legacy <c>EmailManagementView</c> (email management window).
/// </summary>
public sealed partial class EmailWindowViewModel : ObservableObject, IDisposable
{
    private readonly IProjectQueryService _projectQuery;
    private readonly ICurrentProjectContext _currentProject;
    private readonly IEmailGateway _emailGateway;
    private readonly IConnectorAuthService _googleAuthService;
    private readonly IEmailInboxQueryService? _emailInboxQuery;
    private readonly IEmailMoveToProjectCoordinator? _moveToProjectCoordinator;
    private readonly EmailWindowSelectionHandler _selectionHandler;
    private readonly EmailExternalDownloadHandler? _externalDownloadHandler;
    private readonly IEmailAccClosePrompt? _accClosePrompt;

    private WorkSurfaceContext? _workSurfaceContext;
    private EmailFolderRow? _selectedFolder;
    private string? _selectedStatus;
    private string _selectedEmailBody = string.Empty;
    private bool _isBusy;
    private int _selectedEmailLoadVersion;
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private string _statusMessage = "חבר Gmail ולחץ רענן כדי לטעון את כל המיילים.";
    private string _selectedAccStatusDisplay = string.Empty;

    public EmailWindowViewModel()
        : this(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            new InMemoryCurrentProjectContext(),
            new DesignEmailGateway(),
            new DesignConnectorAuthService())
    {
    }

    public EmailWindowViewModel(
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptions,
        ICurrentProjectContext currentProject)
        : this(
            projectQuery,
            filterOptions,
            currentProject,
            new DesignEmailGateway(),
            new DesignConnectorAuthService())
    {
    }

    public EmailWindowViewModel(
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptions,
        ICurrentProjectContext currentProject,
        IEmailGateway emailGateway,
        IConnectorAuthService googleAuthService)
        : this(projectQuery, filterOptions, currentProject, emailGateway, googleAuthService, emailInboxQuery: null, threadLinkQuery: null)
    {
    }

    public EmailWindowViewModel(
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptions,
        ICurrentProjectContext currentProject,
        IEmailGateway emailGateway,
        IConnectorAuthService googleAuthService,
        IEmailInboxQueryService? emailInboxQuery,
        IEmailThreadLinkQueryService? threadLinkQuery)
        : this(
            projectQuery,
            filterOptions,
            currentProject,
            emailGateway,
            googleAuthService,
            emailInboxQuery,
            threadLinkQuery,
            filingService: null,
            statusService: null,
            currentUser: null,
            accStatusService: null,
            accUploadCoordinator: null,
            moveToProjectCoordinator: null,
            externalDownloadCoordinator: null,
            externalDownloadBrowserHost: null,
            accIngestQueue: null,
            ingestSessionEnsurer: null,
            backgroundWorkTracker: null,
            accClosePrompt: null)
    {
    }

    public EmailWindowViewModel(
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptions,
        ICurrentProjectContext currentProject,
        IEmailGateway emailGateway,
        IConnectorAuthService googleAuthService,
        IEmailInboxQueryService? emailInboxQuery,
        IEmailThreadLinkQueryService? threadLinkQuery,
        IEmailFilingService? filingService,
        IEmailStatusService? statusService,
        ICurrentUserContext? currentUser,
        IEmailAccStatusService? accStatusService = null,
        IEmailAccUploadCoordinator? accUploadCoordinator = null,
        IEmailMoveToProjectCoordinator? moveToProjectCoordinator = null,
        IEmailExternalDownloadCoordinator? externalDownloadCoordinator = null,
        IEmailExternalDownloadBrowserHost? externalDownloadBrowserHost = null,
        IEmailAccIngestQueue? accIngestQueue = null,
        IGoogleIngestSessionEnsurer? ingestSessionEnsurer = null,
        IEmailAccBackgroundWorkTracker? backgroundWorkTracker = null,
        IEmailAccClosePrompt? accClosePrompt = null,
        IEmailThreadMappingSyncService? threadMappingSync = null)
    {
        ArgumentNullException.ThrowIfNull(projectQuery);
        ArgumentNullException.ThrowIfNull(filterOptions);
        _projectQuery = projectQuery;
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));
        _emailInboxQuery = emailInboxQuery;
        _moveToProjectCoordinator = moveToProjectCoordinator;
        _accClosePrompt = accClosePrompt;

        Folders = new ObservableCollection<EmailFolderRow>(EmailWindowDesignData.SampleFolders);
        StatusOptions = new ObservableCollection<string>(EmailWindowDesignData.SampleStatuses);
        Attachments = [];

        EmailList = new EmailListViewModel(
            _emailGateway,
            threadLinkQuery,
            _googleAuthService,
            filingService,
            statusService,
            _currentProject,
            currentUser,
            accStatusService,
            accUploadCoordinator,
            moveToProjectCoordinator,
            accIngestQueue,
            ingestSessionEnsurer,
            threadMappingSync);

        _selectionHandler = new EmailWindowSelectionHandler(
            _emailGateway,
            EmailList,
            message => StatusMessage = message,
            body => SelectedEmailBody = body,
            acc => SelectedAccStatusDisplay = acc,
            Attachments,
            () => SelectedEmail,
            () => _selectedEmailLoadVersion,
            () => _selectedEmailLoadVersion++);

        _externalDownloadHandler = externalDownloadCoordinator is not null && externalDownloadBrowserHost is not null
            ? new EmailExternalDownloadHandler(
                externalDownloadCoordinator,
                externalDownloadBrowserHost,
                backgroundWorkTracker,
                EmailList,
                _selectionHandler,
                message => StatusMessage = message,
                () => SelectedEmail,
                () => _selectedEmailLoadVersion,
                () => _selectedEmailLoadVersion++)
            : null;

        EmailList.SelectedEmailChanged += OnEmailListSelectionChanged;
        EmailList.StatusMessageChanged += (_, message) => StatusMessage = message;
        EmailList.AccStatusPatched += (_, display) => SelectedAccStatusDisplay = display;
        EmailList.AccountStatusChanged += (_, _) => RefreshAuthDisplay();

        _selectedFolder = Folders.FirstOrDefault();
        _selectedStatus = StatusOptions.FirstOrDefault();

        ProjectSelector = new ProjectSelectorViewModel(projectQuery, filterOptions, _currentProject);
        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
        _googleAuthService.AuthStateChanged += OnAuthStateChanged;
        UpdateActiveProjectDisplay(_currentProject.CurrentProject);
        _ = ApplyProjectContextFromWorkbenchAsync();
        _ = ProjectSelector.InitializeAsync();
        _ = EmailList.InitializeAsync();

        RefreshCommand = EmailList.RefreshPageCommand;
        SearchCommand = EmailList.ApplyFiltersCommand;
        ClearSearchCommand = EmailList.ClearFiltersCommand;
        OpenEmailCommand = new AsyncRelayCommand(
            () => _selectionHandler.OpenSelectedEmailAsync(),
            () => !IsBusy && SelectedEmail is not null);
        LinkToProjectCommand = DeferredProductionPilotAction("שיוך בפועל לפרויקט — מושהה (production pilot read-only).");
        CreateTaskFromEmailCommand = DeferredProductionPilotAction("יצירת משימה מהמייל — מושהה (דורש slice Workflow/Tasks).");
        MarkHandledCommand = _moveToProjectCoordinator?.IsAvailable == true
            ? new AsyncRelayCommand(MoveSelectedEmailToProjectAsync, () => SelectedEmail?.InboxMessageId is > 0)
            : DeferredProductionPilotAction("Move-to-project / mark-handled — מושהה (production pilot read-only).");
        ArchiveCommand = DeferredProductionPilotAction("ארכוב — מושהה (production pilot read-only).");
        ReplyCommand = DeferredProductionPilotAction("Reply/Send — מושהה (דורש G-Policy).");
        ForwardCommand = DeferredProductionPilotAction("Forward/Send — מושהה (דורש G-Policy).");
        OpenAttachmentCommand = DeferredProductionPilotAction("פתיחת attachment — מושהה (metadata-only pilot).");
        CompleteTaskCommand = DeferredProductionPilotAction("סיום משימה — מושהה (דורש ITaskCompletionCoordinator slice).");
        OpenExternalDownloadLinkCommand = new RelayCommand(
            _ => OpenExternalDownloadLink(),
            _ => HasExternalDownloadLinks && SelectedEmail is not null && _externalDownloadHandler?.IsAvailable == true);
    }

    public string Title => "ניהול דואר — Gmail + ACC Inbox";
    public bool ShowDeferredWriteActions => false;
    public bool ShowDeferredVisualPlaceholders => false;

    public string ProductionPilotNotice { get; } =
        "בחירת מייל מעלה אוטומטית ל-ACC Inbox (כמו המערכת הישנה). תיוק Gmail, MoveToProject ושליחה יחוברו בהמשך.";

    public string UnreadCountDisplay => EmailList.UnreadCountDisplay;
    public bool ShowUnreadCount => EmailList.ShowUnreadCount;
    public EmailListViewModel EmailList { get; }
    public ObservableCollection<EmailListRow> Emails => EmailList.Emails;
    public ProjectSelectorViewModel ProjectSelector { get; }

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public ObservableCollection<EmailFolderRow> Folders { get; }
    public ObservableCollection<string> StatusOptions { get; }
    public ObservableCollection<EmailAttachmentRow> Attachments { get; }
    public WorkSurfaceContext? WorkSurfaceContext => _workSurfaceContext;
    public bool IsConnected => _googleAuthService.IsAuthenticated;

    public string RuntimeSummary =>
        IsConnected
            ? "Gmail מחובר — תיוק Gmail + העלאה ל-ACC Inbox"
            : "Gmail לא מחובר";

    public string SearchText
    {
        get => EmailList.SearchText;
        set => EmailList.SearchText = value;
    }

    public EmailFolderRow? SelectedFolder
    {
        get => _selectedFolder;
        set => SetField(ref _selectedFolder, value);
    }

    public string? SelectedStatus
    {
        get => _selectedStatus;
        set => SetField(ref _selectedStatus, value);
    }

    public EmailListRow? SelectedEmail
    {
        get => EmailList.SelectedEmail;
        set => EmailList.SelectedEmail = value;
    }

    public bool HasSelectedEmail => EmailList.SelectedEmail is not null;

    public string SelectedEmailBody
    {
        get => _selectedEmailBody;
        private set
        {
            if (SetField(ref _selectedEmailBody, value))
            {
                OnPropertyChanged(nameof(HasExternalDownloadLinks));
                OnPropertyChanged(nameof(ShowExternalDownloadLinkAction));
                (OpenExternalDownloadLinkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => EmailList.IsBusy || _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                (OpenEmailCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string SelectedAccStatusDisplay
    {
        get => _selectedAccStatusDisplay;
        private set => SetField(ref _selectedAccStatusDisplay, value);
    }

    public bool ShowSelectedAccStatus => !string.IsNullOrWhiteSpace(SelectedAccStatusDisplay);

    public bool HasExternalDownloadLinks =>
        EmailExternalDownloadLinkDetector.HasExternalDownloadLink(SelectedEmailBody);

    public bool ShowExternalDownloadLinkAction =>
        HasExternalDownloadLinks && _externalDownloadHandler?.IsAvailable == true;

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand OpenEmailCommand { get; }
    public ICommand LinkToProjectCommand { get; }
    public ICommand CreateTaskFromEmailCommand { get; }
    public ICommand MarkHandledCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand ReplyCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand OpenAttachmentCommand { get; }
    public ICommand CompleteTaskCommand { get; }
    public ICommand OpenExternalDownloadLinkCommand { get; }

    public void ApplyContext(WorkSurfaceContext? context)
    {
        if (context is null)
        {
            _workSurfaceContext = null;
            return;
        }

        if (!WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            StatusMessage = $"ההקשר אינו מתאים למסך דואר ({context.ComponentKey}).";
            return;
        }

        _workSurfaceContext = context;
        _ = ApplyTaskContextAsync(context);
    }

    public async Task RefreshAsync()
    {
        await ApplyProjectContextFromWorkbenchAsync().ConfigureAwait(true);
        await EmailList.InitializeAsync().ConfigureAwait(true);
        await EmailList.RefreshPageAsync().ConfigureAwait(true);
    }

    public Task SearchAsync() => EmailList.ApplyFiltersAsync();
    public Task ClearSearchAsync() => EmailList.ClearFiltersAndReloadAsync();
    public Task OpenSelectedEmailAsync() => _selectionHandler.OpenSelectedEmailAsync();

    public bool TryBlockCloseForBackgroundWork(object? owner) =>
        _accClosePrompt?.ConfirmCloseIfNeeded(owner) == false;

    public void Dispose()
    {
        EmailList.SelectedEmailChanged -= OnEmailListSelectionChanged;
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;
        _googleAuthService.AuthStateChanged -= OnAuthStateChanged;
        _externalDownloadHandler?.Dispose();
        ProjectSelector.Dispose();
    }

    private async Task ApplyTaskContextAsync(WorkSurfaceContext context)
    {
        if (context.ProjectId > 0)
        {
            var project = await _projectQuery
                .GetProjectAsync(context.ProjectId)
                .ConfigureAwait(true);

            if (project is not null)
            {
                await _currentProject.SetCurrentProjectAsync(project).ConfigureAwait(true);
            }
            else
            {
                StatusMessage = $"פרויקט #{context.ProjectId} לא נמצא.";
                return;
            }
        }

        if (!IsConnected)
        {
            StatusMessage = $"נפתח מתוך משימה #{context.TaskId}. התחבר ל-Google ורענן כדי לטעון מיילים.";
            return;
        }

        if (context.ProjectId > 0)
        {
            await ApplyProjectContextFromWorkbenchAsync().ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);

        if (context.PrimaryWorkTargetEntityId is not int inboxMessageId)
        {
            StatusMessage = context.TaskId is int taskId
                ? $"נפתח מתוך משימה #{taskId}. לא הוגדר יעד מייל — בחר מהרשימה."
                : "נפתח מתוך משימה. לא הוגדר יעד מייל — בחר מהרשימה.";
            return;
        }

        if (_emailInboxQuery is null)
        {
            StatusMessage = $"לא ניתן לבחור מייל #{inboxMessageId} מתוך משימה — שירות קריאת תיבת דואר לא זמין.";
            return;
        }

        var inboxMessage = await _emailInboxQuery
            .GetByIdAsync(inboxMessageId)
            .ConfigureAwait(true);

        if (inboxMessage is null)
        {
            StatusMessage = $"מייל #{inboxMessageId} לא נמצא במערכת.";
            return;
        }

        if (EmailList.TrySelectByInboxCorrelation(
                inboxMessage.MessageUniqueId,
                inboxMessage.InternetMessageId,
                inboxMessage.Subject,
                inboxMessage.FromAddress))
        {
            StatusMessage = context.TaskId is int openedTaskId
                ? $"נפתח מתוך משימה #{openedTaskId} — נבחר מייל \"{inboxMessage.Subject}\"."
                : $"נבחר מייל \"{inboxMessage.Subject}\".";
            return;
        }

        StatusMessage = $"מייל \"{inboxMessage.Subject}\" לא נמצא בעמוד Gmail הנוכחי.";
    }

    private async void OnEmailListSelectionChanged(object? sender, EmailListRow? value)
    {
        OnPropertyChanged(nameof(SelectedEmail));
        OnPropertyChanged(nameof(HasSelectedEmail));
        (OpenEmailCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (OpenExternalDownloadLinkCommand as RelayCommand)?.RaiseCanExecuteChanged();

        if (value is null)
        {
            _selectionHandler.ClearSelectedEmailDetails();
            return;
        }

        var loadVersion = ++_selectedEmailLoadVersion;

        if (!HasLoadedBodyForCurrentSelection(value.Id))
        {
            _selectionHandler.PrepareSelectedEmailDetailsLoading();
            SelectedAccStatusDisplay = string.Empty;
        }

        await RunSelectionPipelineAsync(value, loadVersion).ConfigureAwait(true);
    }

    private async Task RunSelectionPipelineAsync(EmailListRow row, int loadVersion)
    {
        await _selectionHandler
            .LoadSelectedEmailWithAccPipelineAsync(row, loadVersion, HasLoadedBodyForCurrentSelection)
            .ConfigureAwait(true);

        if (_externalDownloadHandler is not null
            && HasLoadedBodyForCurrentSelection(row.Id)
            && string.Equals(SelectedEmail?.Id, row.Id, StringComparison.Ordinal))
        {
            await _externalDownloadHandler
                .MergeExternalDownloadsIntoViewerAsync(row, loadVersion)
                .ConfigureAwait(true);
        }
    }

    private void OpenExternalDownloadLink()
    {
        if (SelectedEmail is null || _externalDownloadHandler is null)
        {
            return;
        }

        _externalDownloadHandler.OpenFirstDownloadLink(SelectedEmailBody, SelectedEmail);
    }

    private bool HasLoadedBodyForCurrentSelection(string messageId) =>
        string.Equals(SelectedEmail?.Id, messageId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedEmailBody)
        && !string.Equals(SelectedEmailBody, "טוען תוכן מייל...", StringComparison.Ordinal);

    private async Task MoveSelectedEmailToProjectAsync()
    {
        if (_moveToProjectCoordinator is null || SelectedEmail?.InboxMessageId is not int inboxMessageId)
        {
            StatusMessage = "MoveToProject אינו זמין.";
            return;
        }

        var projectId = _currentProject.CurrentProject?.ProjectId ?? SelectedEmail.ProjectId ?? 0;
        if (projectId <= 0)
        {
            StatusMessage = "בחר פרויקט לפני העברה.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _moveToProjectCoordinator.MoveAsync(
                new EmailMoveToProjectCommand(inboxMessageId, projectId)).ConfigureAwait(true);
            StatusMessage = result.Message;
            if (SelectedEmail is not null)
            {
                await EmailList.LoadAccStatusForRowAsync(SelectedEmail).ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AsyncRelayCommand DeferredProductionPilotAction(string message) => new(
        () =>
        {
            StatusMessage = message;
            return Task.CompletedTask;
        },
        () => false);

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        UpdateActiveProjectDisplay(e.Project);
        _selectionHandler.ClearSelectedEmailDetails();
        _ = ApplyProjectContextFromWorkbenchAsync();

        if (!IsConnected)
        {
            StatusMessage = e.Project is null
                ? "לא נבחר פרויקט — מציג כל המיילים לאחר רענון."
                : "הפרויקט הוחלף. התחבר ל-Google.";
        }
        else
        {
            StatusMessage = e.Project is null
                ? "לא נבחר פרויקט — מצב כל המיילים."
                : $"פרויקט נבחר: {e.Project.ProjectNumber} — {e.Project.ProjectName}";
        }

        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task ApplyProjectContextFromWorkbenchAsync()
    {
        await EmailList.ApplyProjectContextAsync(BuildEmailListProjectContext(_currentProject.CurrentProject))
            .ConfigureAwait(true);
    }

    private static EmailListProjectContext? BuildEmailListProjectContext(ProjectSummaryDto? project)
    {
        if (project is null)
        {
            return null;
        }

        var labelName = !string.IsNullOrWhiteSpace(project.ProjectLabelName)
            ? project.ProjectLabelName.Trim()
            : !string.IsNullOrWhiteSpace(project.ProjectNumber) && !string.IsNullOrWhiteSpace(project.ProjectName)
                ? $"{project.ProjectNumber} — {project.ProjectName}"
                : null;

        return new EmailListProjectContext(
            project.ProjectId,
            project.ProjectNumber,
            project.ProjectName,
            labelName,
            project.PlaceName);
    }

    private void OnAuthStateChanged(bool isAuthenticated)
    {
        UiThread.Run(() =>
        {
            RefreshAuthDisplay();
            if (!IsBusy)
            {
                StatusMessage = isAuthenticated
                    ? "החיבור ל-Google זמין. ניתן לרענן כדי לטעון מיילים."
                    : "החיבור ל-Google נותק.";
            }

            if (!isAuthenticated)
            {
                _selectionHandler.ClearSelectedEmailDetails();
            }

            (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SearchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        });
    }

    private void RefreshAuthDisplay()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(RuntimeSummary));
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void UpdateActiveProjectDisplay(ProjectSummaryDto? project)
    {
        ActiveProjectDisplay = project is null
            ? "לא נבחר פרויקט"
            : $"{project.ProjectNumber} — {project.ProjectName}";
    }
}
