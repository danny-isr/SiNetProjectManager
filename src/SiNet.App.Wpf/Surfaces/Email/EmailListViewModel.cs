using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Standalone email list component: paged Gmail read, account bar, filters, Outlook-style cards,
/// and read-only project-link display.
/// </summary>
public sealed class EmailListViewModel : ObservableObject
{
    public const int PageSize = EmailMailboxQuery.DefaultPageSize;
    public const int ProjectEmailChunkSize = 10;
    internal const int MaxPagesPerLabelLoad = 20;

    private readonly IEmailGateway _emailGateway;
    private readonly IEmailThreadLinkQueryService? _threadLinkQuery;
    private readonly IConnectorAuthService _authService;
    private readonly IEmailFilingService? _filingService;
    private readonly IEmailStatusService? _statusService;
    private readonly IEmailAccStatusService? _accStatusService;
    private readonly IEmailAccUploadCoordinator? _accUploadCoordinator;
    private readonly IEmailMoveToProjectCoordinator? _moveToProjectCoordinator;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly ICurrentUserContext? _currentUser;
    private readonly Stack<string?> _pageTokenStack = new();

    private EmailListRow? _selectedEmail;
    private bool _isBusy;
    private string? _nextPageToken;
    private string? _lastUsedPageToken;
    private int _currentPageNumber = 1;
    private int _displayedCount;
    private bool _hasNextPage;
    private bool _groupByLabel = true;
    private bool _attachmentsOnly;
    private EmailListLoadState _loadState = EmailListLoadState.Idle;
    private string? _loadWarning;
    private string? _loadError;
    private string _statusMessage = "חבר Gmail ולחץ רענן כדי לטעון מיילים.";
    private string _searchText = string.Empty;
    private string _addressFilter = string.Empty;
    private string _subjectFilter = string.Empty;
    private string? _selectedLabel;
    private EmailMailboxScope _selectedMailboxScope = EmailMailboxScope.Inbox;
    private int _mailboxUnreadTotal;
    private bool _mailboxUnreadIsExact = true;
    private string? _lastLoadedGmailQuery;
    private string? _lastUnreadQuerySignature;
    private EmailProjectLinkFilter _projectLinkFilter = EmailProjectLinkFilter.All;
    private EmailListProjectContext? _projectContext;
    private EmailLabelGroupViewModel? _projectGroup;
    private bool _hasLabelGroups;
    private ICollectionView? _emailsView;
    private readonly HashSet<string> _busyRowIds = new(StringComparer.Ordinal);
    private string? _lastActionDiagnostics;

    public EmailListViewModel()
        : this(new DesignEmailListGateway(), threadLinkQuery: null, new DesignAuthService())
    {
    }

