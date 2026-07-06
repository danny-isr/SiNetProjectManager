using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.Application.WorkSurfaces;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// View model for <see cref="EmailWindowView"/> — the first real read-only New System slice of the
/// legacy <c>EmailManagementView</c> (email management window).
/// <para>
/// <b>Read-only Gmail slice.</b> This view model keeps the visual-clone layout but now loads real
/// Gmail-backed email summaries and best-effort message details for the selected project through
/// <see cref="IEmailGateway"/>, while auth/session stays behind
/// <see cref="IConnectorAuthService"/>. It remains intentionally narrow: no
/// send/reply/forward/mark-handled workflow, no project-linking side effects, no task creation,
/// no attachment download/open, and no workflow mutation.
/// </para>
/// <para>
/// Workflow-first direction is preserved structurally: the window can later be opened from a
/// Workflow/Task with a <see cref="WorkSurfaceContext"/> (see <see cref="ApplyContext"/>), after which
/// individual actions will be reconnected one at a time through clean Application services. This slice
/// does not implement task opening or task completion behavior.
/// </para>
/// <para>
/// Project selection is <b>not owned here</b>: the window hosts the shared
/// <see cref="ProjectSelectorViewModel"/> (bound in XAML via <see cref="ProjectSelector"/>) and only
/// <i>observes</i> the shared <see cref="ICurrentProjectContext"/> so <see cref="ActiveProjectDisplay"/>
/// reflects the Current Project (see <c>docs/PROJECTS.md</c> §5/§9).
/// </para>
/// <para>
/// The old <c>EmailManagementView</c> remains the visual reference / legacy source and is not modified.
/// This slice now shows full plain-text body plus attachment metadata, but keeps all write/send and
/// file-opening behavior deferred.
/// </para>
/// </summary>
public sealed class EmailWindowViewModel : ObservableObject, IDisposable
{
    private readonly IProjectQueryService _projectQuery;
    private readonly ICurrentProjectContext _currentProject;
    private readonly IEmailGateway _emailGateway;
    private readonly IConnectorAuthService _googleAuthService;
    private readonly IEmailInboxQueryService? _emailInboxQuery;
    private readonly IEmailThreadLinkQueryService? _threadLinkQuery;
    private WorkSurfaceContext? _workSurfaceContext;
    private EmailFolderRow? _selectedFolder;
    private string? _selectedStatus;
    private string _selectedEmailBody = string.Empty;
    private bool _isBusy;
    private int _selectedEmailLoadVersion;
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private string _statusMessage = "חבר Gmail ולחץ רענן כדי לטעון את כל המיילים.";

    public EmailWindowViewModel()
        : this(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            new InMemoryCurrentProjectContext(),
            new DesignEmailGateway(),
            new DesignConnectorAuthService())
    {
    }

    /// <summary>
    /// Convenience constructor used by project-context tests. Real runtime resolution should use the
    /// full constructor so Gmail auth/read seams come from DI.
    /// </summary>
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

