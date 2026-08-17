using SiNet.Application.Abstractions.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailMailboxQueryComposerCategoryTests
{
    [Fact]
    public void BuildSearchQuery_inbox_default_is_label_inbox_without_category()
    {
        var q = EmailMailboxQueryComposer.BuildSearchQuery(new EmailMailboxQuery());

        Assert.Equal("label:INBOX", q);
        Assert.DoesNotContain("category:", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchQuery_inbox_primary_category_appends_category_primary()
    {
        var q = EmailMailboxQueryComposer.BuildSearchQuery(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Inbox,
            Category = EmailMailboxCategory.Primary,
        });

        Assert.Contains("label:INBOX", q, StringComparison.Ordinal);
        Assert.Contains("category:primary", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchQuery_unread_only_appends_is_unread_without_forcing_primary()
    {
        var q = EmailMailboxQueryComposer.BuildSearchQuery(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Inbox,
            UnreadOnly = true,
        });

        Assert.Equal("label:INBOX is:unread", q);
    }

    [Fact]
    public void BuildSearchQuery_deprecated_unread_scope_maps_to_inbox_unread_only()
    {
#pragma warning disable CS0618
        var q = EmailMailboxQueryComposer.BuildSearchQuery(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Unread,
        });
#pragma warning restore CS0618

        Assert.Equal("label:INBOX is:unread", q);
        Assert.DoesNotContain("category:", q, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_unread_scope_sets_inbox_and_unread_only()
    {
#pragma warning disable CS0618
        var normalized = EmailMailboxQueryComposer.Normalize(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Unread,
            Category = EmailMailboxCategory.Updates,
        });
#pragma warning restore CS0618

        Assert.Equal(EmailMailboxScope.Inbox, normalized.MailboxScope);
        Assert.True(normalized.UnreadOnly);
        Assert.Equal(EmailMailboxCategory.Updates, normalized.Category);
    }

    [Fact]
    public void BuildSearchQuery_all_mail_preserves_exclusions()
    {
        var q = EmailMailboxQueryComposer.BuildSearchQuery(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.AllMail,
        });

        Assert.Equal(EmailMailboxQueryComposer.AllMailQuery, q);
    }

    [Fact]
    public void BuildUnreadCountQuery_includes_category_when_set()
    {
        var q = EmailMailboxQueryComposer.BuildUnreadCountQuery(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Inbox,
            Category = EmailMailboxCategory.Social,
        });

        Assert.Equal("label:INBOX category:social is:unread", q);
    }

    [Theory]
    [InlineData(EmailMailboxCategory.Updates, "category:updates")]
    [InlineData(EmailMailboxCategory.Promotions, "category:promotions")]
    [InlineData(EmailMailboxCategory.Forums, "category:forums")]
    public void ResolveCategoryClause_maps_known_categories(EmailMailboxCategory category, string expected)
    {
        Assert.Equal(expected, EmailMailboxQueryComposer.ResolveCategoryClause(category));
    }

    [Fact]
    public void ResolveCategoryClause_all_is_null()
    {
        Assert.Null(EmailMailboxQueryComposer.ResolveCategoryClause(EmailMailboxCategory.All));
    }
}
