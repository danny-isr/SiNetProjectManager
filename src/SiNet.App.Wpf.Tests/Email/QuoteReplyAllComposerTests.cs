using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email.QuoteSend;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class QuoteReplyAllComposerTests
{
    [Fact]
    public void BuildReplyAll_puts_from_in_to_and_others_in_cc_excluding_self()
    {
        var details = new EmailMessageDetails(
            MessageId: "g1",
            ThreadId: "t1",
            From: new EmailAddress("client@example.com"),
            Subject: "בקשת הצעה",
            ReceivedAt: DateTimeOffset.UtcNow,
            BodyText: "hi",
            Attachments: Array.Empty<EmailMessageAttachmentDetails>(),
            InternetMessageId: "<mid@example.com>",
            ToAddresses: ["me@office.com", "peer@example.com"],
            CcAddresses: ["other@example.com", "me@office.com"]);

        var draft = QuoteReplyAllComposer.BuildReplyAll(details, "me@office.com", projectId: 10, marker: "SINET-QS-1-x");

        Assert.Equal(QuoteSendComposeMode.ReplyAll, draft.Mode);
        Assert.Equal(["client@example.com"], draft.To);
        Assert.Contains("peer@example.com", draft.Cc);
        Assert.Contains("other@example.com", draft.Cc);
        Assert.DoesNotContain("me@office.com", draft.To);
        Assert.DoesNotContain("me@office.com", draft.Cc);
        Assert.Equal("Re: בקשת הצעה", draft.Subject);
        Assert.DoesNotContain("SINET-QS", draft.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SINET-QS-1-x", draft.Body, StringComparison.Ordinal);
        Assert.Equal("t1", draft.ThreadId);
        Assert.Equal("<mid@example.com>", draft.InReplyToMessageId);
    }

    [Fact]
    public void BuildNewCompose_has_no_thread_and_marker_only_in_body()
    {
        var draft = QuoteReplyAllComposer.BuildNewCompose(3142, "SINET-QS-9-y");
        Assert.Equal(QuoteSendComposeMode.NewCompose, draft.Mode);
        Assert.Empty(draft.To);
        Assert.Null(draft.ThreadId);
        Assert.DoesNotContain("SINET-QS", draft.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3142", draft.Subject, StringComparison.Ordinal);
        Assert.Contains("SINET-QS-9-y", draft.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailHeaderAddressParser_splits_display_names()
    {
        var parsed = EmailHeaderAddressParser.Parse(
            "\"Last, First\" <a@example.com>, b@example.com");
        Assert.Equal(2, parsed.Count);
        Assert.Contains("a@example.com", parsed);
        Assert.Contains("b@example.com", parsed);
    }
}
