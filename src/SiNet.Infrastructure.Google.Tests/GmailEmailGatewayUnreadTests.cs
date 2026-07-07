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
    public void BuildMailboxQuery_inbox_scope_uses_primary_category()
    {
        var query = new EmailMailboxQuery { MailboxScope = EmailMailboxScope.Inbox };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("label:INBOX", result, StringComparison.Ordinal);
        Assert.Contains("category:primary", result, StringComparison.Ordinal);
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
    public void BuildMailboxQuery_unread_scope_appends_is_unread()
    {
        var query = new EmailMailboxQuery { MailboxScope = EmailMailboxScope.Unread };
        var result = GmailEmailGateway.BuildMailboxQueryString(query);

        Assert.Contains("is:unread", result, StringComparison.Ordinal);
        Assert.Contains("category:primary", result, StringComparison.Ordinal);
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
    public void BuildUnreadCountQuery_inbox_uses_primary_unread_query()
    {
        var query = new EmailMailboxQuery { MailboxScope = EmailMailboxScope.Inbox };
        var result = GmailEmailGateway.BuildUnreadCountQuery(query, inboxQueryOverride: null);

        Assert.Equal(EmailMailboxQueryComposer.InboxPrimaryUnreadQuery, result);
    }
}