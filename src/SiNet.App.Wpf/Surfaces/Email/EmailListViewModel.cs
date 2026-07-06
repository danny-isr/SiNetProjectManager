using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
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
    private EmailProjectLinkFilter _projectLinkFilter = EmailProjectLinkFilter.All;
    private bool _filterByCurrentProject;
    private string? _optionalProjectLabel;
    private int? _optionalProjectId;
    private ICollectionView? _emailsView;
    private ProjectSelectorViewModel? _projectSelector;

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
        ProjectLinkFilterOptions =
        [
            new EmailProjectLinkFilterOption(EmailProjectLinkFilter.All, "הכול"),
            new EmailProjectLinkFilterOption(EmailProjectLinkFilter.Linked, "משויכים"),
            new EmailProjectLinkFilterOption(EmailProjectLinkFilter.Unlinked, "לא משויכים"),
        ];

        _emailsView = CollectionViewSource.GetDefaultView(Emails);
        _emailsView.SortDescriptions.Add(new SortDescription(nameof(EmailListRow.ReceivedOn), ListSortDirection.Descending));

        LoadFirstPageCommand = new AsyncRelayCommand(() => LoadPageAsync(resetStack: true), CanLoadEmails);
        LoadNextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(resetStack: false, useNextToken: true), () => CanLoadEmails() && HasNextPage);
        LoadPreviousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CanLoadEmails() && HasPreviousPage);
        RefreshPageCommand = new AsyncRelayCommand(() => LoadPageAsync(resetStack: true), CanLoadEmails);
        ApplyFiltersCommand = new AsyncRelayCommand(() => LoadPageAsync(resetStack: true), CanLoadEmails);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync, () => !IsBusy);
        ToggleGroupByLabelCommand = new RelayCommand(_ => ToggleGroupByLabel());
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected && !IsBusy);

        _authService.AuthStateChanged += OnAuthStateChanged;
    }

    /// <summary>Optional project selector hosted in the filter bar by the parent workbench.</summary>
    public ProjectSelectorViewModel? ProjectSelector
    {
        get => _projectSelector;
        set
        {
            if (SetField(ref _projectSelector, value))
            {
                OnPropertyChanged(nameof(ShowProjectSelector));
            }
        }
    }

    public bool ShowProjectSelector => ProjectSelector is not null;

    public bool ShowConnectButton => !IsConnected;

    public ObservableCollection<EmailListRow> Emails { get; }

    public ObservableCollection<GmailLabelInfo> AvailableLabels { get; }

    public ObservableCollection<EmailProjectLinkFilterOption> ProjectLinkFilterOptions { get; }

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

    public int UnreadEmailCount => Emails.Count(static row => row.IsUnread);

    public bool ShowUnreadCount => UnreadEmailCount > 0;

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
        set => SetField(ref _selectedLabel, value);
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

    public bool FilterByCurrentProject
    {
        get => _filterByCurrentProject;
        set => SetField(ref _filterByCurrentProject, value);
    }

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
            if (DisplayedCount == 0)
            {
                return $"עמוד {CurrentPageNumber}";
            }

            var start = (CurrentPageNumber - 1) * PageSize + 1;
            var end = start + DisplayedCount - 1;
            return $"מציג {start}–{end} · עמוד {CurrentPageNumber}";
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

    public void SetOptionalProjectFilter(int? projectId, string? projectLabel)
    {
        _optionalProjectId = projectId;
        _optionalProjectLabel = projectLabel;
    }

    public Task RefreshPageAsync() => LoadPageAsync(resetStack: true);

    public Task ApplyFiltersAsync() => LoadPageAsync(resetStack: true);

    public Task ClearFiltersAndReloadAsync() => ClearFiltersAsync();

    public Task LoadNextPageAsync() => LoadPageAsync(resetStack: false, useNextToken: true);

    public Task LoadPreviousPagePublicAsync() => LoadPreviousPageAsync();

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
        StatusMessage = "מתחבר ל-Google…";
        try
        {
            var connected = await _authService.LoginAsync().ConfigureAwait(true);
            if (connected)
            {
                await RefreshAccountProfileAsync().ConfigureAwait(true);
                await LoadLabelsAsync().ConfigureAwait(true);
                StatusMessage = "החיבור ל-Google הושלם. ניתן לרענן כדי לטעון מיילים.";
            }
            else
            {
                StatusMessage = "ההתחברות ל-Google לא הושלמה.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"התחברות ל-Google נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyAuthProperties();
        }
    }

    private void Disconnect()
    {
        _authService.Logout();
        LoadWarning = null;
        LoadError = null;
        LoadState = EmailListLoadState.Idle;
        ReplaceRows([]);
        StatusMessage = "התנתקת מ-Gmail.";
        NotifyAuthProperties();
    }

    private void OnAuthStateChanged(bool isAuthenticated)
    {
        NotifyAuthProperties();
        if (!isAuthenticated)
        {
            ReplaceRows([]);
            LoadState = EmailListLoadState.Idle;
            StatusMessage = "Gmail לא מחובר.";
        }
    }

    private void NotifyAuthProperties()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectedAccountEmail));
        OnPropertyChanged(nameof(AccountStatusDisplay));
        OnPropertyChanged(nameof(ShowConnectButton));
        RaiseCommandStates();
    }

    private async Task RefreshAccountProfileAsync()
    {
        await _authService.RefreshAccountProfileAsync().ConfigureAwait(true);
        NotifyAuthProperties();
    }

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
        SelectedProjectLinkFilter = EmailProjectLinkFilter.All;
        FilterByCurrentProject = false;
        await LoadPageAsync(resetStack: true).ConfigureAwait(true);
    }

    private void ToggleGroupByLabel()
    {
        GroupByLabel = !GroupByLabel;
        ApplyGrouping();
    }

    private void ApplyGrouping()
    {
        if (_emailsView is null)
        {
            return;
        }

        _emailsView.GroupDescriptions.Clear();
        if (GroupByLabel)
        {
            _emailsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EmailListRow.PrimaryLabel)));
        }

        _emailsView.Refresh();
    }

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

        if (FilterByCurrentProject && string.IsNullOrWhiteSpace(_optionalProjectLabel))
        {
            LoadState = EmailListLoadState.Error;
            LoadError = "סינון פרויקט פעיל — בחר פרויקט עם Project label תקין.";
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
            var page = await _emailGateway
                .GetMailboxPageAsync(query, requestToken)
                .ConfigureAwait(true);

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

    private EmailMailboxQuery BuildQuery() => new()
    {
        FreeText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        Subject = string.IsNullOrWhiteSpace(SubjectFilter) ? null : SubjectFilter.Trim(),
        FromOrTo = string.IsNullOrWhiteSpace(AddressFilter) ? null : AddressFilter.Trim(),
        LabelName = SelectedLabel,
        ProjectLinkFilter = SelectedProjectLinkFilter,
        OptionalProjectId = FilterByCurrentProject ? _optionalProjectId : null,
        OptionalProjectLabel = FilterByCurrentProject ? _optionalProjectLabel : null,
        PageSize = PageSize,
    };

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
        SelectedEmail = preserveSelectionId is null
            ? Emails.FirstOrDefault()
            : Emails.FirstOrDefault(row => string.Equals(row.Id, preserveSelectionId, StringComparison.Ordinal))
              ?? Emails.FirstOrDefault();
        OnPropertyChanged(nameof(UnreadEmailCount));
        OnPropertyChanged(nameof(ShowUnreadCount));
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
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
    }

    private sealed class DesignAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; } = true;

        public string? ConnectedAccountEmail { get; private set; } = "design@example.com";

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

public sealed record EmailProjectLinkFilterOption(EmailProjectLinkFilter Value, string DisplayName);
