using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly Stack<string?> _pageTokenStack = new();

    private EmailListRow? _selectedEmail;
    private bool _isBusy;
    private string? _nextPageToken;
    private string? _lastUsedPageToken;
    private int _currentPageNumber = 1;
    private int _displayedCount;
    private bool _hasNextPage;
    private bool _groupByLabel;
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
    private IReadOnlyList<EmailListRow> _projectEmailRows = [];
    private int _projectVisibleCount;
    private bool _hasMoreProjectEmails;
    private ICollectionView? _emailsView;

    public EmailListViewModel()
        : this(new DesignEmailListGateway(), threadLinkQuery: null, new DesignAuthService())
    {
    }

    public EmailListViewModel(
        IEmailGateway emailGateway,
        IEmailThreadLinkQueryService? threadLinkQuery,
        IConnectorAuthService authService)
    {
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _threadLinkQuery = threadLinkQuery;
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        Emails = [];
        AvailableLabels = [];
        LabelGroups = [];
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
        LoadNextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(resetStack: false, useNextToken: true), () => CanLoadEmails() && HasNextPage && IsAllEmailsMode);
        LoadPreviousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CanLoadEmails() && HasPreviousPage && IsAllEmailsMode);
        RefreshPageCommand = new AsyncRelayCommand(() => ReloadForContextAsync(), CanLoadEmails);
        ApplyFiltersCommand = new AsyncRelayCommand(() => ReloadForContextAsync(), () => CanLoadEmails() && IsAllEmailsMode);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync, () => !IsBusy);
        ToggleGroupByLabelCommand = new RelayCommand(_ => ToggleGroupByLabel(), _ => !IsProjectMode);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectGmailAsync, () => IsConnected && !IsBusy);
        ShowMoreProjectEmailsCommand = new RelayCommand(_ => ShowMoreProjectEmails(), _ => HasMoreProjectEmails && !IsBusy);

        _authService.AuthStateChanged += OnAuthStateChanged;
    }

    public EmailListDisplayMode DisplayMode =>
        _projectContext is not null ? EmailListDisplayMode.ProjectEmails : EmailListDisplayMode.AllEmails;

    public bool IsProjectMode => DisplayMode == EmailListDisplayMode.ProjectEmails;

    public bool IsAllEmailsMode => !IsProjectMode;

    public bool ShowAllEmailsPaging => IsAllEmailsMode;

    public bool ShowProjectGroupChrome => IsProjectMode;

    public EmailListProjectContext? SelectedProjectContext => _projectContext;

    public string DisplayModeSummary => IsProjectMode
        ? "מצב: מיילים של הפרויקט"
        : "מצב: כל המיילים";

    public string ProjectGroupHeader => _projectContext?.GroupHeaderDisplay ?? string.Empty;

    public bool HasMoreProjectEmails
    {
        get => _hasMoreProjectEmails;
        private set
        {
            if (SetField(ref _hasMoreProjectEmails, value))
            {
                (ShowMoreProjectEmailsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public int ProjectEmailTotalCount => _projectEmailRows.Count;

    public ObservableCollection<EmailListRow> Emails { get; }

    public ObservableCollection<GmailLabelInfo> AvailableLabels { get; }

    public ObservableCollection<EmailLabelGroupViewModel> LabelGroups { get; }

    public bool ShowLabelGroups => GroupByLabel && IsAllEmailsMode;

    public bool ShowFlatEmailList => !ShowLabelGroups;

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
        private set => SetField(ref _groupByLabel, value);
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
            if (IsProjectMode)
            {
                if (ProjectEmailTotalCount == 0)
                {
                    return DisplayModeSummary;
                }

                return $"{DisplayModeSummary} · מציג {DisplayedCount} מתוך {ProjectEmailTotalCount}";
            }

            if (DisplayedCount == 0)
            {
                return $"{DisplayModeSummary} · עמוד {CurrentPageNumber}";
            }

            var start = (CurrentPageNumber - 1) * PageSize + 1;
            var end = start + DisplayedCount - 1;
            return $"{DisplayModeSummary} · מציג {start}–{end} · עמוד {CurrentPageNumber}";
        }
    }

    public ICommand LoadFirstPageCommand { get; }
    public ICommand LoadNextPageCommand { get; }
    public ICommand LoadPreviousPageCommand { get; }
    public ICommand RefreshPageCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleGroupByLabelCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ShowMoreProjectEmailsCommand { get; }

    /// <summary>Called by the Email Workbench host when the shared project context changes.</summary>
    public async Task ApplyProjectContextAsync(EmailListProjectContext? context)
    {
        if (Equals(_projectContext, context))
        {
            return;
        }

        _projectContext = context;
        _projectEmailRows = [];
        _projectVisibleCount = 0;
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

    private Task ReloadForContextAsync() =>
        IsProjectMode ? LoadProjectEmailsAsync(resetVisibleCount: true) : LoadPageAsync(resetStack: true);

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
            await LoadPageAsync(resetStack: true).ConfigureAwait(true);

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
        GroupByLabel = false;
        ClearLabelGroups();
        MailboxUnreadTotal = 0;
        MailboxUnreadIsExact = true;
        _lastLoadedGmailQuery = null;
        _lastUnreadQuerySignature = null;

        AvailableLabels.Clear();

        _projectEmailRows = [];
        _projectVisibleCount = 0;
        HasMoreProjectEmails = false;

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
        if (IsProjectMode)
        {
            StatusMessage = "סינון מיילים בתוך קבוצת פרויקט — מושהה. נקה את בחירת הפרויקט לחזרה לכל המיילים.";
            return;
        }

        SearchText = string.Empty;
        AddressFilter = string.Empty;
        SubjectFilter = string.Empty;
        SelectedLabel = null;
        SelectedMailboxScope = EmailMailboxScope.Inbox;
        SelectedProjectLinkFilter = EmailProjectLinkFilter.All;
        ClearLabelGroups();
        await LoadPageAsync(resetStack: true).ConfigureAwait(true);
    }

    private void ToggleGroupByLabel()
    {
        GroupByLabel = !GroupByLabel;
        if (GroupByLabel)
        {
            RebuildLabelGroupsFromCurrentEmails();
        }
        else
        {
            ClearLabelGroups();
        }

        ApplyGrouping();
        OnPropertyChanged(nameof(ShowLabelGroups));
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

    private void ClearLabelGroups()
    {
        LabelGroups.Clear();
        OnPropertyChanged(nameof(ShowLabelGroups));
    }

    private void RebuildLabelGroupsFromCurrentEmails()
    {
        if (!GroupByLabel || !IsAllEmailsMode)
        {
            ClearLabelGroups();
            return;
        }

        var expandedByLabelId = LabelGroups.ToDictionary(static g => g.LabelId, static g => g.IsExpanded, StringComparer.Ordinal);
        ClearLabelGroups();

        var labelIdByName = AvailableLabels
            .Where(static label => !string.IsNullOrWhiteSpace(label.Name) && !string.IsNullOrWhiteSpace(label.Id))
            .GroupBy(static label => label.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var groupsById = new Dictionary<string, EmailLabelGroupViewModel>(StringComparer.Ordinal);

        foreach (var row in Emails)
        {
            var labelNames = row.LabelChipNames is { Count: > 0 }
                ? row.LabelChipNames
                : [row.PrimaryLabel ?? "ללא label"];

            foreach (var labelName in labelNames)
            {
                if (string.IsNullOrWhiteSpace(labelName)
                    || !labelIdByName.TryGetValue(labelName, out var labelId)
                    || string.IsNullOrWhiteSpace(labelId))
                {
                    continue;
                }

                if (!groupsById.TryGetValue(labelId, out var group))
                {
                    group = CreateLabelGroup(labelId, labelName);
                    if (expandedByLabelId.TryGetValue(labelId, out var isExpanded))
                    {
                        group.IsExpanded = isExpanded;
                    }

                    groupsById[labelId] = group;
                    LabelGroups.Add(group);
                }

                group.TryAddEmail(row);
            }
        }

        foreach (var group in LabelGroups)
        {
            group.ResetPagingState();
        }

        OnPropertyChanged(nameof(ShowLabelGroups));
    }

    private EmailLabelGroupViewModel CreateLabelGroup(string labelId, string labelDisplayName) =>
        new(
            labelId,
            labelDisplayName,
            LoadMoreEmailsForGroupAsync,
            LoadAllEmailsForGroupAsync);

    private EmailMailboxQuery BuildLabelGroupQuery(EmailLabelGroupViewModel group) =>
        BuildQuery() with
        {
            MailboxScope = EmailMailboxScope.Label,
            LabelId = group.LabelId,
            LabelName = group.LabelDisplayName,
        };

    private async Task LoadMoreEmailsForGroupAsync(EmailLabelGroupViewModel group)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(group.LabelId))
        {
            return;
        }

        group.IsExpanded = true;
        group.IsLoading = true;
        group.ErrorMessage = null;
        StatusMessage = $"טוען מיילים מהלייבל {group.LabelDisplayName}...";

        try
        {
            var query = BuildLabelGroupQuery(group);
            var page = await _emailGateway
                .GetMailboxPageAsync(query, group.NextPageToken)
                .ConfigureAwait(true);

            var (rows, _) = await MapSummariesAsync(page.Items).ConfigureAwait(true);
            rows = ApplyClientLinkFilter(rows);
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

        group.NotifyHeaderChanged();
    }

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
        string? explicitToken = null)
    {
        if (IsProjectMode)
        {
            await LoadProjectEmailsAsync(resetVisibleCount: resetStack).ConfigureAwait(true);
            return;
        }

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

            rows = ApplyClientLinkFilter(rows);

            ReplaceRows(rows, previousSelectionId);
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
            PageSize = PageSize,
        };
    }

    private bool ShouldRefreshUnreadTotal(EmailMailboxQuery query, bool resetStack) =>
        resetStack || !string.Equals(_lastUnreadQuerySignature, BuildUnreadQuerySignature(query), StringComparison.Ordinal);

    private static string BuildUnreadQuerySignature(EmailMailboxQuery query) =>
        $"{query.MailboxScope}|{query.LabelName}|{query.Subject}|{query.FromOrTo}|{query.FreeText}|{query.ProjectLinkFilter}";

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

    private async Task LoadProjectEmailsAsync(bool resetVisibleCount)
    {
        LoadError = null;
        LoadWarning = null;

        if (_projectContext is null)
        {
            await LoadPageAsync(resetStack: true).ConfigureAwait(true);
            return;
        }

        if (!IsConnected)
        {
            LoadState = EmailListLoadState.Error;
            LoadError = "Gmail לא מחובר. התחבר ונסה שוב.";
            StatusMessage = LoadError;
            return;
        }

        var previousSelectionId = SelectedEmail?.Id;
        IsBusy = true;
        LoadState = EmailListLoadState.Loading;
        StatusMessage = "טוען מיילים של הפרויקט…";

        try
        {
            if (resetVisibleCount || _projectEmailRows.Count == 0)
            {
                var summaries = await FetchProjectSummariesAsync(_projectContext).ConfigureAwait(true);
                var (rows, enrichmentWarning) = await MapSummariesAsync(summaries).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(enrichmentWarning))
                {
                    LoadWarning = enrichmentWarning;
                }

                _projectEmailRows = rows;
                _projectVisibleCount = Math.Min(ProjectEmailChunkSize, rows.Count);
            }

            var visibleRows = _projectEmailRows.Take(_projectVisibleCount).ToList();
            HasMoreProjectEmails = _projectVisibleCount < _projectEmailRows.Count;
            ReplaceRows(visibleRows, previousSelectionId);
            DisplayedCount = visibleRows.Count;

            if (_projectEmailRows.Count == 0)
            {
                LoadState = EmailListLoadState.NoResults;
                LoadError = null;
                StatusMessage = "לא נמצאו מיילים משויכים לפרויקט הנבחר.";
            }
            else
            {
                LoadState = string.IsNullOrWhiteSpace(LoadWarning)
                    ? EmailListLoadState.Loaded
                    : EmailListLoadState.PartialFailure;
                StatusMessage = $"נטענו {visibleRows.Count} מתוך {_projectEmailRows.Count} מיילים של הפרויקט.";
            }
        }
        catch (Exception ex)
        {
            LoadState = EmailListLoadState.Error;
            LoadError = $"טעינת מיילי הפרויקט נכשלה: {ex.Message}";
            StatusMessage = LoadError;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowMoreProjectEmails()
    {
        if (!IsProjectMode || _projectEmailRows.Count == 0)
        {
            return;
        }

        _projectVisibleCount = Math.Min(_projectVisibleCount + ProjectEmailChunkSize, _projectEmailRows.Count);
        var visibleRows = _projectEmailRows.Take(_projectVisibleCount).ToList();
        HasMoreProjectEmails = _projectVisibleCount < _projectEmailRows.Count;
        ReplaceRows(visibleRows, SelectedEmail?.Id);
        DisplayedCount = visibleRows.Count;
        StatusMessage = $"מציג {visibleRows.Count} מתוך {_projectEmailRows.Count} מיילים של הפרויקט.";
    }

    private async Task<IReadOnlyList<EmailSummary>> FetchProjectSummariesAsync(EmailListProjectContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ProjectLabelName))
        {
            return await _emailGateway
                .GetProjectEmailsByProjectLabelAsync(context.ProjectLabelName.Trim())
                .ConfigureAwait(true);
        }

        if (!string.IsNullOrWhiteSpace(context.LocationName)
            && !string.IsNullOrWhiteSpace(context.ProjectName))
        {
            return await _emailGateway
                .GetProjectEmailsAsync(context.LocationName.Trim(), context.ProjectName.Trim())
                .ConfigureAwait(true);
        }

        throw new InvalidOperationException("חסר Project label לטעינת מיילי פרויקט.");
    }

    private void NotifyDisplayModeProperties()
    {
        OnPropertyChanged(nameof(DisplayMode));
        OnPropertyChanged(nameof(IsProjectMode));
        OnPropertyChanged(nameof(IsAllEmailsMode));
        OnPropertyChanged(nameof(ShowAllEmailsPaging));
        OnPropertyChanged(nameof(ShowProjectGroupChrome));
        OnPropertyChanged(nameof(SelectedProjectContext));
        OnPropertyChanged(nameof(DisplayModeSummary));
        OnPropertyChanged(nameof(ProjectGroupHeader));
        OnPropertyChanged(nameof(PageInfo));
        (ToggleGroupByLabelCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

    private static EmailListRow ToEmailListRow(
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
        var labelsDisplay = labelChipNames.Count > 0
            ? string.Join(", ", labelChipNames)
            : string.Empty;

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
            AttachmentCount: summary.HasAttachments ? 1 : 0,
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
            LabelChipNames: labelChipNames);
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

        return labelNames.Any(static label =>
            label.Contains("פרויקטים_משרד", StringComparison.OrdinalIgnoreCase)
            && label.Count(static ch => ch == '/') >= 2);
    }

    private IReadOnlyList<EmailListRow> ApplyClientLinkFilter(IReadOnlyList<EmailListRow> rows)
    {
        return SelectedProjectLinkFilter switch
        {
            EmailProjectLinkFilter.Linked => rows.Where(static row => row.IsLinked).ToList(),
            EmailProjectLinkFilter.Unlinked => rows.Where(static row => !row.IsLinked).ToList(),
            _ => rows,
        };
    }

    private void ReplaceRows(IReadOnlyList<EmailListRow> rows, string? preserveSelectionId = null)
    {
        Emails.Clear();
        foreach (var row in rows)
        {
            Emails.Add(row);
        }

        ApplyGrouping();
        if (GroupByLabel)
        {
            RebuildLabelGroupsFromCurrentEmails();
        }

        SelectedEmail = preserveSelectionId is null
            ? Emails.FirstOrDefault()
            : Emails.FirstOrDefault(row => string.Equals(row.Id, preserveSelectionId, StringComparison.Ordinal))
              ?? Emails.FirstOrDefault();
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
        (ShowMoreProjectEmailsCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                    row.HasAttachments,
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
