using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailListViewModelTests
{
    [Fact]
    public async Task Load_first_page_uses_page_size_50()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Equal(EmailMailboxQuery.DefaultPageSize, gateway.LastQuery?.PageSize);
        Assert.Equal(1, sut.CurrentPageNumber);
    }

    [Fact]
    public async Task Next_page_passes_next_token_and_increments_page_number()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());

        await sut.RefreshPageAsync();
        Assert.Equal("page-2", gateway.LastNextToken);

        await sut.LoadNextPageAsync();

        Assert.Equal(2, sut.CurrentPageNumber);
        Assert.Equal("page-2", gateway.LastPageToken);
    }

    [Fact]
    public async Task Previous_page_restores_prior_token()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());

        await sut.RefreshPageAsync();
        await sut.LoadNextPageAsync();
        await sut.LoadPreviousPagePublicAsync();

        Assert.Equal(1, sut.CurrentPageNumber);
        Assert.Null(gateway.LastPageToken);
    }

    [Fact]
    public async Task Clear_filters_resets_to_all_emails()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService())
        {
            SearchText = "foo",
            AddressFilter = "bar@x.com",
            SubjectFilter = "subject",
            SelectedProjectLinkFilter = EmailProjectLinkFilter.Linked,
        };

        await sut.ClearFiltersAndReloadAsync();

        Assert.Equal(string.Empty, sut.SearchText);
        Assert.Equal(EmailProjectLinkFilter.All, sut.SelectedProjectLinkFilter);
        Assert.Null(gateway.LastQuery?.FreeText);
    }

    [Fact]
    public async Task Project_context_loads_first_ten_emails_via_project_label_gateway()
    {
        var gateway = new ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(
            2466,
            "2466",
            "תכנית דוגמה",
            "2466 — תכנית דוגמה",
            "תל אביב"));

        Assert.True(sut.IsProjectMode);
        Assert.Equal(10, sut.Emails.Count);
        Assert.True(sut.HasMoreProjectEmails);
        Assert.Equal("2466 — תכנית דוגמה", sut.ProjectGroupHeader);

        sut.ShowMoreProjectEmailsCommand.Execute(null);

        Assert.Equal(15, sut.Emails.Count);
        Assert.False(sut.HasMoreProjectEmails);
    }

    [Fact]
    public async Task Clearing_project_context_returns_to_all_emails_mode()
    {
        var gateway = new ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));
        await sut.ApplyProjectContextAsync(null);

        Assert.True(sut.IsAllEmailsMode);
        Assert.Equal(1, gateway.MailboxPageCalls);
    }

    [Fact]
    public async Task Link_enrichment_maps_internet_message_id_to_project_display()
    {
        var gateway = new PagingEmailGateway();
        var linkQuery = new StubThreadLinkQuery();
        var sut = new EmailListViewModel(gateway, linkQuery, new StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Equal("1042 — North", sut.Emails[0].ProjectDisplay);
        Assert.Equal(EmailProjectLinkState.Linked, sut.Emails[0].ProjectLinkState);
    }

    [Fact]
    public async Task Partial_enrichment_failure_still_shows_gmail_rows()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, new FailingThreadLinkQuery(), new StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Single(sut.Emails);
        Assert.Equal(EmailListLoadState.PartialFailure, sut.LoadState);
        Assert.Contains("שיוך", sut.LoadWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gmail_page_failure_keeps_previous_rows_when_navigating()
    {
        var gateway = new PagingEmailGateway { FailOnSecondPage = true };
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());

        await sut.RefreshPageAsync();
        Assert.Single(sut.Emails);

        await sut.LoadNextPageAsync();

        Assert.Single(sut.Emails);
        Assert.Equal(EmailListLoadState.Error, sut.LoadState);
    }

    [Fact]
    public async Task Gmail_disconnect_clears_email_list()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true, ConnectedAccountEmail = "user@example.com" };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.RefreshPageAsync();
        Assert.NotEmpty(sut.Emails);

        await sut.DisconnectGmailForTestsAsync();

        Assert.Empty(sut.Emails);
        Assert.False(sut.IsConnected);
    }

    [Fact]
    public async Task Gmail_disconnect_clears_paging_tokens()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.RefreshPageAsync();
        await sut.LoadNextPageAsync();
        Assert.Equal(2, sut.CurrentPageNumber);

        await sut.DisconnectGmailForTestsAsync();

        Assert.Equal(1, sut.CurrentPageNumber);
        Assert.False(sut.HasNextPage);
        Assert.False(sut.HasPreviousPage);
    }

    [Fact]
    public async Task Gmail_disconnect_clears_selected_email()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.RefreshPageAsync();
        Assert.NotNull(sut.SelectedEmail);

        await sut.DisconnectGmailForTestsAsync();

        Assert.Null(sut.SelectedEmail);
    }

    [Fact]
    public async Task Gmail_reconnect_loads_first_page_from_new_account()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(auth.LastLoginOptions?.SkipSilentRestore);
        Assert.True(auth.LastLoginOptions?.PromptAccountSelection);
        Assert.Equal(1, gateway.MailboxPageCalls);
        Assert.Equal(1, sut.CurrentPageNumber);
        Assert.NotEmpty(sut.Emails);
    }

    [Fact]
    public async Task Connect_gmail_refreshes_account_status_after_success()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(sut.IsConnected);
        Assert.Contains("new-user@example.com", sut.AccountStatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_gmail_updates_connected_email_without_reopening_window()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false, ConnectedAccountEmail = "old@example.com" };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.Equal("new-user@example.com", sut.ConnectedAccountEmail);
    }

    [Fact]
    public async Task Connect_gmail_enables_disconnect_command()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(sut.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_gmail_disables_connect_command_after_success()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.False(sut.ShowConnectButton);
    }

    [Fact]
    public async Task Connect_gmail_loads_first_email_page_after_success()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(gateway.MailboxPageCalls >= 1);
    }

    [Fact]
    public async Task Disconnect_then_connect_updates_ui_to_new_account()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true, ConnectedAccountEmail = "first@example.com" };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.DisconnectGmailForTestsAsync();
        auth.LoginConnectedEmail = "second@example.com";
        await sut.ConnectGmailForTestsAsync();

        Assert.Equal("second@example.com", sut.ConnectedAccountEmail);
        Assert.Contains("second@example.com", sut.AccountStatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_failure_shows_error_message()
    {
        var auth = new TrackingAuthService
        {
            IsAuthenticated = false,
            LoginSucceeds = false,
            RestoreSessionOnFailedLogin = false,
        };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.False(sut.IsConnected);
        Assert.Contains("בוטלה", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_restores_session_when_login_returns_false_but_token_exists()
    {
        var auth = new TrackingAuthService
        {
            IsAuthenticated = false,
            LoginSucceeds = false,
            RestoreSessionOnFailedLogin = true,
            RestoredAccountEmail = "restored@example.com",
        };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(sut.IsConnected);
        Assert.Equal("restored@example.com", sut.ConnectedAccountEmail);
        Assert.True(gateway.MailboxPageCalls >= 1);
    }

    [Fact]
    public async Task Refresh_emails_success_implies_account_status_connected()
    {
        var auth = new TrackingAuthService { IsAuthenticated = false };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();
        await sut.RefreshPageAsync();

        Assert.True(sut.IsConnected);
        Assert.True(gateway.MailboxPageCalls >= 1);
    }

    [Fact]
    public async Task Gmail_reconnect_resets_page_index_to_first_page()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.RefreshPageAsync();
        await sut.LoadNextPageAsync();
        Assert.Equal(2, sut.CurrentPageNumber);

        auth.IsAuthenticated = false;
        await sut.DisconnectGmailForTestsAsync();

        await sut.ConnectGmailForTestsAsync();

        Assert.Equal(1, sut.CurrentPageNumber);
    }

    [Fact]
    public async Task Gmail_refresh_after_disconnect_does_not_load_old_emails()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true };
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.RefreshPageAsync();
        var callsBeforeDisconnect = gateway.MailboxPageCalls;

        await sut.DisconnectGmailForTestsAsync();
        await sut.RefreshPageAsync();

        Assert.Equal(callsBeforeDisconnect, gateway.MailboxPageCalls);
        Assert.Empty(sut.Emails);
    }

    [Fact]
    public void Disconnect_clears_connected_account_email()
    {
        var auth = new TrackingAuthService { IsAuthenticated = true, ConnectedAccountEmail = "user@example.com" };
        var sut = new EmailListViewModel(new PagingEmailGateway(), threadLinkQuery: null, auth);

        sut.DisconnectGmailForTestsAsync().GetAwaiter().GetResult();

        Assert.False(sut.IsConnected);
        Assert.Null(auth.ConnectedAccountEmail);
    }

    private sealed class TrackingAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; set; }

        public string? ConnectedAccountEmail { get; set; } = "test@example.com";

        public bool LoginSucceeds { get; set; } = true;

        public string LoginConnectedEmail { get; set; } = "new-user@example.com";

        public bool RestoreSessionOnFailedLogin { get; set; }

        public string? RestoredAccountEmail { get; set; }

        public ConnectorLoginOptions? LastLoginOptions { get; private set; }

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastLoginOptions = options;
            if (!LoginSucceeds)
            {
                return Task.FromResult(false);
            }

            IsAuthenticated = true;
            ConnectedAccountEmail = LoginConnectedEmail;
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public void Logout()
        {
            IsAuthenticated = false;
            ConnectedAccountEmail = null;
            AuthStateChanged?.Invoke(false);
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            if (!RestoreSessionOnFailedLogin)
            {
                return Task.FromResult(IsAuthenticated);
            }

            IsAuthenticated = true;
            ConnectedAccountEmail = RestoredAccountEmail;
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; set; } = true;

        public string? ConnectedAccountEmail { get; set; } = "test@example.com";

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

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

    private sealed class ProjectEmailGateway : IEmailGateway
    {
        public int MailboxPageCalls { get; private set; }

        public string? LastProjectLabel { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default)
        {
            LastProjectLabel = projectLabelName;
            var items = Enumerable.Range(1, 15)
                .Select(i => new EmailSummary(
                    $"proj-{i}",
                    $"thread-{i}",
                    EmailAddress.CreateOrFallback($"user{i}@example.com"),
                    $"Subject {i}",
                    DateTimeOffset.UtcNow.AddHours(-i),
                    false))
                .ToList();

            return Task.FromResult<IReadOnlyList<EmailSummary>>(items);
        }

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            MailboxPageCalls++;
            return Task.FromResult(new EmailMailboxPage([], query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);
    }

    private sealed class PagingEmailGateway : IEmailGateway
    {
        public bool FailOnSecondPage { get; set; }

        public int MailboxPageCalls { get; private set; }

        public EmailMailboxQuery? LastQuery { get; private set; }

        public string? LastPageToken { get; private set; }

        public string? LastNextToken { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            MailboxPageCalls++;
            if (FailOnSecondPage && pageToken is not null)
            {
                throw new InvalidOperationException("Gmail page failed");
            }

            LastQuery = query;
            LastPageToken = pageToken;
            return Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-1",
                    "thread-1",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Hello",
                    DateTimeOffset.UtcNow,
                    false,
                    InternetMessageId: "<abc@mail.com>"),
            ],
            query.PageSize,
            LastNextToken = "page-2",
            true));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);
    }

    private sealed class StubThreadLinkQuery : IEmailThreadLinkQueryService
    {
        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
            IReadOnlyList<string> internetMessageIds,
            CancellationToken cancellationToken = default)
        {
            var map = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["abc@mail.com"] = new EmailProjectLinkInfo(
                    IsLinked: true,
                    ProjectId: 1042,
                    ProjectNumber: "1042",
                    ProjectName: "North",
                    DisplayName: "1042 — North"),
            };

            return Task.FromResult<IReadOnlyDictionary<string, EmailProjectLinkInfo>>(map);
        }
    }

    private sealed class FailingThreadLinkQuery : IEmailThreadLinkQueryService
    {
        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
            IReadOnlyList<string> internetMessageIds,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DB enrichment failed");
    }
}
