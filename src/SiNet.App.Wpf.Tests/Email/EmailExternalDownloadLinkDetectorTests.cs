using SiNet.Application.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailExternalDownloadLinkDetectorTests
{
    [Theory]
    [InlineData("https://www.jumbomail.me/en/Transfer/Download/abc", true)]
    [InlineData("https://we.tl/t-xyz", true)]
    [InlineData("https://wetransfer.com/downloads/abc", true)]
    [InlineData("https://drive.google.com/file/d/abc/view", true)]
    [InlineData("https://www.dropbox.com/s/abc/file.zip", true)]
    [InlineData("https://1drv.ms/u/s!abc", true)]
    [InlineData("https://transfernow.net/dl/abc", true)]
    [InlineData("https://example.com/file.zip", false)]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", false)]
    public void IsExternalDownloadUrl_filters_known_hosts(string url, bool expected)
    {
        Assert.Equal(expected, EmailExternalDownloadLinkDetector.IsExternalDownloadUrl(url));
    }

    [Fact]
    public void ExtractUrls_returns_only_known_hosts_from_body()
    {
        var body =
            "ראה קבצים ב-https://drive.google.com/file/d/abc/view וגם https://example.com/x וגם https://we.tl/t-1";

        var urls = EmailExternalDownloadLinkDetector.ExtractUrls(body);

        Assert.Equal(2, urls.Count);
        Assert.Contains(urls, u => u.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(urls, u => u.Contains("we.tl", StringComparison.OrdinalIgnoreCase));
    }
}
