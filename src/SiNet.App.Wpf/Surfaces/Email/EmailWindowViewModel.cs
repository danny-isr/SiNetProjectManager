using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Projects;
using SiNet.Application.WorkSurfaces;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// View model for <see cref="EmailWindowView"/> — the first real read-only New System slice of the
/// legacy <c>EmailManagementView</c> (email management window).
/// <para>
/// <b>Read-only summary slice.</b> This view model keeps the visual-clone layout but now loads real
/// Gmail-backed email summaries for the selected project through <see cref="IEmailGateway"/> and
/// drives auth/session through <see cref="IConnectorAuthService"/>. It remains intentionally narrow:
/// no send/reply/forward/mark-handled workflow, no project-linking side effects, no task creation,
/// no file opens, and no workflow mutation.
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
/// Body and attachment-detail parity are intentionally deferred; the viewer therefore renders explicit
/// placeholder text once a summary is selected.
/// </para>
/// </summary>
public sealed class EmailWindowViewModel : ObservableObject, IDisposable
{
    private readonly ICurrentProjectContext _currentProject;
    private readonly IEmailGateway _emailGateway;
    private readonly IConnectorAuthService _googleAuthService;
    private IReadOnlyList<EmailSummary> _loadedEmails = [];

    private string _searchText = string.Empty;
    private EmailFolderRow? _selectedFolder;
    private string? _selectedStatus;
    private EmailListRow? _selectedEmail;
    private bool _isBusy;
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private string _statusMessage = "בחר פרויקט וחבר Gmail כדי לטעון מיילים.";

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
        IConnectorAuthService googleAuthService)
    {
        ArgumentNullException.ThrowIfNull(projectQuery);
        ArgumentNullException.ThrowIfNull(filterOptions);
        _currentProject = currentProject ?? throw new ArgumentNullException(nameof(currentProject));
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));

        Folders = new ObservableCollection<EmailFolderRow>(EmailWindowDesignData.SampleFolders);
        StatusOptions = new ObservableCollection<string>(EmailWindowDesignData.SampleStatuses);
        Emails = [];
        Attachments = [];

        _selectedFolder = Folders.FirstOrDefault();
        _selectedStatus = StatusOptions.FirstOrDefault();

        ProjectSelector = new ProjectSelectorViewModel(projectQuery, filterOptions, _currentProject);
        _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
        _googleAuthService.AuthStateChanged += OnAuthStateChanged;
        UpdateActiveProjectDisplay(_currentProject.CurrentProject);
        _ = ProjectSelector.InitializeAsync();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanLoadEmails);
        SearchCommand = new AsyncRelayCommand(SearchAsync, CanLoadEmails);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        OpenEmailCommand = new AsyncRelayCommand(OpenSelectedEmailAsync, () => !IsBusy && SelectedEmail is not null);
        LinkToProjectCommand = DeferredAction("שיוך בפועל לפרויקט עדיין לא אושר בחלון החדש.");
        CreateTaskFromEmailCommand = DeferredAction("יצירת משימה מהמייל תגיע רק אחרי חיבור ה-Workflow/Tasks.");
        MarkHandledCommand = DeferredAction("Move-to-project / mark-handled עדיין מחוץ לסלייס הקריאה בלבד.");
        ArchiveCommand = DeferredAction("ארכוב וטיפול בסטטוסים עדיין לא חלק מהסלייס הזה.");
        ReplyCommand = DeferredAction("Reply/Send נשארים מחוץ לסלייס עד לאישור policy מפורש.");
        ForwardCommand = DeferredAction("Forward/Send נשארים מחוץ לסלייס עד לאישור policy מפורש.");
        OpenAttachmentCommand = DeferredAction("קריאת attachment details תגיע רק אם parity מלא יאושר.");
        CompleteTaskCommand = DeferredAction("סיום משימה עדיין לא מחובר בחלון הדוא\"ל החדש.");
    }

    /// <summary>Window title for the first real read-only email slice.</summary>
    public string Title => "ניהול דואר — קריאה בלבד";

    public ProjectSelectorViewModel ProjectSelector { get; }

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public ObservableCollection<EmailFolderRow> Folders { get; }

    public ObservableCollection<string> StatusOptions { get; }

    public ObservableCollection<EmailListRow> Emails { get; }

    public ObservableCollection<EmailAttachmentRow> Attachments { get; }

    public bool IsConnected => _googleAuthService.IsAuthenticated;

    public string RuntimeSummary =>
        IsConnected
            ? "Gmail מחובר — טעינת summaries בלבד"
            : "Gmail לא מחובר";

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
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
        get => _selectedEmail;
        set
        {
            if (SetField(ref _selectedEmail, value))
            {
                ReplaceAttachmentsForSelectedEmail();
                OnPropertyChanged(nameof(HasSelectedEmail));
                OnPropertyChanged(nameof(SelectedEmailBody));
                (OpenEmailCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedEmail => _selectedEmail is not null;

    public string SelectedEmailBody =>
        _selectedEmail is null
            ? string.Empty
            : $"נבחר מייל אמיתי מ-Gmail.\n\nשולח: {_selectedEmail.Sender}\nנושא: {_selectedEmail.Subject}\nהתקבל: {_selectedEmail.ReceivedDisplay}\n\nתוכן מלא וקריאת attachments עדיין מחוץ לסלייס הקריאה בלבד.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (SearchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ConnectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
    public ICommand ConnectCommand { get; }
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
            return;
        }

        StatusMessage = "נפתח מתוך משימה. ניתן לטעון summaries לפרויקט הנבחר; workflow actions עדיין לא חוברו.";
    }

    public async Task ConnectAsync()
    {
        IsBusy = true;
        StatusMessage = "מתחבר ל-Google… ייתכן שייפתח דפדפן.";

        try
        {
            var connected = await _googleAuthService.LoginAsync().ConfigureAwait(true);
            StatusMessage = connected
                ? "החיבור ל-Google הושלם. ניתן לרענן כדי לטעון מיילים."
                : "ההתחברות ל-Google לא הושלמה. בדוק את תשתית ה-auth ונסה שוב.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"התחברות ל-Google נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(RuntimeSummary));
        }
    }

    public async Task RefreshAsync()
    {
        var projectLabelName = ResolveCurrentProjectLabelName();
        if (projectLabelName is null)
        {
            StatusMessage = "יש לבחור פרויקט עם Project label תקין לפני טעינת מיילים.";
            ReplaceEmailRows([]);
            return;
        }

        IsBusy = true;
        StatusMessage = $"טוען מיילים עבור {projectLabelName}…";
        try
        {
            _loadedEmails = await _emailGateway
                .GetProjectEmailsByProjectLabelAsync(projectLabelName)
                .ConfigureAwait(true);

            ApplySearchFilter();

            StatusMessage = Emails.Count == 0
                ? "לא נמצאו מיילים לפרויקט הנבחר (או ש-Gmail אינו מחובר)."
                : $"נטענו {Emails.Count} מיילים עבור {projectLabelName}.";
        }
        catch (Exception ex)
        {
            _loadedEmails = [];
            ReplaceEmailRows([]);
            StatusMessage = $"טעינת המיילים נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task SearchAsync() => RefreshAsync();

    public Task OpenSelectedEmailAsync()
    {
        if (SelectedEmail is null)
        {
            StatusMessage = "לא נבחר מייל.";
            return Task.CompletedTask;
        }

        StatusMessage = "Summary email נטען. body מלא ו-attachments עדיין deferred.";
        return Task.CompletedTask;
    }

    private bool CanLoadEmails() =>
        !IsBusy && _currentProject.CurrentProject is not null && !string.IsNullOrWhiteSpace(ResolveCurrentProjectLabelName());

    private AsyncRelayCommand DeferredAction(string message) => new(() =>
    {
        StatusMessage = message;
        return Task.CompletedTask;
    });

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        UpdateActiveProjectDisplay(e.Project);
        ReplaceEmailRows([]);
        _loadedEmails = [];
        StatusMessage = e.Project is null
            ? "לא נבחר פרויקט."
            : IsConnected
                ? "הפרויקט הוחלף. לחץ רענן כדי לטעון את המיילים שלו."
                : "הפרויקט הוחלף. התחבר ל-Google ואז לחץ רענן.";
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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

        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private string? ResolveCurrentProjectLabelName()
    {
        var project = _currentProject.CurrentProject;
        if (project is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(project.ProjectLabelName))
        {
            return project.ProjectLabelName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(project.ProjectNumber) && !string.IsNullOrWhiteSpace(project.ProjectName))
        {
            return $"{project.ProjectNumber} — {project.ProjectName}";
        }

        return null;
    }

    private void ApplySearchFilter()
    {
        IEnumerable<EmailSummary> filtered = _loadedEmails;
        var query = SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(email =>
                email.Subject.Contains(query, StringComparison.OrdinalIgnoreCase)
                || email.From.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceEmailRows(filtered
            .OrderByDescending(static email => email.ReceivedAt)
            .Select(ToEmailListRow)
            .ToList());
    }

    private void ReplaceEmailRows(IReadOnlyList<EmailListRow> rows)
    {
        Emails.Clear();
        foreach (var row in rows)
        {
            Emails.Add(row);
        }

        SelectedEmail = Emails.FirstOrDefault();
        UpdateFolderSummaries(rows);
    }

    private void ReplaceAttachmentsForSelectedEmail()
    {
        Attachments.Clear();
        if (SelectedEmail is null)
        {
            return;
        }

        if (SelectedEmail.HasAttachments)
        {
            Attachments.Add(new EmailAttachmentRow(
                "Attachment details pending parity slice",
                "Deferred",
                "Summary only"));
        }
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

    private static EmailListRow ToEmailListRow(EmailSummary summary) => new(
        Id: summary.MessageId,
        Sender: summary.From.Value,
        Subject: string.IsNullOrWhiteSpace(summary.Subject) ? "(ללא נושא)" : summary.Subject,
        Preview: summary.HasAttachments ? "Gmail summary loaded. Attachment/body details are deferred." : "Gmail summary loaded.",
        ReceivedOn: summary.ReceivedAt == DateTimeOffset.MinValue ? DateTime.MinValue : summary.ReceivedAt.LocalDateTime,
        GroupName: "מיילים לפרויקט",
        IsUnread: false,
        IsAssigned: true,
        AssignedProjectName: null,
        AttachmentCount: summary.HasAttachments ? 1 : 0);

    public void Dispose()
    {
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
    }

    private sealed class DesignConnectorAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; }

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(CancellationToken cancellationToken = default)
        {
            IsAuthenticated = true;
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public void Logout()
        {
            IsAuthenticated = false;
            AuthStateChanged?.Invoke(false);
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);
    }
}