    public EmailListViewModel(
        IEmailGateway emailGateway,
        IEmailThreadLinkQueryService? threadLinkQuery,
        IConnectorAuthService authService,
        IEmailFilingService? filingService = null,
        IEmailStatusService? statusService = null,
        ICurrentProjectContext? currentProject = null,
        ICurrentUserContext? currentUser = null,
        IEmailAccStatusService? accStatusService = null,
        IEmailAccUploadCoordinator? accUploadCoordinator = null,
        IEmailMoveToProjectCoordinator? moveToProjectCoordinator = null)
    {
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _threadLinkQuery = threadLinkQuery;
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _filingService = filingService;
        _statusService = statusService;
        _accStatusService = accStatusService;
        _accUploadCoordinator = accUploadCoordinator;
        _moveToProjectCoordinator = moveToProjectCoordinator;
        _currentProject = currentProject;
        _currentUser = currentUser;

        Emails = [];
        FlatDisplayEmails = [];
        AvailableLabels = [];
        DisplayGroups = [];
        ProjectLinkFilterOptions =
        [
            new EmailProjectLinkFilterOption(EmailProjectLinkFilter.All, "הכול"),
            new EmailProjectLinkFilterOption(EmailProjectLinkFilter.Linked, "משויכים"),
            new EmailProjectLinkFilterOption(EmailProjectLinkFilter.Unlinked, "לא משויכים"),
        ];
        MailboxScopeOptions =
        [
            new EmailMailboxScopeOption(EmailMailboxScope.Inbox, "אינבוקס"),
            new EmailMailboxScopeOption(EmailMailboxScope.AllMail, "כל הדואר"),
            new EmailMailboxScopeOption(EmailMailboxScope.Unread, "לא נקראו"),
        ];

        _emailsView = CollectionViewSource.GetDefaultView(Emails);
        _emailsView.SortDescriptions.Add(new SortDescription(nameof(EmailListRow.ReceivedOn), ListSortDirection.Descending));

        LoadFirstPageCommand = new AsyncRelayCommand(() => ReloadForContextAsync(), CanLoadEmails);
        LoadNextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(resetStack: false, useNextToken: true), () => CanLoadEmails() && HasNextPage);
        LoadPreviousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CanLoadEmails() && HasPreviousPage);
        RefreshPageCommand = new AsyncRelayCommand(() => ReloadForContextAsync(), CanLoadEmails);
        ApplyFiltersCommand = new AsyncRelayCommand(() => ReloadForContextAsync(), CanLoadEmails);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync, () => !IsBusy);
        ToggleGroupByLabelCommand = new RelayCommand(_ => ToggleGroupByLabel());
        ToggleAttachmentsOnlyCommand = new AsyncRelayCommand(ToggleAttachmentsOnlyAsync, CanLoadEmails);
        FileEmailToProjectCommand = new AsyncRelayCommand<EmailListRow>(FileEmailToProjectAsync, CanFileEmailToProject, allowConcurrentParameters: true);
        UnfileEmailCommand = new AsyncRelayCommand<EmailListRow>(UnfileEmailAsync, CanUnfileEmail, allowConcurrentParameters: true);
        MarkAsPendingCommand = new AsyncRelayCommand<EmailListRow>(row => SetEmailStatusAsync(row, EmailTriageStatus.Pending), CanSetEmailStatus, allowConcurrentParameters: true);
        MarkAsPersonalCommand = new AsyncRelayCommand<EmailListRow>(row => SetEmailStatusAsync(row, EmailTriageStatus.Personal), CanSetEmailStatus, allowConcurrentParameters: true);
        MarkAsIrrelevantCommand = new AsyncRelayCommand<EmailListRow>(row => SetEmailStatusAsync(row, EmailTriageStatus.Irrelevant), CanSetEmailStatus, allowConcurrentParameters: true);
        UploadToAccInboxCommand = new AsyncRelayCommand<EmailListRow>(UploadToAccInboxAsync, CanUploadToAccInbox, allowConcurrentParameters: true);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectGmailAsync, () => IsConnected && !IsBusy);

        _authService.AuthStateChanged += OnAuthStateChanged;

        if (_currentProject is not null)
        {
            _currentProject.CurrentProjectChanged += (_, _) =>
            {
                RefreshRowBackgrounds();
                RaiseCommandStates();
            };
        }
    }

    public EmailListDisplayMode DisplayMode =>
        HasActiveProject ? EmailListDisplayMode.ProjectEmails : EmailListDisplayMode.AllEmails;

    public bool HasActiveProject => _projectContext is not null;

    public bool IsProjectMode => HasActiveProject;

    public bool IsAllEmailsMode => true;

    public bool ShowAllEmailsPaging => IsConnected;

    public bool ShowProjectGroupChrome => false;

    public EmailListProjectContext? SelectedProjectContext => _projectContext;

    public string DisplayModeSummary => HasActiveProject
        ? "מצב: כל המיילים + פרויקט"
        : "מצב: כל המיילים";

    public string ProjectGroupHeader => _projectContext?.GroupHeaderDisplay ?? string.Empty;

    public EmailLabelGroupViewModel? ActiveProjectGroup => _projectGroup;

    public ObservableCollection<EmailListRow> Emails { get; }

    public ObservableCollection<EmailListRow> FlatDisplayEmails { get; }

    public ObservableCollection<GmailLabelInfo> AvailableLabels { get; }

    public ObservableCollection<EmailLabelGroupViewModel> DisplayGroups { get; }

    public bool HasLabelGroups => _hasLabelGroups;

    public bool ShowLabelGroups => GroupByLabel && HasLabelGroups;

    public bool ShowFlatEmailList => !ShowLabelGroups;

    public bool ShowProjectGroupAboveFlat => !GroupByLabel && _projectGroup is not null;

    public ObservableCollection<EmailProjectLinkFilterOption> ProjectLinkFilterOptions { get; }

    public ObservableCollection<EmailMailboxScopeOption> MailboxScopeOptions { get; }

    public ICollectionView? EmailsView => _emailsView;

    public EmailListRow? SelectedEmail
    {
        get => _selectedEmail;
        set
        {
            if (SetField(ref _selectedEmail, value))
            {
                SelectedEmailChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<EmailListRow?>? SelectedEmailChanged;

    public event EventHandler<string>? StatusMessageChanged;

    public event EventHandler? AccountStatusChanged;

    public int UnreadInCurrentPage => Emails.Count(static row => row.IsUnread);

    public int MailboxUnreadTotal
    {
        get => _mailboxUnreadTotal;
        private set => SetField(ref _mailboxUnreadTotal, value);
    }

    public bool MailboxUnreadIsExact
    {
        get => _mailboxUnreadIsExact;
        private set => SetField(ref _mailboxUnreadIsExact, value);
    }

    public string UnreadCountDisplay
    {
        get
        {
            if (MailboxUnreadIsExact)
            {
                var scopeLabel = SelectedMailboxScope switch
                {
                    EmailMailboxScope.AllMail => "בכל הדואר",
                    EmailMailboxScope.Unread => "לא נקראו",
                    EmailMailboxScope.Label => $"ב־{SelectedLabel ?? "label"}",
                    _ => "באינבוקס",
                };
                return $"לא נקראו {scopeLabel}: {MailboxUnreadTotal} · בעמוד: {UnreadInCurrentPage}";
            }

            return $"לא נקראו בעמוד זה: {UnreadInCurrentPage}";
        }
    }

    public bool ShowUnreadCount => MailboxUnreadIsExact
        ? MailboxUnreadTotal > 0 || UnreadInCurrentPage > 0
        : UnreadInCurrentPage > 0;

    public string MailboxDiagnostics =>
        $"Scope: {SelectedMailboxScope} | Query: {_lastLoadedGmailQuery ?? "—"} | Loaded: {DisplayedCount} | Unread total: {(MailboxUnreadIsExact ? MailboxUnreadTotal.ToString() : "n/a")} | Unread page: {UnreadInCurrentPage} | Next: {(HasNextPage ? "yes" : "no")}";

    public bool ShowMailboxDiagnostics => IsAllEmailsMode && IsConnected;

    public bool IsConnected => _authService.IsAuthenticated;

    public string? ConnectedAccountEmail => _authService.ConnectedAccountEmail;

    public string AccountStatusDisplay => IsConnected
        ? string.IsNullOrWhiteSpace(ConnectedAccountEmail)
            ? "מחובר (אימייל לא זמין)"
            : $"מחובר כ: {ConnectedAccountEmail}"
        : "לא מחובר ל-Gmail";

    public EmailListLoadState LoadState
    {
        get => _loadState;
        private set => SetField(ref _loadState, value);
    }

    public string? LoadWarning
    {
        get => _loadWarning;
        private set
        {
            if (SetField(ref _loadWarning, value))
            {
                OnPropertyChanged(nameof(ShowLoadWarning));
            }
        }
    }

    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetField(ref _loadError, value))
            {
                OnPropertyChanged(nameof(ShowLoadError));
            }
        }
    }

    public bool ShowLoadWarning => !string.IsNullOrWhiteSpace(LoadWarning);

    public bool ShowLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public bool ShowSparsePageWarning =>
        SelectedProjectLinkFilter != EmailProjectLinkFilter.All && DisplayedCount < PageSize && DisplayedCount > 0;

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
            {
                StatusMessageChanged?.Invoke(this, value);
            }
        }
    }

    internal string? LastActionDiagnostics => _lastActionDiagnostics;

    internal bool IsRowActionBusy(string rowId) => _busyRowIds.Contains(rowId);

    internal EmailListRow? FindRowForTests(string rowId) => FindRowById(rowId);

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public string AddressFilter
    {
        get => _addressFilter;
        set => SetField(ref _addressFilter, value);
    }

    public string SubjectFilter
    {
        get => _subjectFilter;
        set => SetField(ref _subjectFilter, value);
    }

    public string? SelectedLabel
    {
        get => _selectedLabel;
        set
        {
            if (!SetField(ref _selectedLabel, value))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                if (_selectedMailboxScope != EmailMailboxScope.Label)
                {
                    _selectedMailboxScope = EmailMailboxScope.Label;
                    OnPropertyChanged(nameof(SelectedMailboxScope));
                }
            }
            else if (_selectedMailboxScope == EmailMailboxScope.Label)
            {
                _selectedMailboxScope = EmailMailboxScope.Inbox;
                OnPropertyChanged(nameof(SelectedMailboxScope));
            }
        }
    }

    public EmailMailboxScope SelectedMailboxScope
    {
        get => _selectedMailboxScope;
        set
        {
            if (!SetField(ref _selectedMailboxScope, value))
            {
                return;
            }

            if (value != EmailMailboxScope.Label && !string.IsNullOrWhiteSpace(_selectedLabel))
            {
                _selectedLabel = null;
                OnPropertyChanged(nameof(SelectedLabel));
            }
        }
    }

    public EmailProjectLinkFilter SelectedProjectLinkFilter
    {
        get => _projectLinkFilter;
        set
        {
            if (SetField(ref _projectLinkFilter, value))
            {
                OnPropertyChanged(nameof(ShowSparsePageWarning));
            }
        }
    }

    public bool ShowConnectButton => !IsConnected;

    public bool CanRefreshEmails => IsConnected && !IsBusy;

    public bool GroupByLabel
    {
        get => _groupByLabel;
        private set
        {
            if (SetField(ref _groupByLabel, value))
            {
                NotifyDisplayGroupProperties();
            }
        }
    }

    public bool AttachmentsOnly
    {
        get => _attachmentsOnly;
        private set => SetField(ref _attachmentsOnly, value);
    }

    public int DisplayedCount
    {
        get => _displayedCount;
        private set
        {
            if (SetField(ref _displayedCount, value))
            {
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(ShowSparsePageWarning));
            }
        }
    }

    public int CurrentPageNumber
    {
        get => _currentPageNumber;
        private set
        {
            if (SetField(ref _currentPageNumber, value))
            {
                OnPropertyChanged(nameof(PageInfo));
            }
        }
    }

    public bool HasNextPage
    {
        get => _hasNextPage;
        private set
        {
            if (SetField(ref _hasNextPage, value))
            {
                (LoadNextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPreviousPage => _pageTokenStack.Count > 0;

    public string PageInfo
    {
        get
        {
            if (DisplayedCount == 0)
            {
                return $"{DisplayModeSummary} · עמוד {CurrentPageNumber}";
            }

            var start = (CurrentPageNumber - 1) * PageSize + 1;
            var end = start + DisplayedCount - 1;
            var projectSuffix = HasActiveProject && _projectGroup is not null
                ? $" · פרויקט: {_projectGroup.LoadedCount}"
                : string.Empty;
            return $"{DisplayModeSummary} · מציג {start}–{end} · עמוד {CurrentPageNumber}{projectSuffix}";
        }
    }

    public ICommand LoadFirstPageCommand { get; }
    public ICommand LoadNextPageCommand { get; }
    public ICommand LoadPreviousPageCommand { get; }
    public ICommand RefreshPageCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleGroupByLabelCommand { get; }
    public ICommand ToggleAttachmentsOnlyCommand { get; }
    public ICommand FileEmailToProjectCommand { get; }
    public ICommand UnfileEmailCommand { get; }
    public ICommand MarkAsPendingCommand { get; }
    public ICommand MarkAsPersonalCommand { get; }
    public ICommand MarkAsIrrelevantCommand { get; }
    public ICommand UploadToAccInboxCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    /// <summary>Called by the Email Workbench host when the shared project context changes.</summary>
    public async Task ApplyProjectContextAsync(EmailListProjectContext? context)
    {
        if (Equals(_projectContext, context))
        {
            return;
        }

        _projectContext = context;
        ClearProjectGroup();
        NotifyDisplayModeProperties();

        if (IsConnected)
        {
            await ReloadForContextAsync().ConfigureAwait(true);
        }
    }

    public Task RefreshPageAsync() => ReloadForContextAsync();

    public Task ApplyFiltersAsync() => ReloadForContextAsync();

    public Task ClearFiltersAndReloadAsync() => ClearFiltersAsync();

    public Task LoadNextPageAsync() => LoadPageAsync(resetStack: false, useNextToken: true);

    public Task LoadPreviousPagePublicAsync() => LoadPreviousPageAsync();

    private async Task ReloadForContextAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);
    }

    private async Task LoadMailboxAndProjectAsync(bool resetStack)
    {
        await LoadPageAsync(resetStack, skipDisplayRebuild: true).ConfigureAwait(true);
        await EnsureProjectGroupAsync(resetPaging: resetStack).ConfigureAwait(true);
        RebuildDisplayGroups();
    }

    public async Task InitializeAsync()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectedAccountEmail));
        OnPropertyChanged(nameof(AccountStatusDisplay));

        if (!IsConnected)
        {
            return;
        }

        await RefreshAccountProfileAsync().ConfigureAwait(true);
        await LoadLabelsAsync().ConfigureAwait(true);
    }

    public bool TrySelectByInboxCorrelation(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress)
    {
        EmailListRow? match = null;

        if (!string.IsNullOrWhiteSpace(messageUniqueId) || !string.IsNullOrWhiteSpace(internetMessageId))
        {
            match = Emails.FirstOrDefault(row =>
                EmailMessageIdMatcher.Matches(row.InternetMessageId, internetMessageId)
                || EmailMessageIdMatcher.Matches(row.InternetMessageId, messageUniqueId));
        }

        if (match is null && !string.IsNullOrWhiteSpace(subject))
        {
            match = Emails.FirstOrDefault(row =>
                string.Equals(row.Subject, subject, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(fromAddress)
                    || row.Sender.Contains(fromAddress, StringComparison.OrdinalIgnoreCase)));
        }

        if (match is null)
        {
            return false;
        }

        SelectedEmail = match;
        return true;
    }

    private bool CanFileEmailToProject(EmailListRow? row) =>
        CanExecuteWriteAction(row)
        && _filingService is not null
        && row is not null
        && !IsFiledToSameProject(row)
        && _currentProject?.CurrentProject is not null
        && (_currentUser?.UserId ?? 0) > 0;

    private bool CanUnfileEmail(EmailListRow? row) =>
        CanExecuteWriteAction(row)
        && _filingService is not null
        && row is { IsFiledToProject: true }
        && (_currentUser?.UserId ?? 0) > 0;

    private bool CanSetEmailStatus(EmailListRow? row) =>
        CanExecuteWriteAction(row)
        && _statusService is not null
        && row is not null
        && (_currentUser?.UserId ?? 0) > 0;

    private bool CanExecuteWriteAction(EmailListRow? row) =>
        row is not null && IsConnected && !_busyRowIds.Contains(row.Id);

    /// <summary>Hebrew tooltip when a context menu action is disabled; null when enabled.</summary>
    public string? GetContextMenuDisabledReason(EmailListRow? row, EmailContextMenuAction action)
    {
        if (IsActionEnabled(row, action))
        {
            return null;
        }

        return action switch
        {
            EmailContextMenuAction.FileToProject => DescribeFileToProjectDisabledReason(row),
            EmailContextMenuAction.Unfile => DescribeUnfileDisabledReason(row),
            EmailContextMenuAction.MarkPending or EmailContextMenuAction.MarkPersonal or EmailContextMenuAction.MarkIrrelevant
                => DescribeSetStatusDisabledReason(row),
            EmailContextMenuAction.UploadToAccInbox => DescribeUploadToAccDisabledReason(row),
            _ => "הפעולה אינה זמינה.",
        };
    }

    private bool IsActionEnabled(EmailListRow? row, EmailContextMenuAction action) =>
        action switch
        {
            EmailContextMenuAction.FileToProject => CanFileEmailToProject(row),
            EmailContextMenuAction.Unfile => CanUnfileEmail(row),
            EmailContextMenuAction.MarkPending or EmailContextMenuAction.MarkPersonal or EmailContextMenuAction.MarkIrrelevant
                => CanSetEmailStatus(row),
            EmailContextMenuAction.UploadToAccInbox => CanUploadToAccInbox(row),
            _ => false,
        };

    private bool CanUploadToAccInbox(EmailListRow? row) =>
        row is not null
        && IsConnected
        && _accUploadCoordinator is not null
        && row.HasAttachments
        && !_busyRowIds.Contains(row.Id)
        && row.AccProcessingStatus is not (
            EmailAccProcessingStatus.UploadedToAcc
            or EmailAccProcessingStatus.MovedToProject
            or EmailAccProcessingStatus.LockedByOtherUser
            or EmailAccProcessingStatus.UploadInProgress)
        && !row.IsAccUploadBusy;

    private string? DescribeUploadToAccDisabledReason(EmailListRow? row)
    {
        if (row is null)
        {
            return "לא נבחר מייל.";
        }

        if (!IsConnected)
        {
            return "התחבר ל-Gmail.";
        }

        if (_accUploadCoordinator is null)
        {
            return "העלאה ל-ACC אינה זמינה.";
        }

        if (!row.HasAttachments)
        {
            return "אין קבצים מצורפים.";
        }

        if (row.AccProcessingStatus == EmailAccProcessingStatus.LockedByOtherUser)
        {
            return row.AccStatusDisplay ?? "המייל בטיפול על ידי משתמש אחר.";
        }

        if (row.AccProcessingStatus is EmailAccProcessingStatus.UploadedToAcc or EmailAccProcessingStatus.MovedToProject)
        {
            return "כבר הועלה ל-ACC.";
        }

        if (row.IsAccUploadBusy || row.AccProcessingStatus == EmailAccProcessingStatus.UploadInProgress)
        {
            return "העלאה מתבצעת.";
        }

        return "הפעולה אינה זמינה.";
    }

    private string? DescribeWriteActionBlockedReason(EmailListRow? row)
    {
        if (row is null)
        {
            return "לא נבחר מייל.";
        }

        if (!IsConnected)
        {
            return "חבר Gmail לפני ביצוע פעולה.";
        }

        if (row is not null && _busyRowIds.Contains(row.Id))
        {
            return "פעולה כבר רצה על מייל זה.";
        }

        if (IsBusy)
        {
            return "המערכת עסוקה — נסה שוב בעוד רגע.";
        }

        return null;
    }

    private string? DescribeFileToProjectDisabledReason(EmailListRow? row)
    {
        var blocked = DescribeWriteActionBlockedReason(row);
        if (blocked is not null)
        {
            return blocked;
        }

        if (_filingService is null)
        {
            return "שירות שיוך מיילים לא זמין.";
        }

        if ((_currentUser?.UserId ?? 0) <= 0)
        {
            return "לא ניתן לבצע פעולה — אין משתמש מחובר במערכת.";
        }

        if (_currentProject?.CurrentProject is null)
        {
            return "בחר פרויקט לפני שיוך מייל.";
        }

        if (row is not null && IsFiledToSameProject(row))
        {
            return "המייל כבר משויך לפרויקט הנבחר.";
        }

        return "לא ניתן לשייך מייל לפרויקט.";
    }

    private string? DescribeUnfileDisabledReason(EmailListRow? row)
    {
        var blocked = DescribeWriteActionBlockedReason(row);
        if (blocked is not null)
        {
            return blocked;
        }

        if (_filingService is null)
        {
            return "שירות שיוך מיילים לא זמין.";
        }

        if ((_currentUser?.UserId ?? 0) <= 0)
        {
            return "לא ניתן לבצע פעולה — אין משתמש מחובר במערכת.";
        }

        if (row is not { IsFiledToProject: true })
        {
            return "המייל לא משויך לפרויקט.";
        }

        return "לא ניתן לבטל שיוך.";
    }

    private string? DescribeSetStatusDisabledReason(EmailListRow? row)
    {
        var blocked = DescribeWriteActionBlockedReason(row);
        if (blocked is not null)
        {
            return blocked;
        }

        if (_statusService is null)
        {
            return "שירות עדכון סטטוס מייל לא זמין.";
        }

        if ((_currentUser?.UserId ?? 0) <= 0)
        {
            return "לא ניתן לבצע פעולה — אין משתמש מחובר במערכת.";
        }

        return "לא ניתן לעדכן סטטוס.";
    }

    private async Task FileEmailToProjectAsync(EmailListRow? row)
    {
        if (row is null)
        {
            LoadWarning = DescribeWriteActionBlockedReason(null) ?? "לא נבחר מייל.";
            return;
        }

        if (_filingService is null)
        {
            LoadWarning = DescribeFileToProjectDisabledReason(row);
            return;
        }

        if (_currentProject?.CurrentProject is not { } project)
        {
            LoadWarning = DescribeFileToProjectDisabledReason(row);
            return;
        }

        var actingUserId = _currentUser?.UserId;
        if (actingUserId is null or <= 0)
        {
            LoadWarning = DescribeFileToProjectDisabledReason(row);
            return;
        }

        if (IsFiledToSameProject(row))
        {
            LoadWarning = DescribeFileToProjectDisabledReason(row);
            return;
        }

        await ExecuteRowActionAsync(
            row,
            startingStatusMessage: "משייך מייל לפרויקט...",
            rowStatusText: "משייך לפרויקט...",
            successStatusMessage: "המייל שויך לפרויקט בהצלחה.",
            serviceCall: async () =>
            {
                var result = await _filingService!.FileToProjectAsync(new FileEmailToProjectCommand(
                    project.ProjectId,
                    actingUserId.Value,
                    row.Id,
                    row.InboxMessageId,
                    row.ThreadId,
                    row.InternetMessageId)).ConfigureAwait(true);
                return (result.Succeeded, result.ErrorMessage ?? "שיוך לפרויקט נכשל.");
            },
            onSuccessLocalUpdate: currentRow => RefreshRowAfterFileAsync(currentRow, project),
            failureMessagePrefix: "שיוך לפרויקט נכשל").ConfigureAwait(true);
    }

    private async Task UnfileEmailAsync(EmailListRow? row)
    {
        if (row is null)
        {
            LoadWarning = DescribeWriteActionBlockedReason(null) ?? "לא נבחר מייל.";
            return;
        }

        if (_filingService is null)
        {
            LoadWarning = DescribeUnfileDisabledReason(row);
            return;
        }

        var actingUserId = _currentUser?.UserId;
        if (actingUserId is null or <= 0)
        {
            LoadWarning = DescribeUnfileDisabledReason(row);
            return;
        }

        if (!row.IsFiledToProject)
        {
            LoadWarning = DescribeUnfileDisabledReason(row);
            return;
        }

        await ExecuteRowActionAsync(
            row,
            startingStatusMessage: "מבטל שיוך מייל...",
            rowStatusText: "מבטל שיוך...",
            successStatusMessage: "שיוך המייל לפרויקט בוטל.",
            serviceCall: async () =>
            {
                var result = await _filingService!.UnfileFromProjectAsync(new UnfileEmailCommand(
                    actingUserId.Value,
                    row.Id,
                    row.InboxMessageId,
                    row.ThreadId,
                    row.InternetMessageId,
                    row.FiledProjectLabelPath)).ConfigureAwait(true);
                return (result.Succeeded, result.ErrorMessage ?? "ביטול שיוך נכשל.");
            },
            onSuccessLocalUpdate: RefreshRowAfterUnfileAsync,
            failureMessagePrefix: "ביטול שיוך נכשל").ConfigureAwait(true);
    }

    private async Task SetEmailStatusAsync(EmailListRow? row, EmailTriageStatus status)
    {
        if (row is null)
        {
            LoadWarning = DescribeWriteActionBlockedReason(null) ?? "לא נבחר מייל.";
            return;
        }

        if (_statusService is null)
        {
            LoadWarning = DescribeSetStatusDisabledReason(row);
            return;
        }

        var actingUserId = _currentUser?.UserId;
        if (actingUserId is null or <= 0)
        {
            LoadWarning = DescribeSetStatusDisabledReason(row);
            return;
        }

        var (startingMessage, rowStatusText, successMessage) = status switch
        {
            EmailTriageStatus.Pending => ("מסמן מייל כממתין לטיפול...", "מסמן כממתין...", "המייל סומן כממתין לטיפול."),
            EmailTriageStatus.Personal => ("מסמן מייל כאישי...", "מסמן כאישי...", "המייל סומן כאישי."),
            EmailTriageStatus.Irrelevant => ("מסמן מייל כלא רלוונטי...", "מסמן כלא רלוונטי...", "המייל סומן כלא רלוונטי."),
            _ => ("מעדכן סטטוס מייל...", "מעדכן סטטוס...", "סטטוס המייל עודכן."),
        };

        var removeOnSuccess = status is EmailTriageStatus.Personal or EmailTriageStatus.Irrelevant;

        await ExecuteRowActionAsync(
            row,
            startingStatusMessage: startingMessage,
            rowStatusText: rowStatusText,
            successStatusMessage: successMessage,
            serviceCall: async () =>
            {
                var result = await _statusService!.SetStatusAsync(new SetEmailStatusCommand(
                    row.Id,
                    row.ThreadId,
                    status,
                    actingUserId.Value,
                    row.InboxMessageId,
                    row.ThreadUniqueId)).ConfigureAwait(true);
                return (result.Succeeded, result.ErrorMessage ?? "עדכון סטטוס נכשל.");
            },
            onSuccessLocalUpdate: currentRow => RefreshRowAfterStatusAsync(currentRow, status),
            failureMessagePrefix: "עדכון סטטוס נכשל",
            removeRowOnSuccess: removeOnSuccess).ConfigureAwait(true);
    }

    private async Task ExecuteRowActionAsync(
        EmailListRow row,
        string startingStatusMessage,
        string rowStatusText,
        string successStatusMessage,
        Func<Task<(bool Succeeded, string? ErrorMessage)>> serviceCall,
        Func<EmailListRow, Task<EmailListRow?>> onSuccessLocalUpdate,
        string failureMessagePrefix,
        bool removeRowOnSuccess = false)
    {
        if (_busyRowIds.Contains(row.Id))
        {
            return;
        }

        _busyRowIds.Add(row.Id);
        var totalSw = Stopwatch.StartNew();
        long serviceMs = 0;
        long localUpdateMs = 0;
        long refreshMs = 0;

        try
        {
            StatusMessage = startingStatusMessage;
            SetRowActionState(FindRowById(row.Id) ?? row, busy: true, statusText: rowStatusText);
            RaiseCommandStates();

            var serviceSw = Stopwatch.StartNew();
            var (succeeded, errorMessage) = await serviceCall().ConfigureAwait(true);
            serviceMs = serviceSw.ElapsedMilliseconds;

            if (!succeeded)
            {
                LoadWarning = errorMessage ?? $"{failureMessagePrefix}.";
                var current = FindRowById(row.Id) ?? row;
                SetRowActionState(current, busy: false, statusText: null, errorText: LoadWarning);
                return;
            }

            LoadWarning = null;

            var localSw = Stopwatch.StartNew();
            var updatedRow = await onSuccessLocalUpdate(row).ConfigureAwait(true);
            localUpdateMs = localSw.ElapsedMilliseconds;

            if (updatedRow is null)
            {
                StatusMessage = "מרענן רשימה...";
                var refreshSw = Stopwatch.StartNew();
                await RefreshPageAsync().ConfigureAwait(true);
                refreshMs = refreshSw.ElapsedMilliseconds;
                StatusMessage = successStatusMessage;
                return;
            }

            var clearedRow = updatedRow with
            {
                IsActionBusy = false,
                ActionStatusText = null,
                ActionErrorText = null,
            };

            if (removeRowOnSuccess)
            {
                RemoveRowFromDisplay(clearedRow);
            }
            else
            {
                ApplyLocalEmailMutation(clearedRow);
            }

            StatusMessage = successStatusMessage;
        }
        catch (Exception ex)
        {
            LoadWarning = $"{failureMessagePrefix}: {ex.Message}";
            var current = FindRowById(row.Id) ?? row;
            SetRowActionState(current, busy: false, statusText: null, errorText: LoadWarning);
        }
        finally
        {
            _busyRowIds.Remove(row.Id);
            _lastActionDiagnostics =
                $"total={totalSw.ElapsedMilliseconds}ms service={serviceMs}ms localUpdate={localUpdateMs}ms refresh={refreshMs}ms";
            Debug.WriteLine($"[PERF] EmailAction row={row.Id} {_lastActionDiagnostics}");
            RaiseCommandStates();
        }
    }

    private void SetRowActionState(
        EmailListRow row,
        bool busy,
        string? statusText,
        string? errorText = null)
    {
        var updated = row with
        {
            IsActionBusy = busy,
            ActionStatusText = busy ? statusText : null,
            ActionErrorText = busy ? errorText : null,
        };
        ReplaceRowInDisplay(updated);
    }

    private EmailListRow? FindRowById(string rowId)
    {
        foreach (var email in Emails)
        {
            if (string.Equals(email.Id, rowId, StringComparison.Ordinal))
            {
                return email;
            }
        }

        foreach (var flatRow in FlatDisplayEmails)
        {
            if (string.Equals(flatRow.Id, rowId, StringComparison.Ordinal))
            {
                return flatRow;
            }
        }

        if (_projectGroup is not null)
        {
            foreach (var email in _projectGroup.Emails)
            {
                if (string.Equals(email.Id, rowId, StringComparison.Ordinal))
                {
                    return email;
                }
            }
        }

        return null;
    }

    private void ReplaceRowInDisplay(EmailListRow updated)
    {
        RunOnUiThread(() => ReplaceRowInDisplayCore(updated));
    }

    private void ReplaceRowInDisplayCore(EmailListRow updated)
    {
        var id = updated.Id;
        var replaced = false;

        for (var index = 0; index < Emails.Count; index++)
        {
            if (string.Equals(Emails[index].Id, id, StringComparison.Ordinal))
            {
                Emails[index] = updated;
                replaced = true;
            }
        }

        for (var index = 0; index < FlatDisplayEmails.Count; index++)
        {
            if (string.Equals(FlatDisplayEmails[index].Id, id, StringComparison.Ordinal))
            {
                FlatDisplayEmails[index] = updated;
                replaced = true;
            }
        }

        foreach (var group in DisplayGroups)
        {
            for (var index = 0; index < group.Emails.Count; index++)
            {
                if (string.Equals(group.Emails[index].Id, id, StringComparison.Ordinal))
                {
                    group.Emails[index] = updated;
                    replaced = true;
                }
            }
        }

        if (_projectGroup is not null)
        {
            for (var index = 0; index < _projectGroup.Emails.Count; index++)
            {
                if (string.Equals(_projectGroup.Emails[index].Id, id, StringComparison.Ordinal))
                {
                    _projectGroup.Emails[index] = updated;
                    replaced = true;
                }
            }
        }

        if (string.Equals(SelectedEmail?.Id, id, StringComparison.Ordinal))
        {
            SelectedEmail = updated;
        }

        if (replaced)
        {
            RaiseCommandStates();
        }
    }

    private void ApplyLocalEmailMutation(EmailListRow updated)
    {
        RunOnUiThread(() => ApplyLocalEmailMutationCore(updated));
    }

    private void ApplyLocalEmailMutationCore(EmailListRow updated)
    {
        var shouldRemove = SelectedProjectLinkFilter switch
        {
            EmailProjectLinkFilter.Linked => !updated.IsLinked,
            EmailProjectLinkFilter.Unlinked => updated.IsLinked,
            _ => false,
        };

        if (shouldRemove)
        {
            RemoveRowFromDisplayCore(updated);
            RebindSelectionAfterRemoval(updated.Id);
            RaiseCommandStates();
            return;
        }

        UpdateEmailInSourceCollection(updated);
        SyncProjectGroupMembership(updated);
        RebuildDisplayGroups();
        RebindSelectionAfterMutation(updated.Id);
        RaiseCommandStates();
    }

    private void UpdateEmailInSourceCollection(EmailListRow updated)
    {
        for (var index = 0; index < Emails.Count; index++)
        {
            if (string.Equals(Emails[index].Id, updated.Id, StringComparison.Ordinal))
            {
                Emails[index] = updated;
                return;
            }
        }
    }

    private void SyncProjectGroupMembership(EmailListRow updated)
    {
        if (_projectGroup is null)
        {
            return;
        }

        if (updated.IsFiledToSameProject)
        {
            _projectGroup.TryAddEmail(updated);
            return;
        }

        _projectGroup.RemoveEmailById(updated.Id);
    }

    private void RebindSelectionAfterMutation(string rowId)
    {
        if (!string.Equals(SelectedEmail?.Id, rowId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedEmail = FindVisibleRowById(rowId);
    }

    private void RebindSelectionAfterRemoval(string rowId)
    {
        if (!string.Equals(SelectedEmail?.Id, rowId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedEmail = FlatDisplayEmails.FirstOrDefault()
            ?? _projectGroup?.Emails.FirstOrDefault()
            ?? Emails.FirstOrDefault();
    }

    private EmailListRow? FindVisibleRowById(string rowId)
    {
        var fromFlat = FlatDisplayEmails.FirstOrDefault(row =>
            string.Equals(row.Id, rowId, StringComparison.Ordinal));
        if (fromFlat is not null)
        {
            return fromFlat;
        }

        foreach (var group in DisplayGroups)
        {
            var fromGroup = group.Emails.FirstOrDefault(row =>
                string.Equals(row.Id, rowId, StringComparison.Ordinal));
            if (fromGroup is not null)
            {
                return fromGroup;
            }
        }

        return Emails.FirstOrDefault(row => string.Equals(row.Id, rowId, StringComparison.Ordinal));
    }

    private void RebindSelectedEmail(EmailListRow updated)
    {
        if (!string.Equals(SelectedEmail?.Id, updated.Id, StringComparison.Ordinal))
        {
            return;
        }

        SelectedEmail = FindVisibleRowById(updated.Id) ?? updated;
    }

    private async Task<EmailListRow?> RefreshRowAfterFileAsync(EmailListRow row, ProjectSummaryDto project)
    {
        var refreshed = await RefreshRowFromGmailAsync(row).ConfigureAwait(true);
        if (refreshed is null || !refreshed.IsFiledToProject)
        {
            return BuildOptimisticFiledRow(row, project);
        }

        return refreshed with
        {
            ProjectId = project.ProjectId,
            ProjectNumber = project.ProjectNumber,
            ProjectName = project.ProjectName,
            ProjectDisplay = $"{project.ProjectNumber} — {project.ProjectName}",
            AssignedProjectName = $"{project.ProjectNumber} — {project.ProjectName}",
            IsFiledToProject = true,
            IsFiledToSameProject = true,
            IsAssigned = true,
            ProjectLinkState = EmailProjectLinkState.Linked,
            FiledProjectLabelPath = refreshed.FiledProjectLabelPath
                ?? $"{EmailGmailLabelNames.RootLabel}/{project.PlaceName ?? string.Empty}/{project.ProjectNumber} — {project.ProjectName}",
        };
    }

    private async Task<EmailListRow?> RefreshRowAfterUnfileAsync(EmailListRow row)
    {
        var refreshed = await RefreshRowFromGmailAsync(row).ConfigureAwait(true);
        return refreshed ?? BuildOptimisticUnfiledRow(row);
    }

    private async Task<EmailListRow?> RefreshRowAfterStatusAsync(EmailListRow row, EmailTriageStatus status)
    {
        if (status is EmailTriageStatus.Personal or EmailTriageStatus.Irrelevant)
        {
            return row;
        }

        var refreshed = await RefreshRowFromGmailAsync(row).ConfigureAwait(true);
        return refreshed ?? BuildOptimisticStatusRow(row, status);
    }

    private async Task<EmailListRow?> RefreshRowFromGmailAsync(EmailListRow row)
    {
        var summary = await _emailGateway.GetByIdAsync(row.Id).ConfigureAwait(true);
        if (summary is null)
        {
            return null;
        }

        var (rows, _) = await MapSummariesAsync([summary]).ConfigureAwait(true);
        return rows.Count > 0 ? rows[0] : null;
    }

    private static EmailListRow BuildOptimisticFiledRow(EmailListRow row, ProjectSummaryDto project)
    {
        var location = project.PlaceName ?? string.Empty;
        var projectLabelPath = $"{EmailGmailLabelNames.RootLabel}/{location}/{project.ProjectNumber} — {project.ProjectName}";
        var labelChipNames = row.LabelChipNames?.ToList() ?? [];
        if (!labelChipNames.Contains(projectLabelPath, StringComparer.OrdinalIgnoreCase))
        {
            labelChipNames.Add(projectLabelPath);
        }

        var filed = row with
        {
            IsFiledToProject = true,
            IsFiledToSameProject = true,
            IsAssigned = true,
            ProjectLinkState = EmailProjectLinkState.Linked,
            ProjectId = project.ProjectId,
            ProjectNumber = project.ProjectNumber,
            ProjectName = project.ProjectName,
            ProjectDisplay = $"{project.ProjectNumber} — {project.ProjectName}",
            AssignedProjectName = $"{project.ProjectNumber} — {project.ProjectName}",
            FiledProjectLabelPath = projectLabelPath,
            RowBackgroundColor = "#C8E6C9",
        };

        return ApplyLabelDisplayFields(filed, labelChipNames);
    }

    private static EmailListRow BuildOptimisticUnfiledRow(EmailListRow row)
    {
        var labelChipNames = row.LabelChipNames?
            .Where(static label => !EmailGmailLabelNames.IsProjectLabel(label))
            .ToList() ?? [];

        var unfiled = row with
        {
            IsFiledToProject = false,
            IsFiledToSameProject = false,
            IsAssigned = false,
            ProjectLinkState = EmailProjectLinkState.Unlinked,
            ProjectId = null,
            ProjectNumber = null,
            ProjectName = null,
            ProjectDisplay = "לא משויך",
            AssignedProjectName = null,
            FiledProjectLabelPath = null,
            RowBackgroundColor = ResolveOptimisticRowBackground(labelChipNames, isFiledToProject: false, linkedProjectId: null),
        };

        return ApplyLabelDisplayFields(unfiled, labelChipNames);
    }

    private static EmailListRow BuildOptimisticStatusRow(EmailListRow row, EmailTriageStatus status)
    {
        var statusLabel = status switch
        {
            EmailTriageStatus.Pending => EmailGmailLabelNames.Pending,
            EmailTriageStatus.Personal => EmailGmailLabelNames.Personal,
            EmailTriageStatus.Irrelevant => EmailGmailLabelNames.Irrelevant,
            _ => null,
        };

        var labelChipNames = row.LabelChipNames?.ToList() ?? [];
        if (statusLabel is not null && !labelChipNames.Contains(statusLabel, StringComparer.OrdinalIgnoreCase))
        {
            labelChipNames.Add(statusLabel);
        }

        var background = status switch
        {
            EmailTriageStatus.Pending => "#F3E5F5",
            EmailTriageStatus.Personal or EmailTriageStatus.Irrelevant => "#E3F2FD",
            _ => row.RowBackgroundColor,
        };

        var updated = row with { RowBackgroundColor = background };
        return ApplyLabelDisplayFields(updated, labelChipNames);
    }

    private static EmailListRow ApplyLabelDisplayFields(
        EmailListRow row,
        IReadOnlyList<string> labelChipNames)
    {
        var displayLabelNames = FilterDisplayLabels(labelChipNames);
        var labelChips = FilterDisplayLabelChips(null, labelChipNames);
        var labelsDisplay = displayLabelNames.Count > 0
            ? string.Join(", ", displayLabelNames)
            : string.Empty;
        var primaryLabel = displayLabelNames.FirstOrDefault() ?? "ללא label";

        return row with
        {
            LabelChipNames = labelChipNames,
            LabelChips = labelChips,
            LabelsDisplay = labelsDisplay,
            PrimaryLabel = primaryLabel,
            GroupName = primaryLabel,
        };
    }

    private static string? ResolveOptimisticRowBackground(
        IReadOnlyList<string> labelChipNames,
        bool isFiledToProject,
        int? linkedProjectId)
    {
        if (labelChipNames.Any(static label =>
                label.Equals(EmailGmailLabelNames.Pending, StringComparison.OrdinalIgnoreCase)))
        {
            return "#F3E5F5";
        }

        if (labelChipNames.Any(static label =>
                label.Equals(EmailGmailLabelNames.Personal, StringComparison.OrdinalIgnoreCase)
                || label.Equals(EmailGmailLabelNames.Irrelevant, StringComparison.OrdinalIgnoreCase)))
        {
            return "#E3F2FD";
        }

        if (!isFiledToProject)
        {
            return null;
        }

        return linkedProjectId.HasValue ? "#C8E6C9" : "#E0E0E0";
    }

    private void RemoveRowFromDisplay(EmailListRow row)
    {
        RunOnUiThread(() => RemoveRowFromDisplayCore(row));
    }

    private void RemoveRowFromDisplayCore(EmailListRow row)
    {
        RemoveRowById(Emails, row.Id);
        foreach (var group in DisplayGroups)
        {
            RemoveRowById(group.Emails, row.Id);
        }

        if (_projectGroup is not null)
        {
            _projectGroup.RemoveEmailById(row.Id);
        }

        RemoveRowById(FlatDisplayEmails, row.Id);

        if (string.Equals(SelectedEmail?.Id, row.Id, StringComparison.Ordinal))
        {
            SelectedEmail = null;
        }

        RebuildDisplayGroups();
    }

    private static void RemoveRowById(ObservableCollection<EmailListRow> rows, string rowId)
    {
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (string.Equals(rows[index].Id, rowId, StringComparison.Ordinal))
            {
                rows.RemoveAt(index);
            }
        }
    }

    private void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private bool CanLoadEmails() => !IsBusy && IsConnected;

    private async Task ConnectAsync()
    {
        IsBusy = true;
        StatusMessage = "מתחבר ל-Gmail...";
        try
        {
            var connected = await _authService.LoginAsync(
                new ConnectorLoginOptions(SkipSilentRestore: true, PromptAccountSelection: true))
                .ConfigureAwait(true);
            if (!connected && await _authService.TryRestoreSessionAsync().ConfigureAwait(true))
            {
                connected = true;
            }

            if (!connected)
            {
                StatusMessage = "התחברות ל-Gmail בוטלה.";
                return;
            }

            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
            ClearEmailState();
            await LoadLabelsAsync().ConfigureAwait(true);
            StatusMessage = "טוען מיילים...";
            await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);

            var email = ConnectedAccountEmail ?? "Gmail";
            StatusMessage = $"מחובר כ־{email}. נטענו {DisplayedCount} מיילים.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"התחברות ל-Gmail נכשלה: {ex.Message}";
            LoadError = StatusMessage;
        }
        finally
        {
            IsBusy = false;
            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        }
    }

    private async Task DisconnectGmailAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        if (MessageBox.Show(
                "להתנתק מחשבון Gmail הנוכחי?",
                "התנתקות מ-Gmail",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "מתנתק...";
        try
        {
            _authService.Logout();
            StatusMessage = "מנותק מ-Gmail.";
        }
        finally
        {
            IsBusy = false;
            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        }

        await Task.CompletedTask;
    }

    /// <summary>Test seam: disconnect without confirmation dialog.</summary>
    internal Task ConnectGmailForTestsAsync() => ConnectAsync();

    internal async Task DisconnectGmailForTestsAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "מתנתק...";
        try
        {
            _authService.Logout();
            StatusMessage = "מנותק מ-Gmail.";
        }
        finally
        {
            IsBusy = false;
            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        }

        await Task.CompletedTask;
    }

    /// <summary>Targeted ACC status load for the selected row — read-only, no upload.</summary>
    public async Task<EmailAccInboxStatus?> LoadAccStatusForRowAsync(EmailListRow row, CancellationToken cancellationToken = default)
    {
        if (_accStatusService is null)
        {
            return null;
        }

        PatchAccRow(row with
        {
            IsAccStatusLoading = true,
            AccStatusDisplay = "בודק ACC…",
        });

        try
        {
            var status = await _accStatusService
                .GetStatusByInternetMessageIdAsync(
                    row.InternetMessageId,
                    row.Id,
                    ResolveActingUserLogin(),
                    cancellationToken)
                .ConfigureAwait(true);

            if (status is not null)
            {
                PatchAccRow(ApplyAccStatus(row, status));
            }
            else
            {
                PatchAccRow(row with { IsAccStatusLoading = false, AccStatusDisplay = "סטטוס ACC לא זמין" });
            }

            return status;
        }
        catch (Exception ex)
        {
            PatchAccRow(row with
            {
                IsAccStatusLoading = false,
                AccProcessingStatus = EmailAccProcessingStatus.Unknown,
                AccStatusDisplay = $"שגיאת ACC: {ex.Message}",
            });
            return null;
        }
    }

    private async Task UploadToAccInboxAsync(EmailListRow? row)
    {
        if (row is null || _accUploadCoordinator is null || !CanUploadToAccInbox(row))
        {
            return;
        }

        await ExecuteAccRowActionAsync(
            row,
            "מעלה ל-ACC Inbox…",
            async () =>
            {
                var command = new EmailAccUploadCommand(
                    row.Id,
                    row.ThreadId ?? string.Empty,
                    row.InternetMessageId,
                    ResolveActingUserLogin());

                var upload = await _accUploadCoordinator
                    .UploadToAccInboxAsync(command)
                    .ConfigureAwait(true);

                if (upload.Outcome == EmailAccUploadOutcome.InProgress
                    && !string.IsNullOrWhiteSpace(upload.MessageUniqueId))
                {
                    StatusMessageChanged?.Invoke(this, "המייל בטיפול על ידי משתמש אחר — ממתין לסיום…");
                    await _accUploadCoordinator
                        .WaitForCompletionAsync(
                            upload.MessageUniqueId!,
                            ResolveActingUserLogin(),
                            TimeSpan.FromSeconds(5),
                            maxAttempts: 24,
                            shouldContinue: () => string.Equals(SelectedEmail?.Id, row.Id, StringComparison.Ordinal))
                        .ConfigureAwait(true);
                }

                var status = await _accStatusService!
                    .GetStatusByInternetMessageIdAsync(
                        row.InternetMessageId,
                        row.Id,
                        ResolveActingUserLogin())
                    .ConfigureAwait(true);

                var patched = ApplyAccStatus(row, status);
                patched = patched with
                {
                    IsAccUploadBusy = false,
                    AccUploadStatusText = upload.Succeeded
                        ? "הועלה ל-ACC"
                        : upload.Outcome == EmailAccUploadOutcome.InProgress
                            ? "בטיפול על ידי משתמש אחר"
                            : upload.ErrorMessage,
                };

                ReplaceRowInDisplay(patched);
                RebindSelectedEmail(patched);

                if (!upload.Succeeded && upload.Outcome != EmailAccUploadOutcome.InProgress)
                {
                    throw new InvalidOperationException(upload.ErrorMessage ?? "העלאה ל-ACC נכשלה.");
                }

                return patched;
            }).ConfigureAwait(true);
    }

    private async Task ExecuteAccRowActionAsync(
        EmailListRow row,
        string busyText,
        Func<Task<EmailListRow>> action)
    {
        if (!_busyRowIds.Add(row.Id))
        {
            return;
        }

        var busy = row with
        {
            IsAccUploadBusy = true,
            AccUploadStatusText = busyText,
        };
        ReplaceRowInDisplay(busy);
        RebindSelectedEmail(busy);

        try
        {
            var result = await action().ConfigureAwait(true);
            StatusMessageChanged?.Invoke(this, result.AccStatusDisplay ?? busyText);
        }
        catch (Exception ex)
        {
            var failed = row with
            {
                IsAccUploadBusy = false,
                AccUploadStatusText = null,
                AccStatusDisplay = ex.Message,
                AccProcessingStatus = EmailAccProcessingStatus.Failed,
            };
            ReplaceRowInDisplay(failed);
            RebindSelectedEmail(failed);
            StatusMessageChanged?.Invoke(this, ex.Message);
        }
        finally
        {
            _busyRowIds.Remove(row.Id);
        }
    }

    private static EmailListRow ApplyAccStatus(EmailListRow row, EmailAccInboxStatus? status)
    {
        if (status is null)
        {
            return row with { IsAccStatusLoading = false };
        }

        return row with
        {
            IsAccStatusLoading = false,
            AccProcessingStatus = status.ProcessingStatus,
            AccStatusDisplay = status.StatusDisplay,
            InboxMessageId = status.InboxMessageId ?? row.InboxMessageId,
        };
    }

    private void PatchAccRow(EmailListRow updated)
    {
        ReplaceRowInDisplay(updated);
        RebindSelectedEmail(updated);
    }

    private string ResolveActingUserLogin()
    {
        try
        {
            return Environment.UserDomainName + "\\" + Environment.UserName;
        }
        catch
        {
            return Environment.UserName;
        }
    }

    internal Task FileEmailToProjectForTestsAsync(EmailListRow? row) => FileEmailToProjectAsync(row);

    internal Task UnfileEmailForTestsAsync(EmailListRow? row) => UnfileEmailAsync(row);

    internal Task MarkAsPersonalForTestsAsync(EmailListRow? row) =>
        SetEmailStatusAsync(row, EmailTriageStatus.Personal);

    internal Task MarkAsPendingForTestsAsync(EmailListRow? row) =>
        SetEmailStatusAsync(row, EmailTriageStatus.Pending);

    internal void ApplyLocalEmailMutationForTests(EmailListRow row) =>
        ApplyLocalEmailMutation(row);

    internal void ClearEmailStateForTests() => ClearEmailState();

    private void ClearEmailState()
    {
        _pageTokenStack.Clear();
        _nextPageToken = null;
        _lastUsedPageToken = null;
        CurrentPageNumber = 1;
        DisplayedCount = 0;
        HasNextPage = false;
        OnPropertyChanged(nameof(HasPreviousPage));

        SearchText = string.Empty;
        AddressFilter = string.Empty;
        SubjectFilter = string.Empty;
        SelectedLabel = null;
        SelectedMailboxScope = EmailMailboxScope.Inbox;
        SelectedProjectLinkFilter = EmailProjectLinkFilter.All;
        GroupByLabel = true;
        ClearProjectGroup();
        ClearDisplayGroups();
        MailboxUnreadTotal = 0;
        MailboxUnreadIsExact = true;
        _lastLoadedGmailQuery = null;
        _lastUnreadQuerySignature = null;

        AvailableLabels.Clear();

        FlatDisplayEmails.Clear();

        LoadWarning = null;
        LoadError = null;
        LoadState = EmailListLoadState.Idle;

        SelectedEmail = null;
        ReplaceRows([]);
    }

    private void OnAuthStateChanged(bool isAuthenticated)
    {
        UiThread.Run(() => _ = HandleAuthStateChangedOnUiThreadAsync(isAuthenticated));
    }

    private async Task HandleAuthStateChangedOnUiThreadAsync(bool isAuthenticated)
    {
        await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        if (!isAuthenticated)
        {
            ClearEmailState();
            if (!IsBusy)
            {
                StatusMessage = "לא מחובר ל-Gmail.";
            }
        }
    }

    private void NotifyAuthProperties()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectedAccountEmail));
        OnPropertyChanged(nameof(AccountStatusDisplay));
        OnPropertyChanged(nameof(ShowConnectButton));
        OnPropertyChanged(nameof(CanRefreshEmails));
        RaiseCommandStates();
        AccountStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshGmailAccountStatusAsync()
    {
        await _authService.RefreshAccountProfileAsync().ConfigureAwait(true);
        UiThread.Run(NotifyAuthProperties);
    }

    private Task RefreshAccountProfileAsync() => RefreshGmailAccountStatusAsync();

    private async Task LoadLabelsAsync()
    {
        try
        {
            var labels = await _emailGateway.GetMailboxLabelsAsync().ConfigureAwait(true);
            AvailableLabels.Clear();
            foreach (var label in labels)
            {
                AvailableLabels.Add(label);
            }
        }
        catch
        {
            LoadWarning = "רשימת labels לא נטענה.";
        }
    }

    private async Task ClearFiltersAsync()
    {
        SearchText = string.Empty;
        AddressFilter = string.Empty;
        SubjectFilter = string.Empty;
        SelectedLabel = null;
        SelectedMailboxScope = EmailMailboxScope.Inbox;
        SelectedProjectLinkFilter = EmailProjectLinkFilter.All;
        AttachmentsOnly = false;
        ClearDisplayGroups();
        await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);
    }

    private async Task ToggleAttachmentsOnlyAsync()
    {
        AttachmentsOnly = !AttachmentsOnly;
        ClearDisplayGroups();
        await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);
    }

    private void ToggleGroupByLabel()
    {
        GroupByLabel = !GroupByLabel;
        RebuildDisplayGroups();
        ApplyGrouping();
    }

    private void ApplyGrouping()
    {
        if (_emailsView is null)
        {
            return;
        }

        _emailsView.GroupDescriptions.Clear();
        _emailsView.Refresh();
    }

    private void ClearDisplayGroups()
    {
        _projectGroup = null;
        DisplayGroups.Clear();
        _hasLabelGroups = false;
        NotifyDisplayGroupProperties();
    }

    private void ClearProjectGroup()
    {
        _projectGroup?.ClearEmails();
        _projectGroup = null;
    }

    private void RebuildDisplayGroups()
    {
        var expandedByLabelId = DisplayGroups
            .Where(static g => !g.IsProjectGroup)
            .ToDictionary(static g => g.LabelId, static g => g.IsExpanded, StringComparer.Ordinal);

        var preservedProjectGroup = _projectGroup;
        DisplayGroups.Clear();

        var result = EmailListGroupBuilder.Rebuild(
            new EmailListGroupBuilder.RebuildInput(
                Emails,
                AvailableLabels,
                preservedProjectGroup,
                _projectContext?.ProjectLabelName,
                GroupByLabel,
                expandedByLabelId),
            CreateLabelGroup);

        _projectGroup = preservedProjectGroup;
        _hasLabelGroups = result.HasLabelGroups;

        foreach (var group in result.DisplayGroups)
        {
            DisplayGroups.Add(group);
        }

        FlatDisplayEmails.Clear();
        foreach (var row in result.FlatDisplayRows)
        {
            FlatDisplayEmails.Add(row);
        }

        NotifyDisplayGroupProperties();
    }

    private void NotifyDisplayGroupProperties()
    {
        OnPropertyChanged(nameof(HasLabelGroups));
        OnPropertyChanged(nameof(ShowLabelGroups));
        OnPropertyChanged(nameof(ShowFlatEmailList));
        OnPropertyChanged(nameof(ShowProjectGroupAboveFlat));
        OnPropertyChanged(nameof(ActiveProjectGroup));
    }

    private EmailLabelGroupViewModel CreateLabelGroup(string labelId, string labelDisplayName) =>
        new(
            labelId,
            labelDisplayName,
            LoadMoreEmailsForGroupAsync,
            LoadAllEmailsForGroupAsync);

    private async Task EnsureProjectGroupAsync(bool resetPaging)
    {
        if (_projectContext is null || !IsConnected)
        {
            ClearProjectGroup();
            return;
        }

        var header = _projectContext.GroupHeaderDisplay;
        if (_projectGroup is null
            || !string.Equals(_projectGroup.LabelDisplayName, header, StringComparison.Ordinal))
        {
            _projectGroup = new EmailLabelGroupViewModel(
                ResolveProjectGroupLabelId(),
                header,
                LoadMoreEmailsForGroupAsync,
                LoadAllEmailsForGroupAsync,
                EmailListGroupKind.Project);
        }

        if (resetPaging)
        {
            _projectGroup.ClearEmails();
            _projectGroup.ResetPagingState();
            await LoadProjectGroupPageAsync(_projectGroup, isInitialPage: true).ConfigureAwait(true);
        }
    }

    private string ResolveProjectGroupLabelId()
    {
        var projectLabelName = _projectContext?.ProjectLabelName;
        if (string.IsNullOrWhiteSpace(projectLabelName))
        {
            return $"project:{_projectContext?.ProjectId ?? 0}";
        }

        var match = AvailableLabels.FirstOrDefault(label =>
            string.Equals(label.Name, projectLabelName.Trim(), StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(match?.Id) ? match.Id : $"project:{projectLabelName.Trim()}";
    }

    private async Task LoadProjectGroupPageAsync(EmailLabelGroupViewModel group, bool isInitialPage)
    {
        if (_projectContext is null)
        {
            return;
        }

        group.IsLoading = true;
        group.ErrorMessage = null;

        try
        {
            var query = BuildProjectGroupQuery(isInitialPage ? ProjectEmailChunkSize : PageSize);
            var page = await _emailGateway
                .GetMailboxPageAsync(query, group.NextPageToken)
                .ConfigureAwait(true);

            var (rows, _) = await MapSummariesAsync(page.Items).ConfigureAwait(true);
            foreach (var row in rows)
            {
                group.TryAddEmail(row);
            }

            group.NextPageToken = page.NextPageToken;
            group.HasMore = page.HasNextPage;
            group.HasLoadedAll = !page.HasNextPage;
        }
        catch (Exception ex)
        {
            group.ErrorMessage = $"נטענו {group.LoadedCount} מיילים, אך הטעינה נעצרה בגלל שגיאה: {ex.Message}";
        }
        finally
        {
            group.IsLoading = false;
            group.NotifyHeaderChanged();
        }
    }

    private EmailMailboxQuery BuildProjectGroupQuery(int pageSize)
    {
        var query = BuildQuery() with { PageSize = pageSize };
        if (!string.IsNullOrWhiteSpace(_projectContext?.ProjectLabelName))
        {
            return query with { OptionalProjectLabel = _projectContext.ProjectLabelName.Trim() };
        }

        return query;
    }

    private EmailMailboxQuery BuildGroupQuery(EmailLabelGroupViewModel group)
    {
        if (group.IsProjectGroup)
        {
            return BuildProjectGroupQuery(group.NextPageToken is null ? ProjectEmailChunkSize : PageSize);
        }

        return BuildQuery() with
        {
            MailboxScope = EmailMailboxScope.Label,
            LabelId = EmailListGroupBuilder.IsSyntheticLabelId(group.LabelId) ? null : group.LabelId,
            LabelName = group.LabelDisplayName,
        };
    }

    private async Task LoadMoreEmailsForGroupAsync(EmailLabelGroupViewModel group)
    {
        if (!IsConnected || !group.SupportsRemotePaging)
        {
            return;
        }

        if (group.IsProjectGroup && _projectContext is null)
        {
            return;
        }

        if (!group.IsProjectGroup && string.IsNullOrWhiteSpace(group.LabelId))
        {
            return;
        }

        group.IsExpanded = true;
        group.IsLoading = true;
        group.ErrorMessage = null;
        StatusMessage = group.IsProjectGroup
            ? $"טוען מיילים של הפרויקט {group.LabelDisplayName}..."
            : $"טוען מיילים מהלייבל {group.LabelDisplayName}...";

        try
        {
            if (group.IsProjectGroup)
            {
                await LoadProjectGroupPageAsync(group, isInitialPage: group.NextPageToken is null).ConfigureAwait(true);
            }
            else
            {
                var query = BuildGroupQuery(group);
                var page = await _emailGateway
                    .GetMailboxPageAsync(query, group.NextPageToken)
                    .ConfigureAwait(true);

                var (rows, _) = await MapSummariesAsync(page.Items).ConfigureAwait(true);
                rows = ApplyClientRowFilters(rows);
                foreach (var row in rows)
                {
                    group.TryAddEmail(row);
                }

                group.NextPageToken = page.NextPageToken;
                group.HasMore = page.HasNextPage;
                if (!page.HasNextPage)
                {
                    group.HasLoadedAll = true;
                }
            }

            if (group.IsProjectGroup)
            {
                RebuildDisplayGroups();
            }
        }
        catch (Exception ex)
        {
            group.ErrorMessage = $"נטענו {group.LoadedCount} מיילים, אך הטעינה נעצרה בגלל שגיאה: {ex.Message}";
        }
        finally
        {
            group.IsLoading = false;
            group.NotifyHeaderChanged();
        }
    }

    private async Task LoadAllEmailsForGroupAsync(EmailLabelGroupViewModel group)
    {
        group.IsExpanded = true;
        group.ErrorMessage = null;

        for (var page = 0; page < MaxPagesPerLabelLoad && group.HasMore; page++)
        {
            await LoadMoreEmailsForGroupAsync(group).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(group.ErrorMessage))
            {
                return;
            }
        }

        if (group.HasMore)
        {
            group.ErrorMessage = $"נטענו {group.LoadedCount} מיילים. יש עוד — לחץ טען עוד.";
        }
        else
        {
            group.HasLoadedAll = true;
        }

        if (group.IsProjectGroup)
        {
            RebuildDisplayGroups();
        }

        group.NotifyHeaderChanged();
    }

    internal Task LoadMailboxAndProjectForTestsAsync(bool resetStack) =>
        LoadMailboxAndProjectAsync(resetStack);

    internal Task LoadMoreForLabelGroupForTestsAsync(EmailLabelGroupViewModel group) =>
        LoadMoreEmailsForGroupAsync(group);

    internal Task LoadAllForLabelGroupForTestsAsync(EmailLabelGroupViewModel group) =>
        LoadAllEmailsForGroupAsync(group);

    private async Task LoadPreviousPageAsync()
    {
        if (_pageTokenStack.Count == 0)
        {
            return;
        }

        var previousToken = _pageTokenStack.Pop();
        CurrentPageNumber = Math.Max(1, CurrentPageNumber - 1);
        await LoadPageAsync(resetStack: false, explicitToken: previousToken).ConfigureAwait(true);
    }

    private async Task LoadPageAsync(
        bool resetStack,
        bool useNextToken = false,
        string? explicitToken = null,
        bool skipDisplayRebuild = false)
    {
        LoadError = null;
        LoadWarning = null;

        if (!IsConnected)
        {
            LoadState = EmailListLoadState.Error;
            LoadError = "Gmail לא מחובר. התחבר ונסה שוב.";
            StatusMessage = LoadError;
            if (resetStack)
            {
                ReplaceRows([]);
            }

            return;
        }

        var previousSelectionId = SelectedEmail?.Id;
        IsBusy = true;
        LoadState = EmailListLoadState.Loading;
        StatusMessage = resetStack ? "טוען מיילים…" : "טוען עמוד…";

        try
        {
            string? requestToken;
            if (resetStack)
            {
                _pageTokenStack.Clear();
                CurrentPageNumber = 1;
                requestToken = null;
            }
            else if (useNextToken)
            {
                _pageTokenStack.Push(_lastUsedPageToken);
                CurrentPageNumber++;
                requestToken = _nextPageToken;
            }
            else
            {
                requestToken = explicitToken;
            }

            _lastUsedPageToken = requestToken;

            var query = BuildQuery();
            _lastLoadedGmailQuery = EmailMailboxQueryComposer.BuildSearchQuery(query);
            var refreshUnreadTotal = ShouldRefreshUnreadTotal(query, resetStack);

            var pageTask = _emailGateway.GetMailboxPageAsync(query, requestToken);
            var unreadTask = refreshUnreadTotal
                ? _emailGateway.GetMailboxUnreadCountAsync(query)
                : Task.FromResult(new EmailMailboxUnreadCount(MailboxUnreadTotal, MailboxUnreadIsExact));

            await Task.WhenAll(pageTask, unreadTask).ConfigureAwait(true);
            var page = await pageTask.ConfigureAwait(true);
            var unreadCount = await unreadTask.ConfigureAwait(true);

            if (refreshUnreadTotal)
            {
                ApplyMailboxUnreadCount(unreadCount);
            }

            _nextPageToken = page.NextPageToken;
            HasNextPage = page.HasNextPage;

            var (rows, enrichmentWarning) = await MapSummariesAsync(page.Items).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(enrichmentWarning))
            {
                LoadWarning = enrichmentWarning;
            }

            rows = ApplyClientRowFilters(rows);

            ReplaceRows(rows, previousSelectionId, skipDisplayRebuild);
            DisplayedCount = rows.Count;
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(HasPreviousPage));
            (LoadPreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

            if (rows.Count == 0)
            {
                LoadState = EmailListLoadState.NoResults;
                StatusMessage = "לא נמצאו מיילים לפי הסינון הנוכחי.";
            }
            else
            {
                LoadState = string.IsNullOrWhiteSpace(LoadWarning)
                    ? EmailListLoadState.Loaded
                    : EmailListLoadState.PartialFailure;
                StatusMessage = $"נטענו {rows.Count} מיילים (עמוד {CurrentPageNumber}).";
                if (ShowSparsePageWarning)
                {
                    StatusMessage += " סינון שיוך פעיל — ייתכן פחות מ-50 תוצאות בדף.";
                }
            }
        }
        catch (Exception ex)
        {
            LoadState = EmailListLoadState.Error;
            LoadError = $"טעינת המיילים נכשלה: {ex.Message}";
            StatusMessage = LoadError;
            if (resetStack && Emails.Count == 0)
            {
                ReplaceRows([]);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private EmailMailboxQuery BuildQuery()
    {
        var scope = SelectedMailboxScope;
        if (!string.IsNullOrWhiteSpace(SelectedLabel))
        {
            scope = EmailMailboxScope.Label;
        }

        return new EmailMailboxQuery
        {
            FreeText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Subject = string.IsNullOrWhiteSpace(SubjectFilter) ? null : SubjectFilter.Trim(),
            FromOrTo = string.IsNullOrWhiteSpace(AddressFilter) ? null : AddressFilter.Trim(),
            LabelName = SelectedLabel,
            MailboxScope = scope,
            ProjectLinkFilter = SelectedProjectLinkFilter,
            AttachmentsOnly = AttachmentsOnly,
            PageSize = PageSize,
        };
    }

    private bool ShouldRefreshUnreadTotal(EmailMailboxQuery query, bool resetStack) =>
        resetStack || !string.Equals(_lastUnreadQuerySignature, BuildUnreadQuerySignature(query), StringComparison.Ordinal);

    private static string BuildUnreadQuerySignature(EmailMailboxQuery query) =>
        $"{query.MailboxScope}|{query.LabelName}|{query.Subject}|{query.FromOrTo}|{query.FreeText}|{query.ProjectLinkFilter}|{query.AttachmentsOnly}";

    private void ApplyMailboxUnreadCount(EmailMailboxUnreadCount unreadCount)
    {
        MailboxUnreadTotal = unreadCount.Count;
        MailboxUnreadIsExact = unreadCount.IsExact;
        _lastUnreadQuerySignature = BuildUnreadQuerySignature(BuildQuery());
        NotifyUnreadDisplayProperties();
    }

    private void NotifyUnreadDisplayProperties()
    {
        OnPropertyChanged(nameof(UnreadInCurrentPage));
        OnPropertyChanged(nameof(UnreadCountDisplay));
        OnPropertyChanged(nameof(ShowUnreadCount));
        OnPropertyChanged(nameof(MailboxDiagnostics));
    }

    private void NotifyDisplayModeProperties()
    {
        OnPropertyChanged(nameof(DisplayMode));
        OnPropertyChanged(nameof(HasActiveProject));
        OnPropertyChanged(nameof(IsProjectMode));
        OnPropertyChanged(nameof(IsAllEmailsMode));
        OnPropertyChanged(nameof(ShowAllEmailsPaging));
        OnPropertyChanged(nameof(ShowProjectGroupChrome));
        OnPropertyChanged(nameof(SelectedProjectContext));
        OnPropertyChanged(nameof(DisplayModeSummary));
        OnPropertyChanged(nameof(ProjectGroupHeader));
        OnPropertyChanged(nameof(PageInfo));
        OnPropertyChanged(nameof(ActiveProjectGroup));
        (ToggleGroupByLabelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleAttachmentsOnlyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ApplyFiltersCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task<(IReadOnlyList<EmailListRow> Rows, string? EnrichmentWarning)> MapSummariesAsync(
        IReadOnlyList<EmailSummary> summaries)
    {
        if (summaries.Count == 0)
        {
            return ([], null);
        }

        IReadOnlyDictionary<string, EmailProjectLinkInfo> linkStates =
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        string? enrichmentWarning = null;

        if (_threadLinkQuery is not null)
        {
            var ids = summaries
                .Select(static summary => summary.InternetMessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .ToList();

            if (ids.Count > 0)
            {
                try
                {
                    linkStates = await _threadLinkQuery
                        .GetLinkStatesByInternetMessageIdsAsync(ids)
                        .ConfigureAwait(true);
                }
                catch
                {
                    enrichmentWarning = "הודעות נטענו, אך מצב שיוך לפרויקט לא נטען.";
                }
            }
        }

        var rows = new List<EmailListRow>(summaries.Count);
        foreach (var summary in summaries)
        {
            rows.Add(ToEmailListRow(summary, linkStates));
        }

        return (rows, enrichmentWarning);
    }

    private EmailListRow ToEmailListRow(
        EmailSummary summary,
        IReadOnlyDictionary<string, EmailProjectLinkInfo> linkStates)
    {
        EmailProjectLinkInfo? link = null;
        if (!string.IsNullOrWhiteSpace(summary.InternetMessageId))
        {
            var key = summary.InternetMessageId.Trim().Trim('<', '>');
            linkStates.TryGetValue(key, out link);
            if (link is null)
            {
                linkStates.TryGetValue(summary.InternetMessageId, out link);
            }
        }

        var isLinkedFromLabels = InferLinkedFromLabels(summary.LabelNames);
        var isLinked = link?.IsLinked == true || isLinkedFromLabels;
        var projectDisplay = isLinked
            ? link?.DisplayName ?? summary.PrimaryLabel ?? "משויך"
            : "לא משויך";

        var labelChipNames = FilterDisplayLabels(summary.LabelNames);
        var labelChips = FilterDisplayLabelChips(summary.LabelChips, summary.LabelNames);
        var labelsDisplay = labelChipNames.Count > 0
            ? string.Join(", ", labelChipNames)
            : string.Empty;
        var isFiledToProject = IsFiledToProject(summary.LabelNames);
        var filedProjectLabelPath = EmailGmailLabelNames.FindProjectLabelPath(summary.LabelNames);
        var isFiledToSameProject = IsFiledToSameProjectForMapping(
            isFiledToProject,
            link?.ProjectId,
            filedProjectLabelPath);

        return new EmailListRow(
            Id: summary.MessageId,
            Sender: summary.From.Value,
            Subject: string.IsNullOrWhiteSpace(summary.Subject) ? "(ללא נושא)" : summary.Subject,
            Preview: string.IsNullOrWhiteSpace(summary.Snippet)
                ? (summary.HasAttachments ? "יש קבצים מצורפים" : string.Empty)
                : summary.Snippet,
            ReceivedOn: summary.ReceivedAt == DateTimeOffset.MinValue ? DateTime.MinValue : summary.ReceivedAt.LocalDateTime,
            GroupName: summary.PrimaryLabel ?? "ללא label",
            IsUnread: summary.IsUnread,
            IsAssigned: isLinked,
            AssignedProjectName: isLinked ? projectDisplay : null,
            AttachmentCount: summary.AttachmentCount,
            InternetMessageId: summary.InternetMessageId,
            To: summary.To?.Value ?? string.Empty,
            Snippet: summary.Snippet ?? string.Empty,
            LabelsDisplay: labelsDisplay,
            PrimaryLabel: summary.PrimaryLabel ?? "ללא label",
            ProjectLinkState: isLinked ? EmailProjectLinkState.Linked : EmailProjectLinkState.Unlinked,
            ProjectId: link?.ProjectId,
            ProjectNumber: link?.ProjectNumber,
            ProjectName: link?.ProjectName,
            ProjectDisplay: projectDisplay,
            LabelChipNames: labelChipNames,
            LabelChips: labelChips,
            ThreadId: summary.ThreadId,
            InboxMessageId: link?.InboxMessageId,
            ThreadUniqueId: link?.ThreadUniqueId,
            IsFiledToProject: isFiledToProject,
            IsFiledToSameProject: isFiledToSameProject,
            FiledProjectLabelPath: filedProjectLabelPath,
            RowBackgroundColor: ResolveRowBackgroundColor(summary.LabelNames, isFiledToProject, link?.ProjectId));
    }

    private bool IsFiledToSameProject(EmailListRow row) =>
        row.IsFiledToSameProject;

    private bool IsFiledToSameProjectForMapping(
        bool isFiledToProject,
        int? linkedProjectId,
        string? filedProjectLabelPath)
    {
        if (!isFiledToProject)
        {
            return false;
        }

        var current = _currentProject?.CurrentProject;
        if (current is null)
        {
            return false;
        }

        if (linkedProjectId == current.ProjectId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(current.ProjectLabelName) || string.IsNullOrWhiteSpace(filedProjectLabelPath))
        {
            return false;
        }

        return filedProjectLabelPath.EndsWith(
            "/" + current.ProjectLabelName,
            StringComparison.OrdinalIgnoreCase)
            || filedProjectLabelPath.EndsWith(
                current.ProjectLabelName,
                StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveRowBackgroundColor(
        IReadOnlyList<string>? labelNames,
        bool isFiledToProject,
        int? linkedProjectId)
    {
        if (labelNames is not null)
        {
            if (labelNames.Any(static label =>
                    label.Equals(EmailGmailLabelNames.Pending, StringComparison.OrdinalIgnoreCase)))
            {
                return "#F3E5F5";
            }

            if (labelNames.Any(static label =>
                    label.Equals(EmailGmailLabelNames.Personal, StringComparison.OrdinalIgnoreCase)
                    || label.Equals(EmailGmailLabelNames.Irrelevant, StringComparison.OrdinalIgnoreCase)))
            {
                return "#E3F2FD";
            }
        }

        if (!isFiledToProject)
        {
            return null;
        }

        var currentProjectId = _currentProject?.CurrentProject?.ProjectId;
        if (currentProjectId.HasValue && linkedProjectId == currentProjectId)
        {
            return "#C8E6C9";
        }

        return "#E0E0E0";
    }

    private void RefreshRowBackgrounds()
    {
        if (Emails.Count == 0)
        {
            return;
        }

        var updated = false;
        for (var index = 0; index < Emails.Count; index++)
        {
            var row = Emails[index];
            var background = ResolveRowBackgroundColor(
                row.LabelChipNames,
                row.IsFiledToProject,
                row.ProjectId);
            var isFiledToSameProject = IsFiledToSameProjectForMapping(
                row.IsFiledToProject,
                row.ProjectId,
                row.FiledProjectLabelPath);
            if (background == row.RowBackgroundColor && isFiledToSameProject == row.IsFiledToSameProject)
            {
                continue;
            }

            Emails[index] = row with
            {
                RowBackgroundColor = background,
                IsFiledToSameProject = isFiledToSameProject,
            };
            updated = true;
        }

        if (updated)
        {
            RebuildDisplayGroups();
            RaiseCommandStates();
        }
    }

    private static bool IsFiledToProject(IReadOnlyList<string>? labelNames) =>
        labelNames?.Any(static label => EmailGmailLabelNames.IsProjectLabel(label)) == true;

    private static IReadOnlyList<EmailLabelChip> FilterDisplayLabelChips(
        IReadOnlyList<EmailLabelChip>? labelChips,
        IReadOnlyList<string>? labelNames)
    {
        if (labelChips is { Count: > 0 })
        {
            return labelChips
                .Where(static chip => !IsSystemGmailLabel(chip.DisplayName))
                .DistinctBy(static chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return FilterDisplayLabels(labelNames)
            .Select(static name => new EmailLabelChip(name))
            .ToList();
    }

    private static IReadOnlyList<string> FilterDisplayLabels(IReadOnlyList<string>? labelNames)
    {
        if (labelNames is null || labelNames.Count == 0)
        {
            return [];
        }

        return labelNames
            .Where(static label => !IsSystemGmailLabel(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSystemGmailLabel(string label) =>
        label.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
        || label.Equals("UNREAD", StringComparison.OrdinalIgnoreCase)
        || label.Equals("SENT", StringComparison.OrdinalIgnoreCase)
        || label.Equals("DRAFT", StringComparison.OrdinalIgnoreCase)
        || label.Equals("SPAM", StringComparison.OrdinalIgnoreCase)
        || label.Equals("TRASH", StringComparison.OrdinalIgnoreCase)
        || label.Equals("STARRED", StringComparison.OrdinalIgnoreCase)
        || label.Equals("IMPORTANT", StringComparison.OrdinalIgnoreCase)
        || label.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase);

    private static bool InferLinkedFromLabels(IReadOnlyList<string>? labelNames)
    {
        if (labelNames is null || labelNames.Count == 0)
        {
            return false;
        }

        return labelNames.Any(static label => EmailGmailLabelNames.IsProjectLabel(label));
    }

    private IReadOnlyList<EmailListRow> ApplyClientRowFilters(IReadOnlyList<EmailListRow> rows)
    {
        var filtered = SelectedProjectLinkFilter switch
        {
            EmailProjectLinkFilter.Linked => rows.Where(static row => row.IsLinked).ToList(),
            EmailProjectLinkFilter.Unlinked => rows.Where(static row => !row.IsLinked).ToList(),
            _ => rows,
        };

        if (AttachmentsOnly)
        {
            filtered = filtered.Where(static row => row.HasAttachments).ToList();
        }

        return filtered;
    }

    private void ReplaceRows(IReadOnlyList<EmailListRow> rows, string? preserveSelectionId = null, bool skipDisplayRebuild = false)
    {
        Emails.Clear();
        foreach (var row in rows)
        {
            Emails.Add(row);
        }

        ApplyGrouping();
        if (!skipDisplayRebuild)
        {
            RebuildDisplayGroups();
        }

        var selectionPool = FlatDisplayEmails.Count > 0 ? FlatDisplayEmails : Emails;
        SelectedEmail = preserveSelectionId is null
            ? selectionPool.FirstOrDefault() ?? _projectGroup?.Emails.FirstOrDefault()
            : selectionPool.FirstOrDefault(row => string.Equals(row.Id, preserveSelectionId, StringComparison.Ordinal))
              ?? _projectGroup?.Emails.FirstOrDefault(row => string.Equals(row.Id, preserveSelectionId, StringComparison.Ordinal))
              ?? selectionPool.FirstOrDefault()
              ?? _projectGroup?.Emails.FirstOrDefault();
        OnPropertyChanged(nameof(UnreadInCurrentPage));
        OnPropertyChanged(nameof(UnreadCountDisplay));
        OnPropertyChanged(nameof(ShowUnreadCount));
        OnPropertyChanged(nameof(MailboxDiagnostics));
    }

    private void RaiseCommandStates()
    {
        (LoadFirstPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (LoadNextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (LoadPreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RefreshPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ApplyFiltersCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearFiltersCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ConnectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (FileEmailToProjectCommand as AsyncRelayCommand<EmailListRow>)?.RaiseCanExecuteChanged();
        (UnfileEmailCommand as AsyncRelayCommand<EmailListRow>)?.RaiseCanExecuteChanged();
        (MarkAsPendingCommand as AsyncRelayCommand<EmailListRow>)?.RaiseCanExecuteChanged();
        (MarkAsPersonalCommand as AsyncRelayCommand<EmailListRow>)?.RaiseCanExecuteChanged();
        (MarkAsIrrelevantCommand as AsyncRelayCommand<EmailListRow>)?.RaiseCanExecuteChanged();
    }

    private sealed class DesignEmailListGateway : IEmailGateway
    {
        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(string location, string projectName, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(string projectLabelName, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(EmailMailboxQuery query, string? pageToken = null, CancellationToken cancellationToken = default)
        {
            var items = EmailWindowDesignData.SampleEmails
                .Select(static row => new EmailSummary(
                    row.Id,
                    $"thread-{row.Id}",
                    EmailAddress.CreateOrFallback(row.Sender),
                    row.Subject,
                    row.ReceivedOn == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(row.ReceivedOn),
                    row.AttachmentCount,
                    InternetMessageId: null,
                    To: null,
                    Snippet: row.Preview,
                    LabelNames: [row.GroupName],
                    PrimaryLabel: row.GroupName,
                    IsUnread: row.IsUnread))
                .ToList();

            return Task.FromResult(new EmailMailboxPage(items, query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GmailLabelInfo>>([
                new GmailLabelInfo("INBOX", "INBOX"),
                new GmailLabelInfo("lbl1", "פרויקטים_משרד/תל אביב/1042 — דוגמה"),
            ]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(2, IsExact: true, EmailMailboxQueryComposer.DescribeMailboxScope(query)));
    }

    private sealed class DesignAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; } = true;

        public string? ConnectedAccountEmail { get; private set; } = "design@example.com";

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
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

public sealed record EmailProjectLinkFilterOption(EmailProjectLinkFilter Value, string DisplayName);
