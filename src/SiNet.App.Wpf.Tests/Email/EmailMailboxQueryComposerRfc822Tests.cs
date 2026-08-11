using SiNet.Application.Abstractions.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailMailboxQueryComposerRfc822Tests
{
    [Fact]
    public void BuildRfc822MessageIdSearchTerm_quotes_and_strips_brackets()
    {
        var term = EmailMailboxQueryComposer.BuildRfc822MessageIdSearchTerm(
            "<VI2PR06MB9879@eurprd06.prod.outlook.com>");
        Assert.Equal("rfc822msgid:\"VI2PR06MB9879@eurprd06.prod.outlook.com\"", term);
    }

    [Fact]
    public void BuildSearchQuery_rfc822msgid_FreeText_is_not_anded_with_AllMail_exclusions()
    {
        var q = EmailMailboxQueryComposer.BuildSearchQuery(new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.AllMail,
            FreeText = "rfc822msgid:\"abc@example.com\"",
        });
        Assert.Equal("rfc822msgid:\"abc@example.com\"", q);
        Assert.DoesNotContain("-in:sent", q, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetGmailApiMessageId_rejects_rfc822_unique_ids()
    {
        Assert.Null(EmailMailboxQueryComposer.TryGetGmailApiMessageId("abc@example.com"));
        Assert.Equal("18f9", EmailMailboxQueryComposer.TryGetGmailApiMessageId("gmail:18f9"));
        Assert.Equal("18f9abc", EmailMailboxQueryComposer.TryGetGmailApiMessageId("18f9abc"));
    }
}
