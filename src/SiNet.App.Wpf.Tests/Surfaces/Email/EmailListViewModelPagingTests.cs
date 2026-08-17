using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;
public sealed class EmailListViewModelPagingTests
{
    [Fact]
    public async Task Load_first_page_uses_page_size_50()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Equal(EmailMailboxQuery.DefaultPageSize, gateway.LastQuery?.PageSize);
        Assert.Equal(1, sut.CurrentPageNumber);
    }

    [Fact]
    public async Task Next_page_passes_next_token_and_increments_page_number()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        Assert.Equal("page-2", gateway.LastNextToken);

        await sut.LoadNextPageAsync();

        Assert.Equal(2, sut.CurrentPageNumber);
        Assert.Equal("page-2", gateway.LastPageToken);
    }

    [Fact]
    public async Task Previous_page_restores_prior_token()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        await sut.LoadNextPageAsync();
        await sut.LoadPreviousPagePublicAsync();

        Assert.Equal(1, sut.CurrentPageNumber);
        Assert.Null(gateway.LastPageToken);
    }

    [Fact]
    public async Task Clear_filters_resets_to_all_emails()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService())
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
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(
            2466,
            "2466",
            "תכנית דוגמה",
            "2466 — תכנית דוגמה",
            "תל אביב"));

        Assert.True(sut.HasActiveProject);
        Assert.NotNull(sut.ActiveProjectGroup);
        Assert.Equal(10, sut.ActiveProjectGroup!.Emails.Count);

        await sut.LoadAllForLabelGroupForTestsAsync(sut.ActiveProjectGroup);

        Assert.Equal(15, sut.ActiveProjectGroup.Emails.Count);
        Assert.True(sut.ActiveProjectGroup.HasLoadedAll);
    }

    [Fact]
    public async Task Clearing_project_context_returns_to_all_emails_mode()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));
        await sut.ApplyProjectContextAsync(null);

        Assert.False(sut.HasActiveProject);
        Assert.Null(sut.ActiveProjectGroup);
        Assert.True(gateway.MailboxPageCalls >= 2);
    }

    [Fact]
    public async Task Link_enrichment_maps_internet_message_id_to_project_display()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var linkQuery = new EmailListViewModelTestFixtures.StubThreadLinkQuery();
        var sut = new EmailListViewModel(gateway, linkQuery, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        // SQL enrichment alone must not mark «משויך» (Gmail project label is SoT).
        Assert.Equal(EmailProjectLinkState.Unlinked, sut.Emails[0].ProjectLinkState);
        Assert.False(sut.Emails[0].IsFiledToProject);
        Assert.Equal("לא משויך", sut.Emails[0].ProjectLinkDisplay);
    }

    [Fact]
    public async Task Partial_enrichment_failure_still_shows_gmail_rows()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, new EmailListViewModelTestFixtures.FailingThreadLinkQuery(), new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Single(sut.Emails);
        Assert.Equal(EmailListLoadState.PartialFailure, sut.LoadState);
        Assert.Contains("שיוך", sut.LoadWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gmail_page_failure_keeps_previous_rows_when_navigating()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway { FailOnSecondPage = true };
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        Assert.Single(sut.Emails);

        await sut.LoadNextPageAsync();

        Assert.Single(sut.Emails);
        Assert.Equal(EmailListLoadState.Error, sut.LoadState);
    }

    [Fact]
    public async Task Gmail_disconnect_clears_email_list()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true, ConnectedAccountEmail = "user@example.com" };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
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
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
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
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.RefreshPageAsync();
        Assert.NotNull(sut.SelectedEmail);

        await sut.DisconnectGmailForTestsAsync();

        Assert.Null(sut.SelectedEmail);
    }

    [Fact]
    public async Task Gmail_reconnect_loads_first_page_from_new_account()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
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
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(sut.IsConnected);
        Assert.Contains("new-user@example.com", sut.AccountStatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_gmail_updates_connected_email_without_reopening_window()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false, ConnectedAccountEmail = "old@example.com" };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.Equal("new-user@example.com", sut.ConnectedAccountEmail);
    }

    [Fact]
    public async Task Connect_gmail_enables_disconnect_command()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(sut.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_gmail_disables_connect_command_after_success()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.False(sut.ShowConnectButton);
    }

    [Fact]
    public async Task Connect_gmail_loads_first_email_page_after_success()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(gateway.MailboxPageCalls >= 1);
    }

    [Fact]
    public async Task Disconnect_then_connect_updates_ui_to_new_account()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true, ConnectedAccountEmail = "first@example.com" };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.DisconnectGmailForTestsAsync();
        auth.LoginConnectedEmail = "second@example.com";
        await sut.ConnectGmailForTestsAsync();

        Assert.Equal("second@example.com", sut.ConnectedAccountEmail);
        Assert.Contains("second@example.com", sut.AccountStatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_failure_shows_error_message()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService
        {
            IsAuthenticated = false,
            LoginSucceeds = false,
            RestoreSessionOnFailedLogin = false,
        };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.False(sut.IsConnected);
        Assert.Contains("בוטלה", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_restores_session_when_login_returns_false_but_token_exists()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService
        {
            IsAuthenticated = false,
            LoginSucceeds = false,
            RestoreSessionOnFailedLogin = true,
            RestoredAccountEmail = "restored@example.com",
        };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();

        Assert.True(sut.IsConnected);
        Assert.Equal("restored@example.com", sut.ConnectedAccountEmail);
        Assert.True(gateway.MailboxPageCalls >= 1);
    }

    [Fact]
    public async Task Refresh_emails_success_implies_account_status_connected()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = false };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, auth);

        await sut.ConnectGmailForTestsAsync();
        await sut.RefreshPageAsync();

        Assert.True(sut.IsConnected);
        Assert.True(gateway.MailboxPageCalls >= 1);
    }

    [Fact]
    public async Task Gmail_reconnect_resets_page_index_to_first_page()
    {
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
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
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true };
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
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
        var auth = new EmailListViewModelTestFixtures.TrackingAuthService { IsAuthenticated = true, ConnectedAccountEmail = "user@example.com" };
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, auth);

        sut.DisconnectGmailForTestsAsync().GetAwaiter().GetResult();

        Assert.False(sut.IsConnected);
        Assert.Null(auth.ConnectedAccountEmail);
    }

    [Fact]
    public void Email_list_default_scope_is_inbox()
    {
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());
        Assert.Equal(EmailMailboxScope.Inbox, sut.SelectedMailboxScope);
    }

    [Fact]
    public async Task Email_list_default_query_uses_inbox_label()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Equal(EmailMailboxScope.Inbox, gateway.LastQuery?.MailboxScope);
        Assert.DoesNotContain("category:", EmailMailboxQueryComposer.BuildSearchQuery(gateway.LastQuery!), StringComparison.Ordinal);
        Assert.Contains("label:INBOX", EmailMailboxQueryComposer.BuildSearchQuery(gateway.LastQuery!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_list_default_does_not_include_promotions_unless_selected()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        var defaultQuery = EmailMailboxQueryComposer.BuildSearchQuery(gateway.LastQuery!);
        Assert.DoesNotContain("promotions", defaultQuery, StringComparison.OrdinalIgnoreCase);

        sut.SelectedLabel = "CATEGORY_PROMOTIONS";
        await sut.ApplyFiltersAsync();
        Assert.Equal(EmailMailboxScope.Label, gateway.LastQuery?.MailboxScope);
        Assert.Equal("CATEGORY_PROMOTIONS", gateway.LastQuery?.LabelName);
    }

    [Fact]
    public async Task Email_list_can_select_all_mail_scope()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService())
        {
            SelectedMailboxScope = EmailMailboxScope.AllMail,
        };

        await sut.RefreshPageAsync();

        Assert.Equal(EmailMailboxScope.AllMail, gateway.LastQuery?.MailboxScope);
    }

    [Fact]
    public async Task Email_list_can_select_label_scope()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService())
        {
            SelectedLabel = "INBOX",
        };

        await sut.ApplyFiltersAsync();

        Assert.Equal(EmailMailboxScope.Label, sut.SelectedMailboxScope);
        Assert.Equal(EmailMailboxScope.Label, gateway.LastQuery?.MailboxScope);
    }

    [Fact]
    public async Task Email_list_unread_total_uses_separate_gmail_query_not_current_page_only()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway { ConfiguredUnreadTotal = 7 };
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.True(gateway.UnreadCountCalls >= 1);
        Assert.Equal(7, sut.MailboxUnreadTotal);
    }

    [Fact]
    public async Task Email_list_displays_unread_total_for_inbox_scope()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway { ConfiguredUnreadTotal = 3 };
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.True(sut.MailboxUnreadIsExact);
        Assert.Equal(3, sut.MailboxUnreadTotal);
        Assert.Contains("3", sut.UnreadCountDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_list_displays_unread_in_current_page_separately()
    {
        var gateway = new EmailListViewModelTestFixtures.UnreadPagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Contains("בעמוד:", sut.UnreadCountDisplay, StringComparison.Ordinal);
        Assert.Equal(1, sut.UnreadInCurrentPage);
        Assert.Equal(5, sut.MailboxUnreadTotal);
    }

    [Fact]
    public async Task Email_list_clears_old_unread_state_on_refresh()
    {
        var gateway = new EmailListViewModelTestFixtures.UnreadPagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        gateway.ReturnUnreadRows = false;
        await sut.RefreshPageAsync();

        Assert.Equal(0, sut.UnreadInCurrentPage);
    }

    [Fact]
    public async Task Email_list_paging_does_not_change_total_unread_count_incorrectly()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway { ConfiguredUnreadTotal = 4 };
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        var unreadCallsAfterFirst = gateway.UnreadCountCalls;
        var totalAfterFirst = sut.MailboxUnreadTotal;

        await sut.LoadNextPageAsync();

        Assert.Equal(unreadCallsAfterFirst, gateway.UnreadCountCalls);
        Assert.Equal(totalAfterFirst, sut.MailboxUnreadTotal);
    }

    [Fact]
    public async Task Email_list_unread_item_true_only_when_labelIds_contains_UNREAD()
    {
        var gateway = new EmailListViewModelTestFixtures.UnreadPagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Contains(sut.Emails, static row => row.IsUnread);
        Assert.Contains(sut.Emails, static row => !row.IsUnread);
    }

    [Fact]
    public async Task Email_list_missing_labelIds_does_not_mark_unread()
    {
        var gateway = new EmailListViewModelTestFixtures.UnreadPagingEmailGateway { ReturnUnreadRows = false };
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.All(sut.Emails, static row => Assert.False(row.IsUnread));
    }
}

