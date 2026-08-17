using SiNet.Application.Abstractions.Email;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailEmailGatewayUnreadTests
{
    [Theory]
    [InlineData(new[] { "INBOX", "UNREAD" }, true)]
    [InlineData(new[] { "INBOX" }, false)]
    [InlineData(new string[0], false)]
    public void ResolveIsUnread_reflects_gmail_unread_label(string[] labelIds, bool expected)
    {
        Assert.Equal(expected, GmailEmailGateway.ResolveIsUnread(labelIds));
    }

    [Fact]
    public void ResolveIsUnread_is_false_when_label_ids_missing()
    {
        Assert.False(GmailEmailGateway.ResolveIsUnread(null));
    }

    [Fact]
    public void BuildMailboxQuery_inbox_scope_uses_label_inbox_without_category_by_default()
    {
        var query = new EmailMailboxQuery { MailboxScope = EmailMailboxScope.Inbox };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("label:INBOX", result, StringComparison.Ordinal);
        Assert.DoesNotContain("category:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMailboxQuery_all_mail_excludes_spam_trash_sent_drafts()
    {
        var query = new EmailMailboxQuery { MailboxScope = EmailMailboxScope.AllMail };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("-in:spam", result, StringComparison.Ordinal);
        Assert.Contains("-in:trash", result, StringComparison.Ordinal);
        Assert.Contains("-in:drafts", result, StringComparison.Ordinal);
        Assert.Contains("-in:sent", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMailboxQuery_unread_scope_maps_to_inbox_unread_without_forcing_primary()
    {
#pragma warning disable CS0618
        var query = new EmailMailboxQuery { MailboxScope = EmailMailboxScope.Unread };
#pragma warning restore CS0618
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("is:unread", result, StringComparison.Ordinal);
        Assert.Contains("label:INBOX", result, StringComparison.Ordinal);
        Assert.DoesNotContain("category:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMailboxQuery_label_scope_uses_selected_label()
    {
        var query = new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Label,
            LabelName = "CATEGORY_PROMOTIONS",
        };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("label:CATEGORY_PROMOTIONS", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMailboxQuery_unread_only_overlay_on_inbox_appends_is_unread()
    {
        var query = new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Inbox,
            UnreadOnly = true,
        };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("label:INBOX", result, StringComparison.Ordinal);
        Assert.Contains("is:unread", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMailboxQuery_unread_scope_does_not_duplicate_is_unread()
    {
        var query = new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Unread,
            UnreadOnly = true,
        };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);
        var unreadCount = result.Split("is:unread", StringSplitOptions.None).Length - 1;

        Assert.Equal(1, unreadCount);
    }

    [Fact]
    public void HasNonScopeListFilters_includes_unread_only_overlay()
    {
        var query = new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Inbox,
            UnreadOnly = true,
        };

        Assert.True(GmailEmailGateway.HasNonScopeListFilters(query));
    }
}