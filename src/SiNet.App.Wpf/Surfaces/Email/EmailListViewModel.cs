using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email.Internal;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Identity;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Standalone email list component: paged Gmail read, account bar, filters, Outlook-style cards,
/// and read-only project-link display.
/// </summary>
public sealed partial class EmailListViewModel : ObservableObject, IEmailListRowMutator
{
    public const int PageSize = EmailMailboxQuery.DefaultPageSize;
    public const int ProjectEmailChunkSize = 10;
    internal const int MaxPagesPerLabelLoad = 20;

    private readonly IEmailGateway _emailGateway;
    private readonly IEmailThreadLinkQueryService? _threadLinkQuery;
    private readonly IEmailThreadMappingSyncService? _threadMappingSync;
    private readonly IConnectorAuthService _authService;
    private readonly IEmailFilingService? _filingService;
    private readonly IEmailStatusService? _statusService;
    private readonly EmailAccSelectionHandler? _accHandler;
    private readonly IGoogleIngestSessionEnsurer? _ingestSessionEnsurer;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IProjectGmailLabelSyncService? _projectLabelSync;
    private readonly IGmailMailboxLabelAuditService? _labelAudit;

    private readonly EmailListRowDisplayCoordinator _display;
    private readonly EmailListGroupingCoordinator _grouping;
    private readonly EmailListPagingCoordinator _paging;
    private readonly EmailListFilingCoordinator _filing;

