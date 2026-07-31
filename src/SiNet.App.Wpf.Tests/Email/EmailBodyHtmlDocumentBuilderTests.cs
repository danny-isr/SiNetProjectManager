using SiNet.Application.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailBodyHtmlDocumentBuilderTests
{
    [Fact]
    public void Build_plain_text_encodes_body_and_includes_subject()
    {
        var html = EmailBodyHtmlDocumentBuilder.Build(
            subject: "Hello <x>",
            fromDisplay: "a@b.com",
            receivedAt: new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
            internetMessageId: "<id@test>",
            bodyContent: "Line & more",
            isPlainTextFallback: true);

        Assert.Contains("Hello &lt;x&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Line &amp; more", html, StringComparison.Ordinal);
        Assert.Contains("white-space: pre-wrap", html, StringComparison.Ordinal);
        Assert.Contains("Message-ID:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_html_body_keeps_markup()
    {
        var html = EmailBodyHtmlDocumentBuilder.Build(
            subject: "S",
            fromDisplay: "a@b.com",
            receivedAt: DateTimeOffset.UtcNow,
            internetMessageId: null,
            bodyContent: "<p>Hi</p>",
            isPlainTextFallback: false);

        Assert.Contains("<p>Hi</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;p&gt;", html, StringComparison.Ordinal);
    }
}