    /// <summary>
    /// Primary constructor: hosts the shared <see cref="ProjectSelectorViewModel"/> over the supplied
    /// read ports and shared current-project context, and observes that context for display updates.
    /// Gmail auth/session remains behind <see cref="IConnectorAuthService"/> and read access behind
    /// <see cref="IEmailGateway"/>.
    /// </summary>
    public EmailWindowViewModel(
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService filterOptions,
        ICurrentProjectContext currentProject,
        IEmailGateway emailGateway,
        IConnectorAuthService googleAuthService,
        IEmailInboxQueryService? emailInboxQuery,
        IEmailThreadLinkQueryService? threadLinkQuery)
    {
        ArgumentNullException.ThrowIfNull(projectQuery);
        ArgumentNullException.ThrowIfNull(filterOptions);
        _projectQuery = projectQuery;
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));
        _emailInboxQuery = emailInboxQuery;
        _threadLinkQuery = threadLinkQuery;

        Folders = new ObservableCollection<EmailFolderRow>(EmailWindowDesignData.SampleFolders);
        StatusOptions = new ObservableCollection<string>(EmailWindowDesignData.SampleStatuses);
        Attachments = [];

        EmailList = new EmailListViewModel(_emailGateway, _threadLinkQuery, _googleAuthService);
        EmailList.SelectedEmailChanged += OnEmailListSelectionChanged;
        EmailList.StatusMessageChanged += (_, message) => StatusMessage = message;

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
        OpenEmailCommand = new AsyncRelayCommand(OpenSelectedEmailAsync, () => !IsBusy && SelectedEmail is not null);
        LinkToProjectCommand = DeferredProductionPilotAction("שיוך בפועל לפרויקט — מושהה (production pilot read-only).");
        CreateTaskFromEmailCommand = DeferredProductionPilotAction("יצירת משימה מהמייל — מושהה (דורש slice Workflow/Tasks).");
        MarkHandledCommand = DeferredProductionPilotAction("Move-to-project / mark-handled — מושהה (production pilot read-only).");
        ArchiveCommand = DeferredProductionPilotAction("ארכוב — מושהה (production pilot read-only).");
        ReplyCommand = DeferredProductionPilotAction("Reply/Send — מושהה (דורש G-Policy).");
        ForwardCommand = DeferredProductionPilotAction("Forward/Send — מושהה (דורש G-Policy).");
        OpenAttachmentCommand = DeferredProductionPilotAction("פתיחת attachment — מושהה (metadata-only pilot).");
        CompleteTaskCommand = DeferredProductionPilotAction("סיום משימה — מושהה (דורש ITaskCompletionCoordinator slice).");
    }

    /// <summary>Window title for the limited production pilot (read-only Gmail).</summary>
    public string Title => "ניהול דואר — קריאה בלבד";

    /// <summary>
    /// Production pilot envelope: deferred write/workflow/attachment-open actions stay in code but are
    /// hidden from the UI and cannot execute until an approved slice enables them.
    /// </summary>
    public bool ShowDeferredWriteActions => false;

    /// <summary>
    /// Production pilot: hide non-functional visual placeholders (pagination, calendar, date filters, help).
    /// Markup remains for a future slice — not deleted.
    /// </summary>
    public bool ShowDeferredVisualPlaceholders => false;

    /// <summary>Sidebar notice shown instead of deferred workflow/calendar placeholders.</summary>
    public string ProductionPilotNotice { get; } =
        "מצב פרודקשן ראשוני: צפייה במיילים ובפרטי קבצים מצורפים בלבד. פעולות תיוק ושליחה יחוברו בסלייס נפרד.";

    public int UnreadEmailCount => EmailList.UnreadEmailCount;

    public bool ShowUnreadCount => EmailList.ShowUnreadCount;

    public EmailListViewModel EmailList { get; }

    /// <summary>Backward-compatible alias for list rows.</summary>
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
            ? "Gmail מחובר — טעינת קריאה בלבד"
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
        private set => SetField(ref _selectedEmailBody, value);
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

    private void OnEmailListSelectionChanged(object? sender, EmailListRow? value)
    {
        OnPropertyChanged(nameof(SelectedEmail));
        OnPropertyChanged(nameof(HasSelectedEmail));
        (OpenEmailCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

        if (value is null)
        {
            ClearSelectedEmailDetails();
            return;
        }

        var loadVersion = ++_selectedEmailLoadVersion;
        PrepareSelectedEmailDetailsLoading();
        _ = LoadSelectedEmailDetailsAsync(value.Id, loadVersion);
    }

    public async Task RefreshAsync()
    {
        await ApplyProjectContextFromWorkbenchAsync().ConfigureAwait(true);
        await EmailList.InitializeAsync().ConfigureAwait(true);
        await EmailList.RefreshPageAsync().ConfigureAwait(true);
    }

    public Task SearchAsync() => EmailList.ApplyFiltersAsync();

    public Task ClearSearchAsync() => EmailList.ClearFiltersAndReloadAsync();

    public Task OpenSelectedEmailAsync()
    {
        if (SelectedEmail is null)
        {
            StatusMessage = "לא נבחר מייל.";
            return Task.CompletedTask;
        }

        var loadVersion = ++_selectedEmailLoadVersion;
        PrepareSelectedEmailDetailsLoading();
        return LoadSelectedEmailDetailsAsync(SelectedEmail.Id, loadVersion);
    }

    /// <summary>
    /// Deferred action kept for future slices. Disabled and hidden during the limited production pilot.
    /// </summary>
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
        ClearSelectedEmailDetails();
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
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(RuntimeSummary));
        if (!IsBusy)
        {
            StatusMessage = isAuthenticated
                ? "החיבור ל-Google זמין. ניתן לרענן כדי לטעון מיילים."
                : "החיבור ל-Google נותק.";
        }

        if (!isAuthenticated)
        {
            ClearSelectedEmailDetails();
        }

        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void PrepareSelectedEmailDetailsLoading()
    {
        SelectedEmailBody = "טוען תוכן מייל...";
        Attachments.Clear();
        if (SelectedEmail?.HasAttachments == true)
        {
            Attachments.Add(new EmailAttachmentRow(
                "טוען פרטי קבצים...",
                "Loading",
                "..."));
        }
    }

    private async Task LoadSelectedEmailDetailsAsync(string messageId, int loadVersion)
    {
        try
        {
            var details = await _emailGateway.GetDetailsAsync(messageId).ConfigureAwait(true);
            if (!ShouldApplySelectedEmailLoad(messageId, loadVersion))
            {
                return;
            }

            if (details is null)
            {
                ApplyMissingSelectedEmailDetails();
                StatusMessage = "לא ניתן היה לטעון את תוכן המייל המלא.";
                return;
            }

            ApplySelectedEmailDetails(details);
            StatusMessage = details.HasAttachments
                ? $"נטען תוכן המייל ו-{details.Attachments.Count} קבצים מצורפים."
                : "נטען תוכן המייל המלא.";
        }
        catch (Exception ex)
        {
            if (!ShouldApplySelectedEmailLoad(messageId, loadVersion))
            {
                return;
            }

            ApplyMissingSelectedEmailDetails();
            StatusMessage = $"טעינת תוכן המייל נכשלה: {ex.Message}";
        }
    }

    private bool ShouldApplySelectedEmailLoad(string messageId, int loadVersion) =>
        loadVersion == _selectedEmailLoadVersion
        && string.Equals(SelectedEmail?.Id, messageId, StringComparison.Ordinal);

    private void ApplySelectedEmailDetails(EmailMessageDetails details)
    {
        SelectedEmailBody = string.IsNullOrWhiteSpace(details.BodyText)
            ? "לא התקבל תוכן טקסטואלי זמין עבור המייל הזה."
            : details.BodyText;

        Attachments.Clear();
        foreach (var attachment in details.Attachments)
        {
            Attachments.Add(new EmailAttachmentRow(
                attachment.FileName,
                FormatAttachmentKind(attachment),
                FormatAttachmentSize(attachment.SizeBytes)));
        }
    }

    private void ApplyMissingSelectedEmailDetails()
    {
        SelectedEmailBody = SelectedEmail is null
            ? string.Empty
            : $"לא ניתן היה לטעון את תוכן המייל המלא.\n\nשולח: {SelectedEmail.Sender}\nנושא: {SelectedEmail.Subject}\nהתקבל: {SelectedEmail.ReceivedDisplay}";

        Attachments.Clear();
        if (SelectedEmail?.HasAttachments == true)
        {
            Attachments.Add(new EmailAttachmentRow(
                "פרטי הקבצים לא זמינים כרגע",
                "Unavailable",
                "..."));
        }
    }

    private void ClearSelectedEmailDetails()
    {
        _selectedEmailLoadVersion++;
        SelectedEmailBody = string.Empty;
        Attachments.Clear();
    }

    private void UpdateFolderSummaries(IReadOnlyList<EmailListRow> rows)
    {
        var total = rows.Count;
        var withAttachments = rows.Count(static row => row.HasAttachments);
        var unread = rows.Count(static row => row.IsUnread);
        var assigned = rows.Count(static row => row.IsAssigned);

        Folders.Clear();
        Folders.Add(new EmailFolderRow("מיילים לפרויקט", total));
        Folders.Add(new EmailFolderRow("עם קבצים מצורפים", withAttachments));
        Folders.Add(new EmailFolderRow("לא נקראו", unread));
        Folders.Add(new EmailFolderRow("משויכים לפרויקט", assigned));

        _selectedFolder = Folders.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedFolder));
    }

    private void UpdateActiveProjectDisplay(ProjectSummaryDto? project)
    {
        ActiveProjectDisplay = project is null
            ? "לא נבחר פרויקט"
            : $"{project.ProjectNumber} — {project.ProjectName}";
    }

    private static string FormatAttachmentKind(EmailMessageAttachmentDetails attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(attachment.ContentType) ? "FILE" : attachment.ContentType;
    }

    private static string FormatAttachmentSize(long? sizeBytes)
    {
        if (sizeBytes is null || sizeBytes <= 0)
        {
            return "Unknown";
        }

        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        if (sizeBytes >= megabyte)
        {
            return $"{sizeBytes.Value / megabyte:0.#} MB";
        }

        if (sizeBytes >= kilobyte)
        {
            return $"{sizeBytes.Value / kilobyte:0.#} KB";
        }

        return $"{sizeBytes.Value} B";
    }

    public void Dispose()
    {
        EmailList.SelectedEmailChanged -= OnEmailListSelectionChanged;
        _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;
        _googleAuthService.AuthStateChanged -= OnAuthStateChanged;
        ProjectSelector.Dispose();
    }

    private sealed class DesignEmailGateway : IEmailGateway
    {
        private static readonly IReadOnlyList<EmailSummary> SampleEmails = EmailWindowDesignData.SampleEmails
            .Select(static row => new EmailSummary(
                row.Id,
                $"thread-{row.Id}",
                EmailAddress.CreateOrFallback(row.Sender),
                row.Subject,
                row.ReceivedOn == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(row.ReceivedOn),
                row.HasAttachments))
            .ToList();

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleEmails);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleEmails);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleEmails.FirstOrDefault(email => string.Equals(email.MessageId, messageId, StringComparison.Ordinal)));

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
        {
            var summary = SampleEmails.FirstOrDefault(email => string.Equals(email.MessageId, messageId, StringComparison.Ordinal));
            if (summary is null)
            {
                return Task.FromResult<EmailMessageDetails?>(null);
            }

            return Task.FromResult<EmailMessageDetails?>(new EmailMessageDetails(
                summary.MessageId,
                summary.ThreadId,
                summary.From,
                summary.Subject,
                summary.ReceivedAt,
                EmailWindowDesignData.SampleBody,
                EmailWindowDesignData.SampleAttachments
                    .Select((attachment, index) => new EmailMessageAttachmentDetails(
                        $"att-{index + 1}",
                        attachment.FileName,
                        attachment.Kind,
                        null))
                    .ToList()));
        }

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            var items = EmailWindowDesignData.SampleEmails
                .Select(static row => new EmailSummary(
                    row.Id,
                    $"thread-{row.Id}",
                    EmailAddress.CreateOrFallback(row.Sender),
                    row.Subject,
                    row.ReceivedOn == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(row.ReceivedOn),
                    row.HasAttachments,
                    InternetMessageId: null,
                    To: null,
                    Snippet: row.Preview,
                    LabelNames: [row.GroupName],
                    PrimaryLabel: row.PrimaryLabel ?? row.GroupName))
                .ToList();

            return Task.FromResult(new EmailMailboxPage(items, query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GmailLabelInfo>>([
                new GmailLabelInfo("INBOX", "INBOX"),
            ]);
    }

    private sealed class DesignConnectorAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; }

        public string? ConnectedAccountEmail { get; private set; }

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(CancellationToken cancellationToken = default)
        {
            IsAuthenticated = true;
            ConnectedAccountEmail = "design@example.com";
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public void Logout()
        {
            IsAuthenticated = false;
            ConnectedAccountEmail = null;
            AuthStateChanged?.Invoke(false);
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