    private EmailListRow? _selectedEmail;
    private string? _selectedEmailId;
    private bool _isBusy;
    private string? _nextPageToken;
    private string? _lastUsedPageToken;
    private int _currentPageNumber = 1;
    private int _displayedCount;
    private bool _hasNextPage;
    private bool _groupByLabel = true;
    private bool _attachmentsOnly;
    private bool _unreadOnly;
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
    private string? _followQuoteThreadFilter;
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
        IEmailMoveToProjectCoordinator? moveToProjectCoordinator = null,
        IEmailAccIngestQueue? accIngestQueue = null,
        IGoogleIngestSessionEnsurer? ingestSessionEnsurer = null,
        IEmailThreadMappingSyncService? threadMappingSync = null,
        IProjectGmailLabelSyncService? projectLabelSync = null,
        IGmailMailboxLabelAuditService? labelAudit = null)
    {
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _threadLinkQuery = threadLinkQuery;
        _threadMappingSync = threadMappingSync;
        _projectLabelSync = projectLabelSync;
        _labelAudit = labelAudit;
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _filingService = filingService;
        _statusService = statusService;
        _currentProject = currentProject;
        _currentUser = currentUser;
        _ingestSessionEnsurer = ingestSessionEnsurer;
        _ = moveToProjectCoordinator;

        Emails = [];
        FlatDisplayEmails = [];
        AvailableLabels = [];
        DisplayGroups = [];
        PageTokenStack = new Stack<string?>();
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

        var groupBridge = new CoordinatorGroupBridge();
        var pagingBridge = new CoordinatorPagingBridge();
        _display = new EmailListRowDisplayCoordinator(
            this,
            () => groupBridge.RebuildDisplayGroups(),
            () => groupBridge.ApplyGrouping(),
            () => _currentProject?.CurrentProject);
        _grouping = new EmailListGroupingCoordinator(
            this,
            _display,
            () => pagingBridge.BuildQuery(),
            (query, pageSize) => pagingBridge.BuildProjectGroupQuery(query, pageSize));
        _paging = new EmailListPagingCoordinator(this, _display, _grouping);
        groupBridge.Bind(_grouping);
        pagingBridge.Bind(_paging);
        _filing = new EmailListFilingCoordinator(this, _display, _paging);

        LoadFirstPageCommand = new AsyncRelayCommand(() => _paging.ReloadForContextAsync(), CanLoadEmails);
        LoadNextPageCommand = new AsyncRelayCommand(
            () => _paging.LoadPageAsync(resetStack: false, useNextToken: true),
            () => CanLoadEmails() && HasNextPage);
        LoadPreviousPageCommand = new AsyncRelayCommand(
            () => _paging.LoadPreviousPageAsync(),
            () => CanLoadEmails() && HasPreviousPage);
        RefreshPageCommand = new AsyncRelayCommand(() => _paging.ReloadForContextAsync(), CanLoadEmails);
        ApplyFiltersCommand = new AsyncRelayCommand(() => _paging.ReloadForContextAsync(), CanLoadEmails);
        ClearFiltersCommand = new AsyncRelayCommand(() => _paging.ClearFiltersAsync(), () => !IsBusy);
        SyncProjectLabelNamesCommand = new AsyncRelayCommand(
            () => TrySyncProjectLabelNamesAsync(force: true),
            () => IsConnected && !IsBusy && _projectLabelSync is not null);
        AuditMailboxLabelsCommand = new AsyncRelayCommand(
            AuditMailboxLabelsAsync,
            () => !IsBusy && _labelAudit is not null);
        ToggleGroupByLabelCommand = new RelayCommand(_ => _grouping.ToggleGroupByLabel());
        ToggleAttachmentsOnlyCommand = new AsyncRelayCommand(() => _paging.ToggleAttachmentsOnlyAsync(), CanLoadEmails);
        ToggleUnreadOnlyCommand = new AsyncRelayCommand(() => _paging.ToggleUnreadOnlyAsync(), CanLoadEmails);
        FileEmailToProjectCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.FileEmailToProjectAsync(row),
            _filing.CanFileEmailToProject,
            allowConcurrentParameters: true);
        FileEmailToThreadProjectCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.FileEmailToThreadProjectAsync(row),
            _filing.CanFileEmailToThreadProject,
            allowConcurrentParameters: true);
        UnfileEmailCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.UnfileEmailAsync(row),
            _filing.CanUnfileEmail,
            allowConcurrentParameters: true);
        MarkAsPendingCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.SetEmailStatusAsync(row, EmailTriageStatus.Pending),
            _filing.CanSetEmailStatus,
            allowConcurrentParameters: true);
        MarkAsPersonalCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.SetEmailStatusAsync(row, EmailTriageStatus.Personal),
            _filing.CanSetEmailStatus,
            allowConcurrentParameters: true);
        MarkAsIrrelevantCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.SetEmailStatusAsync(row, EmailTriageStatus.Irrelevant),
            _filing.CanSetEmailStatus,
            allowConcurrentParameters: true);
        MarkAsFyiCommand = new AsyncRelayCommand<EmailListRow>(
            row => _filing.SetEmailStatusAsync(row, EmailTriageStatus.Fyi),
            _filing.CanMarkAsFyi,
            allowConcurrentParameters: true);
        UploadToAccInboxCommand = new AsyncRelayCommand<EmailListRow>(UploadToAccInboxAsync, CanUploadToAccInbox, allowConcurrentParameters: true);
        ConnectCommand = new AsyncRelayCommand(() => _paging.ConnectAsync(), () => !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(() => _paging.DisconnectGmailAsync(), () => IsConnected && !IsBusy);

        _authService.AuthStateChanged += OnAuthStateChanged;

        if (_currentProject is not null)
        {
            _currentProject.CurrentProjectChanged += (_, _) =>
            {
                _display.RefreshRowBackgrounds();
                RaiseCommandStates();
            };
        }

        _accHandler = accStatusService is not null || accUploadCoordinator is not null || accIngestQueue is not null
            ? new EmailAccSelectionHandler(accStatusService, accUploadCoordinator, PatchAccRow, accIngestQueue, FindRowById)
            : null;
        if (_accHandler is not null)
        {
            _accHandler.StatusMessageChanged += message => StatusMessageChanged?.Invoke(this, message);
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
    public string DisplayModeSummary => HasActiveProject ? "מצב: כל המיילים + פרויקט" : "מצב: כל המיילים";
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
            var newId = value?.Id;
            var idChanged = !string.Equals(_selectedEmail?.Id, newId, StringComparison.Ordinal);
            if (!idChanged && (value is null || ReferenceEquals(_selectedEmail, value)))
            {
                return;
            }

            _selectedEmail = value;
            if (!string.Equals(_selectedEmailId, newId, StringComparison.Ordinal))
            {
                _selectedEmailId = newId;
                OnPropertyChanged(nameof(SelectedEmailId));
            }

            OnPropertyChanged(nameof(SelectedEmail));
            if (idChanged)
            {
                SelectedEmailChanged?.Invoke(this, value);
            }
        }
    }

    public string? SelectedEmailId
    {
        get => _selectedEmailId;
        set
        {
            if (string.Equals(_selectedEmailId, value, StringComparison.Ordinal))
            {
                return;
            }

            if (value is null
                && _selectedEmailId is not null
                && _display.ResolveSelectionRow(_selectedEmailId) is not null)
            {
                return;
            }

            var row = value is null ? null : _display.ResolveSelectionRow(value);
            if (value is not null
                && row is null
                && string.Equals(_selectedEmail?.Id, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedEmailId = value;
            OnPropertyChanged(nameof(SelectedEmailId));

            var idChanged = !string.Equals(_selectedEmail?.Id, value, StringComparison.Ordinal);
            _selectedEmail = row;
            OnPropertyChanged(nameof(SelectedEmail));
            if (idChanged)
            {
                SelectedEmailChanged?.Invoke(this, row);
            }
        }
    }

    internal void SyncSelectedEmailInstance(EmailListRow? candidate)
    {
        if (candidate is null || !string.Equals(_selectedEmailId, candidate.Id, StringComparison.Ordinal))
        {
            return;
        }

        var resolved = _display.ResolveSelectionRow(candidate.Id) ?? candidate;
        if (ReferenceEquals(_selectedEmail, resolved))
        {
            return;
        }

        _selectedEmail = resolved;
        OnPropertyChanged(nameof(SelectedEmail));
    }

    public event EventHandler<EmailListRow?>? SelectedEmailChanged;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<string>? AccStatusPatched;
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

    public bool ShowUnreadFilterActive => UnreadOnly || SelectedMailboxScope == EmailMailboxScope.Unread;

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
                OnPropertyChanged(nameof(CanRefreshEmails));
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
    internal EmailListRow? FindRowForTests(string rowId) => _display.FindRowById(rowId);

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

            OnPropertyChanged(nameof(ShowUnreadFilterActive));

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

    /// <summary>
    /// Optional client-side thread filter for FollowQuoteApproval (SendQuote anchor).
    /// Applied in <see cref="EmailListRowDisplayCoordinator.ApplyClientRowFilters"/>.
    /// </summary>
    public string? FollowQuoteThreadFilter
    {
        get => _followQuoteThreadFilter;
        set => SetField(ref _followQuoteThreadFilter, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
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
                NotifyDisplayGroupPropertiesChanged();
            }
        }
    }

    public bool AttachmentsOnly
    {
        get => _attachmentsOnly;
        private set => SetField(ref _attachmentsOnly, value);
    }

    public bool UnreadOnly
    {
        get => _unreadOnly;
        private set
        {
            if (SetField(ref _unreadOnly, value))
            {
                OnPropertyChanged(nameof(ShowUnreadFilterActive));
            }
        }
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

    public bool HasPreviousPage => PageTokenStack.Count > 0;

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
    public ICommand SyncProjectLabelNamesCommand { get; }
    public ICommand AuditMailboxLabelsCommand { get; }
    public ICommand ToggleGroupByLabelCommand { get; }
    public ICommand ToggleAttachmentsOnlyCommand { get; }
    public ICommand ToggleUnreadOnlyCommand { get; }
    public ICommand FileEmailToProjectCommand { get; }
    public ICommand FileEmailToThreadProjectCommand { get; }
    public ICommand UnfileEmailCommand { get; }
    public ICommand MarkAsPendingCommand { get; }
    public ICommand MarkAsPersonalCommand { get; }
    public ICommand MarkAsIrrelevantCommand { get; }
    public ICommand MarkAsFyiCommand { get; }
    public ICommand UploadToAccInboxCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public async Task ApplyProjectContextAsync(EmailListProjectContext? context)
    {
        if (Equals(_projectContext, context))
        {
            return;
        }

        _projectContext = context;
        _grouping.ClearProjectGroup();
        NotifyDisplayModeProperties();

        if (IsConnected)
        {
            await _paging.ReloadForContextAsync().ConfigureAwait(true);
        }
    }

    public Task RefreshPageAsync() => _paging.ReloadForContextAsync();
    public Task ApplyFiltersAsync() => _paging.ReloadForContextAsync();
    public Task ClearFiltersAndReloadAsync() => _paging.ClearFiltersAsync();
    public Task LoadNextPageAsync() => _paging.LoadPageAsync(resetStack: false, useNextToken: true);
    public Task LoadPreviousPagePublicAsync() => _paging.LoadPreviousPageAsync();

    public async Task InitializeAsync()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectedAccountEmail));
        OnPropertyChanged(nameof(AccountStatusDisplay));

        if (!IsConnected)
        {
            return;
        }

        await _paging.RefreshAccountProfileAsync().ConfigureAwait(true);
        await _paging.LoadLabelsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// When <c>Email.AutoSyncProjectLabelNames</c> is on (or <paramref name="force"/>), rename
    /// Gmail project leaf labels to current <c>NameAndNumber</c>. Duplicate numbers open a
    /// keep/delete decision dialog (DEV-009 Layer B).
    /// </summary>
    internal async Task TrySyncProjectLabelNamesAsync(bool force = false)
    {
        if (_projectLabelSync is null || !IsConnected)
            return;

        try
        {
            var result = await _projectLabelSync.SyncAsync(force, CancellationToken.None).ConfigureAwait(true);
            if (!force && !result.SettingEnabled)
                return;

            result = await ResolveDuplicateLabelDecisionsAsync(result).ConfigureAwait(true);

            var ambiguousOnly = result.NeedsUserDecision
                .GroupBy(i => i.ProjectNumber)
                .Where(g => g.Count() == 1)
                .SelectMany(g => g)
                .ToList();
            if (ambiguousOnly.Count > 0)
            {
                var lines = string.Join(
                    Environment.NewLine,
                    ambiguousOnly.Select(i => $"• ({i.ProjectNumber}) {i.CurrentFullPath}: {i.Message}"));
                System.Windows.MessageBox.Show(
                    "נותרו לייבלים שלא ניתן ליישב אוטומטית (למשל מספר כפול ב-DB):"
                    + Environment.NewLine + lines,
                    "סנכרון שמות לייבלים",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            if (result.RenamedCount > 0)
            {
                SetStatusMessage($"סונכרנו {result.RenamedCount} שמות לייבלים.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[EmailList] Label name sync failed: {ex}");
            // Material ops: also surface on Llog when Serilog is wired (local Trace stays for debug hosts).
            Serilog.Log.Warning(ex, "[EmailList] outcome=Failed op=LabelNameSync detail={Message}", ex.Message);
            SetLoadWarning($"סנכרון שמות לייבלים נכשל: {ex.Message}");
        }
    }

    internal async Task AuditMailboxLabelsAsync()
    {
        const string notConnected = "Gmail לא מחובר. התחבר ונסה שוב.";
        if (_labelAudit is null)
            return;

        if (!IsConnected)
        {
            SetLoadError(notConnected);
            if (System.Windows.Application.Current is not null)
            {
                System.Windows.MessageBox.Show(
                    notConnected,
                    "בדיקת תיוג",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }

            return;
        }

        try
        {
            IsBusy = true;
            var rows = await _labelAudit.AuditAsync(CancellationToken.None).ConfigureAwait(true);
            var dialog = new GmailMailboxLabelAuditWindow(rows);
            var owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive);
            if (owner is not null)
                dialog.Owner = owner;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[EmailList] Label audit failed: {ex}");
            Serilog.Log.Warning(ex, "[EmailList] outcome=Failed op=LabelAudit detail={Message}", ex.Message);
            SetLoadWarning($"בדיקת תיוג נכשלה: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ProjectGmailLabelSyncResult> ResolveDuplicateLabelDecisionsAsync(
        ProjectGmailLabelSyncResult result)
    {
        if (_projectLabelSync is null)
            return result;

        var duplicateItems = result.NeedsUserDecision
            .GroupBy(i => i.ProjectNumber)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        if (duplicateItems.Count == 0)
            return result;

        var dialog = new GmailDuplicateLabelDecisionDialog(duplicateItems);
        var owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(w => w.IsActive);
        if (owner is not null)
            dialog.Owner = owner;

        if (dialog.ShowDialog() != true)
            return result;

        var errorLines = new List<string>();
        foreach (var (number, keepId) in dialog.KeepSelections)
        {
            var resolve = await _projectLabelSync
                .ResolveDuplicateLeavesAsync(number, keepId, CancellationToken.None)
                .ConfigureAwait(true);
            if (resolve.Errors.Count > 0)
            {
                errorLines.AddRange(
                    resolve.Errors.Select(e => $"({number}) {e}"));
            }
        }

        if (errorLines.Count > 0)
        {
            System.Windows.MessageBox.Show(
                "חלק מהמחיקות נכשלו:" + Environment.NewLine
                + string.Join(Environment.NewLine, errorLines),
                "סנכרון שמות לייבלים",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        // Re-sync so the survivor is renamed to NameAndNumber when auto-sync / force applies.
        return await _projectLabelSync.SyncAsync(force: true, CancellationToken.None).ConfigureAwait(true);
    }

    public bool TrySelectByInboxCorrelation(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress) =>
        _display.TrySelectByInboxCorrelation(messageUniqueId, internetMessageId, subject, fromAddress);

    /// <summary>
    /// Direct task email locate: GetByIdAsync (real Gmail API id only) / rfc822msgid search, then inject+select.
    /// Does not depend on the current mailbox page (top-N). Returns null when Gmail cannot resolve the message.
    /// </summary>
    /// <param name="messageUniqueId">
    /// SQL <c>MessageUniqueId</c> — often an RFC822 id (see <c>EmailMessageIdentity</c>), not a Gmail API id.
    /// </param>
    internal async Task<EmailListRow?> TryLocateAndSelectTaskEmailAsync(
        string? messageUniqueId,
        string? internetMessageId,
        int inboxMessageId,
        CancellationToken cancellationToken = default)
    {
        if (TrySelectByInboxCorrelation(messageUniqueId, internetMessageId, subject: null, fromAddress: null))
        {
            return PatchRowInboxMessageId(SelectedEmail?.Id ?? string.Empty, inboxMessageId) ?? SelectedEmail;
        }

        EmailSummary? summary = null;
        var gmailApiId = EmailMailboxQueryComposer.TryGetGmailApiMessageId(messageUniqueId);
        try
        {
            if (!string.IsNullOrWhiteSpace(gmailApiId))
            {
                summary = await EmailGateway.GetByIdAsync(gmailApiId, cancellationToken)
                    .ConfigureAwait(true);
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step(
                    "Email.Locate",
                    summary is null
                        ? $"GetById miss gmailApiId={gmailApiId} inbox={inboxMessageId}"
                        : $"GetById hit gmailApiId={gmailApiId} inbox={inboxMessageId}");
            }

            if (summary is null && !string.IsNullOrWhiteSpace(internetMessageId))
            {
                var rfc822Term = EmailMailboxQueryComposer.BuildRfc822MessageIdSearchTerm(internetMessageId);
                var page = await EmailGateway.GetMailboxPageAsync(
                    new EmailMailboxQuery
                    {
                        MailboxScope = EmailMailboxScope.AllMail,
                        FreeText = rfc822Term,
                        PageSize = 5,
                    },
                    pageToken: null,
                    cancellationToken).ConfigureAwait(true);
                summary = page.Items.FirstOrDefault();
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step(
                    "Email.Locate",
                    summary is null
                        ? $"rfc822msgid miss inbox={inboxMessageId} q={rfc822Term} pageItems={page.Items.Count}"
                        : $"rfc822msgid hit inbox={inboxMessageId} gmailId={summary.MessageId} q={rfc822Term}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.Locate",
                $"EXCEPTION inbox={inboxMessageId} {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        if (summary is null)
        {
            return null;
        }

        var (rows, _) = await EmailListRowMapper.MapSummariesAsync(
            [summary],
            ThreadLinkQuery,
            GetCurrentProject).ConfigureAwait(true);
        if (rows.Count == 0)
        {
            return null;
        }

        return _display.InjectAndSelectTaskRow(rows[0], inboxMessageId);
    }

    private EmailTaskSelectionTarget? _pendingTaskSelection;

    /// <summary>
    /// Task-driven opens race the (fire-and-forget) list reloads triggered by the project-context
    /// change: a reload finishing after the explicit selection resets it to the first row, and an
    /// explicit selection running before rows exist silently misses. Registering the target here
    /// lets every <c>ReplaceRows</c> re-apply it until the row shows up (observed in manual QA logs).
    /// </summary>
    internal EmailTaskSelectionTarget? PendingTaskSelection => _pendingTaskSelection;

    internal void SetPendingTaskSelection(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress,
        int? inboxMessageId = null) =>
        _pendingTaskSelection = new EmailTaskSelectionTarget(
            messageUniqueId, internetMessageId, subject, fromAddress, inboxMessageId);

    internal void ClearPendingTaskSelection() => _pendingTaskSelection = null;

    internal sealed record EmailTaskSelectionTarget(
        string? MessageUniqueId,
        string? InternetMessageId,
        string? Subject,
        string? FromAddress,
        int? InboxMessageId = null);

    public string? GetContextMenuDisabledReason(EmailListRow? row, EmailContextMenuAction action) =>
        action == EmailContextMenuAction.UploadToAccInbox
            ? DescribeUploadToAccDisabledReason(row)
            : _filing.GetContextMenuDisabledReason(row, action);

    internal Task ConnectGmailForTestsAsync() => _paging.ConnectAsync();
    internal Task DisconnectGmailForTestsAsync() => _paging.DisconnectGmailForTestsAsync();

    public async Task<EmailAccInboxStatus?> LoadAccStatusForRowAsync(EmailListRow row, CancellationToken cancellationToken = default)
    {
        if (_accHandler is null)
        {
            return null;
        }

        var (_, status) = await _accHandler.LoadStatusAsync(row, cancellationToken).ConfigureAwait(true);
        return status;
    }

    public async Task<(EmailListRow Row, EmailAccInboxStatus? Status)> TryPassiveAccIngestOnSelectionAsync(
        EmailListRow row,
        Func<bool> isStillSelected,
        CancellationToken cancellationToken = default)
    {
        if (_accHandler is null)
        {
            return (row, null);
        }

        return await _accHandler.TryPassiveIngestAsync(row, isStillSelected, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// N4.3: after mailbox File-to-project, ingest zero-attachment (or not-yet-uploaded) messages.
    /// Best-effort — filing already succeeded.
    /// </summary>
    internal async Task TryIngestAfterProjectFileAsync(EmailListRow row)
    {
        if (_accHandler is null || !IsConnected)
        {
            return;
        }

        if (!EmailAccIngestGates.IsEligibleForAccIngest(row.HasAttachments, row.IsFiledToProject))
        {
            return;
        }

        if (row.AccProcessingStatus is EmailAccProcessingStatus.UploadedToAcc
            or EmailAccProcessingStatus.MovedToProject
            or EmailAccProcessingStatus.LockedByOtherUser
            or EmailAccProcessingStatus.UploadInProgress)
        {
            return;
        }

        try
        {
            await _accHandler
                .TryPassiveIngestAsync(
                    row,
                    () => string.Equals(SelectedEmail?.Id, row.Id, StringComparison.Ordinal))
                .ConfigureAwait(true);
        }
        catch
        {
            // Filing already succeeded; ACC ingest failure is surfaced via row status.
        }
    }

    internal async Task SyncThreadMappingsFromPageAsync(IReadOnlyList<EmailSummary> summaries)
    {
        if (_threadMappingSync is null || summaries.Count == 0)
        {
            return;
        }

        try
        {
            await _threadMappingSync
                .SyncFiledThreadsFromSummariesAsync(summaries)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort background sync; mailbox rows are already visible.
        }
    }

    public EmailListRow? FindRowById(string rowId) => _display.FindRowById(rowId);

    public bool CanFileEmailToProject(EmailListRow? row) => _filing.CanFileEmailToProject(row);

    public bool CanAttemptFileEmailToProject(EmailListRow? row) => _filing.CanAttemptFileEmailToProject(row);

    public bool CanMarkAsFyi(EmailListRow? row) => _filing.CanMarkAsFyi(row);

    public Task<EmailListRow?> FileEmailToProjectAsync(EmailListRow? row) => _filing.FileEmailToProjectAsync(row);

    public Task<EmailListRow?> FileEmailToProjectAsync(EmailListRow? row, ProjectSummaryDto? targetProject) =>
        _filing.FileEmailToProjectAsync(row, targetProject);

    public EmailListRow? PatchRowAttachmentCount(string messageId, int attachmentCount)
    {
        var row = _display.FindRowById(messageId);
        if (row is null || row.AttachmentCount == attachmentCount)
        {
            return row;
        }

        var updated = row with { AttachmentCount = attachmentCount };
        PatchAccRow(updated);
        return updated;
    }

    /// <summary>
    /// After a workflow-starting action materializes an inbox row, bind it onto the visible Gmail row
    /// so subsequent context analysis / duplicate guards see the same identity.
    /// </summary>
    public EmailListRow? PatchRowInboxMessageId(string messageId, int inboxMessageId)
    {
        var row = _display.FindRowById(messageId);
        if (row is null || inboxMessageId <= 0 || row.InboxMessageId == inboxMessageId)
        {
            return row;
        }

        var updated = row with { InboxMessageId = inboxMessageId };
        PatchAccRow(updated);
        return updated;
    }

    /// <summary>
    /// Optimistic local unread-state patch (DEV-004). Adjusts mailbox unread total when the
    /// total is exact; page counters refresh from the visible rows.
    /// </summary>
    public EmailListRow? PatchRowIsUnread(string messageId, bool isUnread)
    {
        var row = _display.FindRowById(messageId);
        if (row is null || row.IsUnread == isUnread)
        {
            return row;
        }

        var updated = row with { IsUnread = isUnread };
        PatchAccRow(updated);

        if (MailboxUnreadIsExact)
        {
            MailboxUnreadTotal = isUnread
                ? MailboxUnreadTotal + 1
                : Math.Max(0, MailboxUnreadTotal - 1);
        }

        NotifyUnreadDisplayProperties();
        return updated;
    }

    private bool CanLoadEmails() => !IsBusy && IsConnected;

    private bool CanUploadToAccInbox(EmailListRow? row) =>
        _accHandler?.CanUpload(row, IsConnected) == true;

    private string? DescribeUploadToAccDisabledReason(EmailListRow? row) =>
        _accHandler?.DescribeUploadDisabledReason(row, IsConnected) ?? "העלאה ל-ACC אינה זמינה.";

    private async Task UploadToAccInboxAsync(EmailListRow? row)
    {
        if (row is null || _accHandler is null || !CanUploadToAccInbox(row))
        {
            return;
        }

        await _accHandler.UploadExplicitAsync(
            row,
            () => string.Equals(SelectedEmail?.Id, row.Id, StringComparison.Ordinal))
            .ConfigureAwait(true);
    }

    private void PatchAccRow(EmailListRow updated)
    {
        _display.ReplaceRowInDisplay(updated);
        _display.RebindSelectedEmail(updated);

        if (string.Equals(SelectedEmailId, updated.Id, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(updated.AccStatusDisplay))
        {
            AccStatusPatched?.Invoke(this, updated.AccStatusDisplay);
        }
    }

    private void OnAuthStateChanged(bool isAuthenticated)
    {
        UiThread.Run(() => _ = _paging.HandleAuthStateChangedOnUiThreadAsync(isAuthenticated));
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
        (ToggleUnreadOnlyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ApplyFiltersCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
}

public sealed record EmailProjectLinkFilterOption(EmailProjectLinkFilter Value, string DisplayName);
