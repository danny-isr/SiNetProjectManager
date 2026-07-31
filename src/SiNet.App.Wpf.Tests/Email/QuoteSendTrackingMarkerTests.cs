using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email.QuoteSend;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class QuoteSendTrackingMarkerTests
{
    [Fact]
    public void Create_includes_instance_and_prefix()
    {
        var marker = QuoteSendTrackingMarker.Create(42, "abc123");
        Assert.Equal("SINET-QS-42-abc123", marker);
        Assert.True(QuoteSendTrackingMarker.LooksLikeMarker(marker));
    }

    [Fact]
    public void BuildSentSearchQuery_uses_sent_scope_and_marker()
    {
        var q = QuoteSendTrackingMarker.BuildSentSearchQuery("SINET-QS-1-token");
        Assert.Equal(EmailMailboxScope.Sent, q.MailboxScope);
        Assert.Equal("SINET-QS-1-token", q.FreeText);

        var gmail = EmailMailboxQueryComposer.BuildSearchQuery(q);
        Assert.Contains("in:sent", gmail, StringComparison.Ordinal);
        Assert.Contains("SINET-QS-1-token", gmail, StringComparison.Ordinal);
    }

    [Fact]
    public void GmailComposeUrlBuilder_embeds_subject_and_body()
    {
        var (subject, body) = GmailComposeUrlBuilder.BuildQuoteSendContent("SINET-QS-9-x", 3146);
        Assert.Contains("SINET-QS-9-x", subject, StringComparison.Ordinal);
        Assert.Contains("3146", subject, StringComparison.Ordinal);
        Assert.Contains("SINET-QS-9-x", body, StringComparison.Ordinal);

        var url = GmailComposeUrlBuilder.Build(subject, body);
        Assert.StartsWith("https://mail.google.com/mail/?view=cm&fs=1", url, StringComparison.Ordinal);
        Assert.Contains("su=", url, StringComparison.Ordinal);
        Assert.Contains("body=", url, StringComparison.Ordinal);
    }
}
