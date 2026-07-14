using System.Windows.Input;
using System.Collections.ObjectModel;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using AccBrowserHost = SiNet.Application.Email.Acc.IEmailExternalDownloadBrowserHost;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Shell view model for <see cref="EmailWindowView"/> — project bar, list, status; detail work is in <see cref="EmailDetailViewModel"/>.
/// </summary>
public sealed partial class EmailWindowViewModel : ObservableObject, IDisposable
{
    private readonly IProjectQueryService _projectQuery;
    private readonly ICurrentProjectContext _currentProject;
    private readonly IConnectorAuthService _googleAuthService;
    private readonly IEmailInboxQueryService? _emailInboxQuery;
    private readonly IEmailAccClosePrompt? _accClosePrompt;
    private readonly IEmailAccBackgroundWorkTracker? _backgroundWorkTracker;
    private readonly EmailExternalDownloadHandler? _externalDownloadHandler;

    private WorkSurfaceContext? _workSurfaceContext;
    private EmailFolderRow? _selectedFolder;
    private string? _selectedStatus;
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private string _statusMessage = "מתחבר ומטען מיילים…";
    private int _backgroundWorkActiveCount;
    private int _lastBackgroundWorkCount;
    private int _autoRefreshGate;
    private int _applyTaskContextGate;

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
        : this(projectQuery, filterOptions, currentProject, new DesignEmailGateway(), new DesignConnectorAuthService())
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
            moveToProjectService: null,
            moveEligibility: null,
            workflowContextService: null,
            suggestedActionService: null,
            actionExecutionService: null,
            taskCompletionService: null,
            bodyRenderer: null,
            externalDownloadCoordinator: null,
            externalDownloadBrowserHost: null,
            accIngestQueue: null,
            ingestSessionEnsurer: null,
            backgroundWorkTracker: null,
            accClosePrompt: null,
            threadMappingSync: null)
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
        IEmailMoveToProjectService? moveToProjectService = null,
        IEmailMoveToProjectEligibilityService? moveEligibility = null,
        IEmailWorkflowContextService? workflowContextService = null,
        IEmailSuggestedActionService? suggestedActionService = null,
        IEmailSuggestedActionExecutionService? actionExecutionService = null,
        ITaskCompletionService? taskCompletionService = null,
        IEmailBodyRenderer? bodyRenderer = null,
        IEmailExternalDownloadCoordinator? externalDownloadCoordinator = null,
        AccBrowserHost? externalDownloadBrowserHost = null,
        IEmailAccIngestQueue? accIngestQueue = null,
        IGoogleIngestSessionEnsurer? ingestSessionEnsurer = null,
        IEmailAccBackgroundWorkTracker? backgroundWorkTracker = null,
        IEmailAccClosePrompt? accClosePrompt = null,
        IEmailThreadMappingSyncService? threadMappingSync = null,
        IEmailAttachmentTaggingService? attachmentTaggingService = null,
        IEmailAttachmentProjectFilePickerHost? attachmentProjectFilePicker = null,
        IEmailFilingProjectPickerHost? filingProjectPicker = null,
        IEmailAlternativeNamePromptHost? alternativeNamePrompt = null)
    {
        ArgumentNullException.ThrowIfNull(projectQuery);
        ArgumentNullException.ThrowIfNull(filterOptions);
        _projectQuery = projectQuery;
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));
        _emailInboxQuery = emailInboxQuery;
        _accClosePrompt = accClosePrompt;
        _backgroundWorkTracker = backgroundWorkTracker;

        Folders = new ObservableCollection<EmailFolderRow>(EmailWindowDesignData.SampleFolders);
        StatusOptions = new ObservableCollection<string>(EmailWindowDesignData.SampleStatuses);

        EmailList = new EmailListViewModel(
            emailGateway,
            threadLinkQuery,
            _googleAuthService,
            filingService,
            statusService,
            _currentProject,
            currentUser,
            accStatusService,
            accUploadCoordinator,
            moveToProjectCoordinator: null,
            accIngestQueue,
            ingestSessionEnsurer,
            threadMappingSync);

        EmailDetail = new EmailDetailViewModel(
            EmailList,
            emailGateway,
            _currentProject,
            filingService,
            currentUser,
            moveToProjectService,
            moveEligibility,
            workflowContextService,
            suggestedActionService,
            actionExecutionService,
            taskCompletionService,
            bodyRenderer,
            attachmentTaggingService,
            attachmentProjectFilePicker,
            filingProjectPicker,
            alternativeNamePrompt);

        _externalDownloadHandler = externalDownloadCoordinator is not null && externalDownloadBrowserHost is not null
            ? new EmailExternalDownloadHandler(
                externalDownloadCoordinator,
                externalDownloadBrowserHost,
                backgroundWorkTracker,
                EmailList,
                EmailDetail.SelectionCoordinator,
                message => StatusMessage = message,
                () => EmailList.SelectedEmail,
                () => EmailDetail.GetLoadVersion(),
                () => EmailDetail.BumpLoadVersion())
            : null;

        if (_externalDownloadHandler is not null)
        {
            EmailDetail.SetExternalDownloadHandler(_externalDownloadHandler);
        }

        EmailList.SelectedEmailChanged += OnEmailListSelectionChanged;
        EmailList.StatusMessageChanged += (_, message) => StatusMessage = message;
        EmailList.AccStatusPatched += OnAccStatusPatched;
        EmailList.AccountStatusChanged += (_, _) => RefreshAuthDisplay();
        EmailDetail.StatusMessageChanged += (_, message) => StatusMessage = message;

        _selectedFolder = Folders.FirstOrDefault();
        _selectedStatus = StatusOptions.FirstOrDefault();

        ProjectSelector = new ProjectSelectorViewModel(projectQuery, filterOptions, _currentProject);
        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
        _googleAuthService.AuthStateChanged += OnAuthStateChanged;
        UpdateActiveProjectDisplay(_currentProject.CurrentProject);

        if (_backgroundWorkTracker is not null)
        {
            _backgroundWorkActiveCount = _backgroundWorkTracker.ActiveCount;
            _lastBackgroundWorkCount = _backgroundWorkActiveCount;
            _backgroundWorkTracker.ActiveCountChanged += OnBackgroundWorkActiveCountChanged;
            if (_backgroundWorkActiveCount > 0)
            {
                StatusMessage = $"תהליכי רקע פעילים: {_backgroundWorkActiveCount}";
            }
        }

        _ = ProjectSelector.InitializeAsync();
        _ = AutoRefreshOnOpenAsync();

        RefreshCommand = EmailList.RefreshPageCommand;
        SearchCommand = EmailList.ApplyFiltersCommand;
        ClearSearchCommand = EmailList.ClearFiltersCommand;
        OpenEmailCommand = new AsyncRelayCommand(
            () => EmailDetail.OpenSelectedEmailAsync(),
            () => !EmailList.IsBusy && EmailList.SelectedEmail is not null);
    }

    public string Title => "ניהול דואר — Gmail + ACC Inbox";

    public string UnreadCountDisplay => EmailList.UnreadCountDisplay;
    public bool ShowUnreadCount => EmailList.ShowUnreadCount;
    public EmailListViewModel EmailList { get; }
    public EmailDetailViewModel EmailDetail { get; }
    public ProjectSelectorViewModel ProjectSelector { get; }

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public ObservableCollection<EmailFolderRow> Folders { get; }
    public ObservableCollection<string> StatusOptions { get; }
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

    public bool HasSelectedEmail => EmailDetail.HasSelectedEmail;

    public bool IsBusy => EmailList.IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public int BackgroundWorkActiveCount
    {
        get => _backgroundWorkActiveCount;
        private set
        {
            if (SetField(ref _backgroundWorkActiveCount, value))
            {
                OnPropertyChanged(nameof(HasBackgroundWork));
                OnPropertyChanged(nameof(BackgroundWorkDisplay));
            }
        }
    }

    public bool HasBackgroundWork => BackgroundWorkActiveCount > 0;

    public string BackgroundWorkDisplay => HasBackgroundWork
        ? $"תהליכי רקע פעילים: {BackgroundWorkActiveCount}"
        : string.Empty;

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand OpenEmailCommand { get; }

    public void ApplyContext(WorkSurfaceContext? context)
    {
        if (context is null)
        {
            _workSurfaceContext = null;
            EmailDetail.ApplyWorkSurfaceContext(null);
            return;
        }

        if (!WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            StatusMessage = $"ההקשר אינו מתאים למסך דואר ({context.ComponentKey}).";
            return;
        }

        _workSurfaceContext = context;
        EmailDetail.ApplyWorkSurfaceContext(context);
        _ = ApplyTaskContextAsync(context);
    }

    public async Task RefreshAsync()
    {
        await ApplyProjectContextFromWorkbenchAsync().ConfigureAwait(true);
        await EmailList.InitializeAsync().ConfigureAwait(true);
        if (IsConnected)
        {
            await EmailList.RefreshPageAsync().ConfigureAwait(true);
        }
    }

    public Task SearchAsync() => EmailList.ApplyFiltersAsync();
    public Task ClearSearchAsync() => EmailList.ClearFiltersAndReloadAsync();
    public Task OpenSelectedEmailAsync() => EmailDetail.OpenSelectedEmailAsync();

    public bool TryBlockCloseForBackgroundWork(object? owner) =>
        _accClosePrompt?.ConfirmCloseIfNeeded(owner) == false;

    public void Dispose()
    {
        EmailList.SelectedEmailChanged -= OnEmailListSelectionChanged;
        EmailList.AccStatusPatched -= OnAccStatusPatched;
        EmailDetail.StatusMessageChanged -= (_, _) => { };
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;
        _googleAuthService.AuthStateChanged -= OnAuthStateChanged;
        if (_backgroundWorkTracker is not null)
        {
            _backgroundWorkTracker.ActiveCountChanged -= OnBackgroundWorkActiveCountChanged;
        }

        _externalDownloadHandler?.Dispose();
        EmailDetail.Dispose();
        ProjectSelector.Dispose();
    }

    private async Task AutoRefreshOnOpenAsync()
    {
        if (Interlocked.CompareExchange(ref _autoRefreshGate, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!IsConnected)
            {
                await ApplyProjectContextFromWorkbenchAsync().ConfigureAwait(true);
                await EmailList.InitializeAsync().ConfigureAwait(true);
                StatusMessage = "חבר Gmail כדי לטעון מיילים.";
                return;
            }

            StatusMessage = "טוען מיילים…";
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Exchange(ref _autoRefreshGate, 0);
        }
    }

    private void OnBackgroundWorkActiveCountChanged(int count)
    {
        UiThread.Run(() =>
        {
            var previous = _lastBackgroundWorkCount;
            BackgroundWorkActiveCount = count;
            _lastBackgroundWorkCount = count;

            if (count > previous)
            {
                StatusMessage = $"תהליכי רקע פעילים: {count}";
            }
            else if (count > 0 && count < previous)
            {
                StatusMessage = $"תהליך הסתיים — נותרו {count}";
            }
            else if (count == 0 && previous > 0)
            {
                StatusMessage = "כל תהליכי הרקע הסתיימו";
            }
        });
    }

    private async void OnEmailListSelectionChanged(object? sender, EmailListRow? value)
    {
        OnPropertyChanged(nameof(SelectedEmail));
        OnPropertyChanged(nameof(HasSelectedEmail));
        (OpenEmailCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        // ApplySelectionAsync cancels any in-flight prior selection via its own CTS.
        await EmailDetail.ApplySelectionAsync(value).ConfigureAwait(true);
    }

    private void OnAccStatusPatched(object? sender, string display) =>
        EmailDetail.Viewer.AccStatusDisplay = display;

    private async Task ApplyTaskContextAsync(WorkSurfaceContext context)
    {
        // Single-flight: ignore a new task-context apply while one is already running so overlapping
        // opens cannot race project/refresh state. Fire-and-forget callers rely on this guard plus the
        // catch below so unobserved exceptions surface as a status message instead of crashing.
        if (Interlocked.CompareExchange(ref _applyTaskContextGate, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await ApplyTaskContextCoreAsync(context).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"טעינת הקשר המשימה נכשלה: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _applyTaskContextGate, 0);
        }
    }

    private async Task ApplyTaskContextCoreAsync(WorkSurfaceContext context)
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
            StatusMessage = $"נפתח מתוך משימה #{context.TaskId}. התחבר ל-Google כדי לטעון מיילים.";
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

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        UpdateActiveProjectDisplay(e.Project);
        EmailDetail.UpdateActiveProjectDisplay(e.Project);
        _ = SafeApplyProjectContextFromWorkbenchAsync();

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

    /// <summary>
    /// Guarded fire-and-forget wrapper for the <see cref="OnCurrentProjectChanged"/> event handler so a
    /// failed project-context apply surfaces as a status message instead of an unobserved exception.
    /// </summary>
    private async Task SafeApplyProjectContextFromWorkbenchAsync()
    {
        try
        {
            await ApplyProjectContextFromWorkbenchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"החלת הקשר הפרויקט נכשלה: {ex.Message}";
        }
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

            if (!isAuthenticated)
            {
                StatusMessage = "החיבור ל-Google נותק.";
                EmailDetail.ClearOnDisconnect();
            }
            else
            {
                StatusMessage = "החיבור ל-Google זמין — טוען מיילים…";
                _ = AutoRefreshOnOpenAsync();
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
