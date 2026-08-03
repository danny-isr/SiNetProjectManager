using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Email.Detail;
using SiNet.Application.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

/// <summary>
/// DEV-001: a link clicked inside the rendered body must not navigate the body WebView2 away from
/// the message — it is handed to the host, which routes file-transfer hosts into the ACC download
/// window (same path as the attachment-strip chips).
/// </summary>
public sealed class EmailBodyLinkNavigationTests
{
    [Theory]
    [InlineData("about:blank")]
    [InlineData("about:blank#quoted-text")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("https://sinet-mail-images.local/logo001")]
    public void Body_document_uris_keep_navigating_in_place(string uri)
    {
        Assert.True(WebView2EmailBodyRenderer.IsInternalBodyUri(uri));
    }

    [Theory]
    [InlineData("https://www.jumbomail.me/j/abc123")]
    [InlineData("https://we.tl/t-abc123")]
    [InlineData("https://example.com/newsletter")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("javascript:void(0)")]
    public void Clicked_links_never_navigate_the_body(string uri)
    {
        Assert.False(WebView2EmailBodyRenderer.IsInternalBodyUri(uri));
    }

    [Theory]
    [InlineData("https://www.jumbomail.me/j/abc123")]
    [InlineData("http://example.com/newsletter")]
    [InlineData("mailto:someone@example.com")]
    public void Web_and_mail_links_are_handed_to_the_host(string uri)
    {
        Assert.True(WebView2EmailBodyRenderer.IsUserFollowableLink(uri));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:void(0)")]
    [InlineData("ms-settings:privacy")]
    public void Other_schemes_are_cancelled_without_reaching_the_host(string uri)
    {
        Assert.False(WebView2EmailBodyRenderer.IsUserFollowableLink(uri));
    }

    [Fact]
    public void Body_link_click_reaches_the_shared_open_path()
    {
        string? opened = null;
        var viewer = new EmailViewerPaneViewModel(url => opened = url);
        var renderer = new StubBodyRenderer();
        viewer.SetBodyRenderer(renderer);

        renderer.RaiseLinkClick("https://www.jumbomail.me/j/abc123");

        Assert.Equal("https://www.jumbomail.me/j/abc123", opened);
    }

    [Fact]
    public void Replacing_the_renderer_stops_forwarding_from_the_previous_one()
    {
        var openedCount = 0;
        var viewer = new EmailViewerPaneViewModel(_ => openedCount++);
        var first = new StubBodyRenderer();
        viewer.SetBodyRenderer(first);
        viewer.SetBodyRenderer(new StubBodyRenderer());

        first.RaiseLinkClick("https://www.jumbomail.me/j/abc123");

        Assert.Equal(0, openedCount);
    }

    private sealed class StubBodyRenderer : IEmailBodyRenderer
    {
        public event Action<string>? ExternalLinkRequested;

        public bool IsAvailable => true;

        public void AttachHost(object hostElement)
        {
        }

        public Task<bool> LoadAsync(EmailBodyRenderRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public void Clear()
        {
        }

        public void RaiseLinkClick(string url) => ExternalLinkRequested?.Invoke(url);
    }
}
