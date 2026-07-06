using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// General mailbox list: paged Gmail read, filters, label grouping, and read-only project-link display.
/// </summary>
public sealed class EmailListViewModel : ObservableObject
{
    public const int PageSize = EmailMailboxQuery.DefaultPageSize;

    private readonly IEmailGateway _emailGateway;
    private readonly IEmailThreadLinkQueryService? _threadLinkQuery;
    private readonly Func<bool> _isConnected;
    private readonly Stack<string?> _pageTokenStack = new();

    private EmailListRow? _selectedEmail;
    private bool _isBusy;
    private string? _nextPageToken;
    private string? _lastUsedPageToken;
    private int _currentPageNumber = 1;
    private int _displayedCount;
    private bool _hasNextPage;
    private bool _groupByLabel;
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

    public EmailListViewModel()
        : this(new DesignEmailListGateway(), threadLinkQuery: null, () => true)
    {
    }

    public EmailListViewModel(
        IEmailGateway emailGateway,
        IEmailThreadLinkQueryService? threadLinkQuery,
        Func<bool> isConnected)
    {
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _threadLinkQuery = threadLinkQuery;
        _isConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));

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
    }

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
        set => SetField(ref _projectLinkFilter, value);
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
        private set => SetField(ref _displayedCount, value);
    }

    public int CurrentPageNumber
    {
        get => _currentPageNumber;
        private set => SetField(ref _currentPageNumber, value);
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

    public string PageInfo => $"עמוד {CurrentPageNumber} · {DisplayedCount} מיילים";

    public ICommand LoadFirstPageCommand { get; }
    public ICommand LoadNextPageCommand { get; }
    public ICommand LoadPreviousPageCommand { get; }
    public ICommand RefreshPageCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleGroupByLabelCommand { get; }

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
        if (!_isConnected())
        {
            return;
        }

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
            // Labels are optional for first load.
        }
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

    private bool CanLoadEmails() => !IsBusy && _isConnected();

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
        if (!_isConnected())
        {
            StatusMessage = "Gmail לא מחובר. התחבר ונסה שוב.";
            ReplaceRows([]);
            return;
        }

        if (FilterByCurrentProject && string.IsNullOrWhiteSpace(_optionalProjectLabel))
        {
            StatusMessage = "סינון פרויקט פעיל — בחר פרויקט עם Project label תקין.";
            ReplaceRows([]);
            return;
        }

        IsBusy = true;
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

            var rows = await MapSummariesAsync(page.Items).ConfigureAwait(true);
            rows = ApplyClientLinkFilter(rows);

            ReplaceRows(rows);
            DisplayedCount = rows.Count;
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(HasPreviousPage));
            (LoadPreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

            StatusMessage = rows.Count == 0
                ? "לא נמצאו מיילים לפי הסינון הנוכחי."
                : $"נטענו {rows.Count} מיילים (עמוד {CurrentPageNumber}).";
        }
        catch (Exception ex)
        {
            ReplaceRows([]);
            StatusMessage = $"טעינת המיילים נכשלה: {ex.Message}";
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

    private async Task<IReadOnlyList<EmailListRow>> MapSummariesAsync(IReadOnlyList<EmailSummary> summaries)
    {
        if (summaries.Count == 0)
        {
            return [];
        }

        IReadOnlyDictionary<string, EmailProjectLinkInfo> linkStates = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        if (_threadLinkQuery is not null)
        {
            var ids = summaries
                .Select(static summary => summary.InternetMessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .ToList();

            if (ids.Count > 0)
            {
                linkStates = await _threadLinkQuery
                    .GetLinkStatesByInternetMessageIdsAsync(ids)
                    .ConfigureAwait(true);
            }
        }

        var rows = new List<EmailListRow>(summaries.Count);
        foreach (var summary in summaries)
        {
            rows.Add(ToEmailListRow(summary, linkStates));
        }

        return rows;
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

        var labelsDisplay = summary.LabelNames is { Count: > 0 }
            ? string.Join(", ", summary.LabelNames)
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
            IsUnread: false,
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
            ProjectDisplay: projectDisplay);
    }

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

    private void ReplaceRows(IReadOnlyList<EmailListRow> rows)
    {
        Emails.Clear();
        foreach (var row in rows)
        {
            Emails.Add(row);
        }

        ApplyGrouping();
        SelectedEmail = Emails.FirstOrDefault();
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
                    PrimaryLabel: row.GroupName))
                .ToList();

            return Task.FromResult(new EmailMailboxPage(items, query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GmailLabelInfo>>([
                new GmailLabelInfo("INBOX", "INBOX"),
                new GmailLabelInfo("lbl1", "פרויקטים_משרד/תל אביב/1042 — דוגמה"),
            ]);
    }
}

public sealed record EmailProjectLinkFilterOption(EmailProjectLinkFilter Value, string DisplayName);
