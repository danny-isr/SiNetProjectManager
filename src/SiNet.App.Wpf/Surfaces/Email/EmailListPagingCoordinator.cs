using System.Diagnostics;
using System.Windows;
using SiNet.App.Wpf.Infrastructure;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

internal sealed class EmailListPagingCoordinator
{
    private readonly EmailListViewModel _owner;
    private readonly EmailListRowDisplayCoordinator _display;
    private readonly EmailListGroupingCoordinator _grouping;

    public EmailListPagingCoordinator(
        EmailListViewModel owner,
        EmailListRowDisplayCoordinator display,
        EmailListGroupingCoordinator grouping)
    {
        _owner = owner;
        _display = display;
        _grouping = grouping;
    }

    public async Task ReloadForContextAsync()
    {
        if (!_owner.IsConnected)
        {
            return;
        }

        await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);
    }

    public async Task LoadMailboxAndProjectAsync(bool resetStack)
    {
        await LoadPageAsync(resetStack, skipDisplayRebuild: true).ConfigureAwait(true);
        await _grouping.EnsureProjectGroupAsync(resetPaging: resetStack).ConfigureAwait(true);
        _grouping.RebuildDisplayGroups();
    }

    public async Task LoadPreviousPageAsync()
    {
        if (_owner.PageTokenStack.Count == 0)
        {
            return;
        }

        var previousToken = _owner.PageTokenStack.Pop();
        _owner.SetCurrentPageNumber(Math.Max(1, _owner.CurrentPageNumber - 1));
        await LoadPageAsync(resetStack: false, explicitToken: previousToken).ConfigureAwait(true);
    }

    public async Task LoadPageAsync(
        bool resetStack,
        bool useNextToken = false,
        string? explicitToken = null,
        bool skipDisplayRebuild = false)
    {
        _owner.SetLoadError(null);
        _owner.SetLoadWarning(null);

        if (!_owner.IsConnected)
        {
            _owner.SetLoadState(EmailListLoadState.Error);
            _owner.SetLoadError("Gmail לא מחובר. התחבר ונסה שוב.");
            _owner.SetStatusMessage(_owner.LoadError!);
            if (resetStack)
            {
                _display.ReplaceRows([]);
            }

            return;
        }

        var previousSelectionId = _owner.SelectedEmail?.Id;
        _owner.SetIsBusy(true);
        _owner.SetLoadState(EmailListLoadState.Loading);
        _owner.SetStatusMessage(resetStack ? "טוען מיילים…" : "טוען עמוד…");

        try
        {
            string? requestToken;
            if (resetStack)
            {
                _owner.PageTokenStack.Clear();
                _owner.SetCurrentPageNumber(1);
                requestToken = null;
            }
            else if (useNextToken)
            {
                _owner.PageTokenStack.Push(_owner.LastUsedPageToken);
                _owner.SetCurrentPageNumber(_owner.CurrentPageNumber + 1);
                requestToken = _owner.NextPageToken;
            }
            else
            {
                requestToken = explicitToken;
            }

            _owner.SetLastUsedPageToken(requestToken);

            var query = BuildQuery();
            _owner.SetLastLoadedGmailQuery(EmailMailboxQueryComposer.BuildSearchQuery(query));
            var refreshUnreadTotal = ShouldRefreshUnreadTotal(query, resetStack);

            var pageTask = _owner.EmailGateway.GetMailboxPageAsync(query, requestToken);
            var unreadTask = refreshUnreadTotal
                ? _owner.EmailGateway.GetMailboxUnreadCountAsync(query)
                : Task.FromResult(new EmailMailboxUnreadCount(_owner.MailboxUnreadTotal, _owner.MailboxUnreadIsExact));

            await Task.WhenAll(pageTask, unreadTask).ConfigureAwait(true);
            var page = await pageTask.ConfigureAwait(true);
            var unreadCount = await unreadTask.ConfigureAwait(true);

            if (refreshUnreadTotal)
            {
                ApplyMailboxUnreadCount(unreadCount);
            }

            _owner.SetNextPageToken(page.NextPageToken);
            _owner.SetHasNextPage(page.HasNextPage);

            var (rows, enrichmentWarning) = await EmailListRowMapper.MapSummariesAsync(
                page.Items,
                _owner.ThreadLinkQuery,
                () => _owner.GetCurrentProject()).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(enrichmentWarning))
            {
                _owner.SetLoadWarning(enrichmentWarning);
            }

            rows = _display.ApplyClientRowFilters(rows);

            _display.ReplaceRows(rows, previousSelectionId, skipDisplayRebuild);
            _owner.SetDisplayedCount(rows.Count);
            _owner.NotifyPageInfoChanged();
            _owner.NotifyHasPreviousPageChanged();

            if (rows.Count == 0)
            {
                _owner.SetLoadState(EmailListLoadState.NoResults);
                _owner.SetStatusMessage("לא נמצאו מיילים לפי הסינון הנוכחי.");
            }
            else
            {
                _owner.SetLoadState(string.IsNullOrWhiteSpace(_owner.LoadWarning)
                    ? EmailListLoadState.Loaded
                    : EmailListLoadState.PartialFailure);
                _owner.SetStatusMessage($"נטענו {rows.Count} מיילים (עמוד {_owner.CurrentPageNumber}).");
                if (_owner.ShowSparsePageWarning)
                {
                    _owner.SetStatusMessage(_owner.StatusMessage + " סינון שיוך פעיל — ייתכן פחות מ-50 תוצאות בדף.");
                }
            }
        }
        catch (Exception ex)
        {
            _owner.SetLoadState(EmailListLoadState.Error);
            _owner.SetLoadError($"טעינת המיילים נכשלה: {ex.Message}");
            _owner.SetStatusMessage(_owner.LoadError!);
            if (resetStack && _owner.Emails.Count == 0)
            {
                _display.ReplaceRows([]);
            }
        }
        finally
        {
            _owner.SetIsBusy(false);
        }
    }

    public async Task ConnectAsync()
    {
        _owner.SetIsBusy(true);
        _owner.SetStatusMessage("מתחבר ל-Gmail...");
        try
        {
            var connected = await _owner.AuthService.LoginAsync(
                new ConnectorLoginOptions(SkipSilentRestore: true, PromptAccountSelection: true))
                .ConfigureAwait(true);
            if (!connected && await _owner.AuthService.TryRestoreSessionAsync().ConfigureAwait(true))
            {
                connected = true;
            }

            if (!connected)
            {
                _owner.SetStatusMessage("התחברות ל-Gmail בוטלה.");
                return;
            }

            if (_owner.IngestSessionEnsurer is not null)
            {
                var legacyReady = await _owner.IngestSessionEnsurer
                    .EnsureAuthenticatedForAccIngestAsync()
                    .ConfigureAwait(true);
                if (!legacyReady)
                {
                    _owner.SetStatusMessage("Gmail מחובר לרשימה — העלאה ל-ACC תדרוש התחברות נוספת.");
                }
            }

            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
            ClearEmailState();
            await LoadLabelsAsync().ConfigureAwait(true);
            _owner.SetStatusMessage("טוען מיילים...");
            await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);

            var email = _owner.ConnectedAccountEmail ?? "Gmail";
            _owner.SetStatusMessage($"מחובר כ־{email}. נטענו {_owner.DisplayedCount} מיילים.");
        }
        catch (Exception ex)
        {
            _owner.SetStatusMessage($"התחברות ל-Gmail נכשלה: {ex.Message}");
            _owner.SetLoadError(_owner.StatusMessage);
        }
        finally
        {
            _owner.SetIsBusy(false);
            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        }
    }

    public async Task DisconnectGmailAsync()
    {
        if (!_owner.IsConnected)
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

        await DisconnectGmailCoreAsync().ConfigureAwait(true);
    }

    public async Task DisconnectGmailForTestsAsync()
    {
        if (!_owner.IsConnected)
        {
            return;
        }

        await DisconnectGmailCoreAsync().ConfigureAwait(true);
    }

    public async Task HandleAuthStateChangedOnUiThreadAsync(bool isAuthenticated)
    {
        await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        if (!isAuthenticated)
        {
            ClearEmailState();
            if (!_owner.IsBusy)
            {
                _owner.SetStatusMessage("לא מחובר ל-Gmail.");
            }
        }
    }

    public async Task LoadLabelsAsync()
    {
        try
        {
            var labels = await _owner.EmailGateway.GetMailboxLabelsAsync().ConfigureAwait(true);
            _owner.AvailableLabels.Clear();
            foreach (var label in labels)
            {
                _owner.AvailableLabels.Add(label);
            }
        }
        catch
        {
            _owner.SetLoadWarning("רשימת labels לא נטענה.");
        }
    }

    public async Task ClearFiltersAsync()
    {
        _owner.SearchText = string.Empty;
        _owner.AddressFilter = string.Empty;
        _owner.SubjectFilter = string.Empty;
        _owner.SelectedLabel = null;
        _owner.SelectedMailboxScope = EmailMailboxScope.Inbox;
        _owner.SelectedProjectLinkFilter = EmailProjectLinkFilter.All;
        _owner.SetAttachmentsOnly(false);
        _grouping.ClearDisplayGroups();
        await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);
    }

    public async Task ToggleAttachmentsOnlyAsync()
    {
        _owner.SetAttachmentsOnly(!_owner.AttachmentsOnly);
        _grouping.ClearDisplayGroups();
        await LoadMailboxAndProjectAsync(resetStack: true).ConfigureAwait(true);
    }

    public EmailMailboxQuery BuildQuery()
    {
        var scope = _owner.SelectedMailboxScope;
        if (!string.IsNullOrWhiteSpace(_owner.SelectedLabel))
        {
            scope = EmailMailboxScope.Label;
        }

        return new EmailMailboxQuery
        {
            FreeText = string.IsNullOrWhiteSpace(_owner.SearchText) ? null : _owner.SearchText.Trim(),
            Subject = string.IsNullOrWhiteSpace(_owner.SubjectFilter) ? null : _owner.SubjectFilter.Trim(),
            FromOrTo = string.IsNullOrWhiteSpace(_owner.AddressFilter) ? null : _owner.AddressFilter.Trim(),
            LabelName = _owner.SelectedLabel,
            MailboxScope = scope,
            ProjectLinkFilter = _owner.SelectedProjectLinkFilter,
            AttachmentsOnly = _owner.AttachmentsOnly,
            PageSize = EmailListViewModel.PageSize,
        };
    }

    public EmailMailboxQuery BuildProjectGroupQuery(EmailMailboxQuery query, int pageSize)
    {
        var sized = query with { PageSize = pageSize };
        if (!string.IsNullOrWhiteSpace(_owner.GetProjectContext()?.ProjectLabelName))
        {
            return sized with { OptionalProjectLabel = _owner.GetProjectContext()!.ProjectLabelName!.Trim() };
        }

        return sized;
    }

    public void ClearEmailState()
    {
        _owner.PageTokenStack.Clear();
        _owner.SetNextPageToken(null);
        _owner.SetLastUsedPageToken(null);
        _owner.SetCurrentPageNumber(1);
        _owner.SetDisplayedCount(0);
        _owner.SetHasNextPage(false);
        _owner.NotifyHasPreviousPageChanged();

        _owner.SearchText = string.Empty;
        _owner.AddressFilter = string.Empty;
        _owner.SubjectFilter = string.Empty;
        _owner.SelectedLabel = null;
        _owner.SelectedMailboxScope = EmailMailboxScope.Inbox;
        _owner.SelectedProjectLinkFilter = EmailProjectLinkFilter.All;
        _owner.SetGroupByLabel(true);
        _grouping.ClearProjectGroup();
        _grouping.ClearDisplayGroups();
        _owner.SetMailboxUnreadTotal(0);
        _owner.SetMailboxUnreadIsExact(true);
        _owner.SetLastLoadedGmailQuery(null);
        _owner.SetLastUnreadQuerySignature(null);

        _owner.AvailableLabels.Clear();
        _owner.FlatDisplayEmails.Clear();

        _owner.SetLoadWarning(null);
        _owner.SetLoadError(null);
        _owner.SetLoadState(EmailListLoadState.Idle);

        _owner.SelectedEmail = null;
        _display.ReplaceRows([]);
    }

    public Task RefreshAccountProfileAsync() => RefreshGmailAccountStatusAsync();

    private async Task DisconnectGmailCoreAsync()
    {
        _owner.SetIsBusy(true);
        _owner.SetStatusMessage("מתנתק...");
        try
        {
            _owner.AuthService.Logout();
            _owner.SetStatusMessage("מנותק מ-Gmail.");
        }
        finally
        {
            _owner.SetIsBusy(false);
            await RefreshGmailAccountStatusAsync().ConfigureAwait(true);
        }

        await Task.CompletedTask;
    }

    private async Task RefreshGmailAccountStatusAsync()
    {
        await _owner.AuthService.RefreshAccountProfileAsync().ConfigureAwait(true);
        UiThread.Run(_owner.NotifyAuthProperties);
    }

    private bool ShouldRefreshUnreadTotal(EmailMailboxQuery query, bool resetStack) =>
        resetStack || !string.Equals(_owner.LastUnreadQuerySignature, BuildUnreadQuerySignature(query), StringComparison.Ordinal);

    private static string BuildUnreadQuerySignature(EmailMailboxQuery query) =>
        $"{query.MailboxScope}|{query.LabelName}|{query.Subject}|{query.FromOrTo}|{query.FreeText}|{query.ProjectLinkFilter}|{query.AttachmentsOnly}";

    private void ApplyMailboxUnreadCount(EmailMailboxUnreadCount unreadCount)
    {
        _owner.SetMailboxUnreadTotal(unreadCount.Count);
        _owner.SetMailboxUnreadIsExact(unreadCount.IsExact);
        _owner.SetLastUnreadQuerySignature(BuildUnreadQuerySignature(BuildQuery()));
        _owner.NotifyUnreadDisplayProperties();
    }
}
