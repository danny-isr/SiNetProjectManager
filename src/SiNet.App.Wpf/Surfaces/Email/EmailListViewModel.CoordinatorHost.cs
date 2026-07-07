using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Surfaces.Email.Internal;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

public sealed partial class EmailListViewModel
{
    internal void ClearAccSessionStateForTests() => _accHandler?.ClearSessionStateForTests();
    internal Task FileEmailToProjectForTestsAsync(EmailListRow? row) => _filing.FileEmailToProjectAsync(row);
    internal Task UnfileEmailForTestsAsync(EmailListRow? row) => _filing.UnfileEmailAsync(row);
    internal Task MarkAsPersonalForTestsAsync(EmailListRow? row) => _filing.SetEmailStatusAsync(row, EmailTriageStatus.Personal);
    internal Task MarkAsPendingForTestsAsync(EmailListRow? row) => _filing.SetEmailStatusAsync(row, EmailTriageStatus.Pending);
    internal void ApplyLocalEmailMutationForTests(EmailListRow row) => _display.ApplyLocalEmailMutation(row);
    internal void ClearEmailStateForTests() => _paging.ClearEmailState();
    internal Task LoadMailboxAndProjectForTestsAsync(bool resetStack) => _paging.LoadMailboxAndProjectAsync(resetStack);
    internal Task LoadMoreForLabelGroupForTestsAsync(EmailLabelGroupViewModel group) => _grouping.LoadMoreEmailsForGroupAsync(group);
    internal Task LoadAllForLabelGroupForTestsAsync(EmailLabelGroupViewModel group) => _grouping.LoadAllEmailsForGroupAsync(group);

    void IEmailListRowMutator.ReplaceRowInDisplay(EmailListRow updated) => _display.ReplaceRowInDisplay(updated);
    EmailListRow? IEmailListRowMutator.FindRowById(string rowId) => _display.FindRowById(rowId);
    void IEmailListRowMutator.ApplyLocalEmailMutation(EmailListRow updated) => _display.ApplyLocalEmailMutation(updated);
    void IEmailListRowMutator.RefreshRowBackgrounds() => _display.RefreshRowBackgrounds();
    bool IEmailListRowMutator.TrySelectByInboxCorrelation(string? a, string? b, string? c, string? d) =>
        _display.TrySelectByInboxCorrelation(a, b, c, d);
    IReadOnlyList<EmailListRow> IEmailListRowMutator.ApplyClientRowFilters(IReadOnlyList<EmailListRow> rows) =>
        _display.ApplyClientRowFilters(rows);
    void IEmailListRowMutator.ReplaceRows(IReadOnlyList<EmailListRow> rows, string? preserveSelectionId, bool skipDisplayRebuild) =>
        _display.ReplaceRows(rows, preserveSelectionId, skipDisplayRebuild);
    void IEmailListRowMutator.RemoveRowFromDisplay(EmailListRow row) => _display.RemoveRowFromDisplay(row);
    void IEmailListRowMutator.RebindSelectedEmail(EmailListRow updated) => _display.RebindSelectedEmail(updated);

    internal IEmailGateway EmailGateway => _emailGateway;
    internal IEmailThreadLinkQueryService? ThreadLinkQuery => _threadLinkQuery;
    internal IConnectorAuthService AuthService => _authService;
    internal IEmailFilingService? FilingService => _filingService;
    internal IEmailStatusService? StatusService => _statusService;
    internal Stack<string?> PageTokenStack { get; }
    internal string? NextPageToken => _nextPageToken;
    internal string? LastUsedPageToken => _lastUsedPageToken;
    internal string? LastUnreadQuerySignature => _lastUnreadQuerySignature;

    internal EmailListProjectContext? GetProjectContext() => _projectContext;
    internal ProjectSummaryDto? GetCurrentProject() => _currentProject?.CurrentProject;
    internal int? GetCurrentUserId() => _currentUser?.UserId;
    internal EmailLabelGroupViewModel? GetProjectGroup() => _projectGroup;

