using SiNet.App.Wpf.Surfaces.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailInlineImageRewriteTests
{
    private const string Host = "sinet-mail-images.local";

    [Fact]
    public void Rewrites_cid_source_to_virtual_host_url()
    {
        const string html = "<p>hi</p><img src=\"cid:logo001\" alt=\"logo\"/>";

        var result = WebView2EmailBodyRenderer.RewriteInlineCidSources(html, Host);

        Assert.Contains($"src=\"https://{Host}/logo001\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("cid:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizes_angle_bracketed_content_id()
    {
        const string html = "<img src='cid:<abc@mail>'>";

        var result = WebView2EmailBodyRenderer.RewriteInlineCidSources(html, Host);

        // Angle brackets stripped; value URL-escaped.
        Assert.Contains($"https://{Host}/abc%40mail", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_external_and_data_sources_untouched()
    {
        const string html =
            "<img src=\"https://example.com/a.png\"><img src=\"data:image/png;base64,AAAA\">";

        var result = WebView2EmailBodyRenderer.RewriteInlineCidSources(html, Host);

        Assert.Equal(html, result);
    }

    [Fact]
    public void Null_or_empty_html_is_returned_as_is()
    {
        Assert.Equal(string.Empty, WebView2EmailBodyRenderer.RewriteInlineCidSources(string.Empty, Host));
    }
}