    internal void SetProjectGroup(EmailLabelGroupViewModel? group)
    {
        _projectGroup = group;
        OnPropertyChanged(nameof(ActiveProjectGroup));
        OnPropertyChanged(nameof(ShowProjectGroupAboveFlat));
    }

    internal void SetHasLabelGroups(bool value)
    {
        _hasLabelGroups = value;
        NotifyDisplayGroupPropertiesChanged();
    }

    internal void SetGroupByLabel(bool value) => GroupByLabel = value;
    internal void SetAttachmentsOnly(bool value) => AttachmentsOnly = value;
    internal void SetIsBusy(bool value) => IsBusy = value;
    internal void SetLoadState(EmailListLoadState value) => LoadState = value;
    internal void SetLoadWarning(string? value) => LoadWarning = value;
    internal void SetLoadError(string? value) => LoadError = value;
    internal void SetStatusMessage(string value) => StatusMessage = value;
    internal void SetCurrentPageNumber(int value) => CurrentPageNumber = value;
    internal void SetDisplayedCount(int value) => DisplayedCount = value;
    internal void SetHasNextPage(bool value) => HasNextPage = value;
    internal void SetNextPageToken(string? value) => _nextPageToken = value;
    internal void SetLastUsedPageToken(string? value) => _lastUsedPageToken = value;
    internal void SetMailboxUnreadTotal(int value) => MailboxUnreadTotal = value;
    internal void SetMailboxUnreadIsExact(bool value) => MailboxUnreadIsExact = value;
    internal void SetLastLoadedGmailQuery(string? value) => _lastLoadedGmailQuery = value;
    internal void SetLastUnreadQuerySignature(string? value) => _lastUnreadQuerySignature = value;
    internal void SetLastActionDiagnostics(string value) => _lastActionDiagnostics = value;
    internal void AddBusyRowId(string rowId) => _busyRowIds.Add(rowId);
    internal void RemoveBusyRowId(string rowId) => _busyRowIds.Remove(rowId);

    internal void NotifyUnreadDisplayProperties()
    {
        OnPropertyChanged(nameof(UnreadInCurrentPage));
        OnPropertyChanged(nameof(UnreadCountDisplay));
        OnPropertyChanged(nameof(ShowUnreadCount));
        OnPropertyChanged(nameof(MailboxDiagnostics));
    }

    internal void NotifyPageInfoChanged() => OnPropertyChanged(nameof(PageInfo));

    internal void NotifyHasPreviousPageChanged()
    {
        OnPropertyChanged(nameof(HasPreviousPage));
        (LoadPreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    internal void NotifyDisplayGroupPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasLabelGroups));
        OnPropertyChanged(nameof(ShowLabelGroups));
        OnPropertyChanged(nameof(ShowFlatEmailList));
        OnPropertyChanged(nameof(ShowProjectGroupAboveFlat));
        OnPropertyChanged(nameof(ActiveProjectGroup));
    }

    internal void NotifyAuthProperties()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectedAccountEmail));
        OnPropertyChanged(nameof(AccountStatusDisplay));
        OnPropertyChanged(nameof(ShowConnectButton));
        OnPropertyChanged(nameof(CanRefreshEmails));
        RaiseCommandStates();
        AccountStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseCommandStates()
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

    private sealed class CoordinatorGroupBridge
    {
        private EmailListGroupingCoordinator? _grouping;

        public void Bind(EmailListGroupingCoordinator grouping) => _grouping = grouping;

        public void RebuildDisplayGroups() => _grouping!.RebuildDisplayGroups();

        public void ApplyGrouping() => _grouping!.ApplyGrouping();
    }

    private sealed class CoordinatorPagingBridge
    {
        private EmailListPagingCoordinator? _paging;

        public void Bind(EmailListPagingCoordinator paging) => _paging = paging;

        public EmailMailboxQuery BuildQuery() => _paging!.BuildQuery();

        public EmailMailboxQuery BuildProjectGroupQuery(EmailMailboxQuery query, int pageSize) =>
            _paging!.BuildProjectGroupQuery(query, pageSize);
    }
}
