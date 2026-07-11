using System.Text.RegularExpressions;

namespace SiNet.Application.Email.Acc;

/// <summary>Detects file-transfer download links in plain-text email bodies (JumboMail, WeTransfer, Drive, etc.).</summary>
public static partial class EmailExternalDownloadLinkDetector
{
    private static readonly string[] KnownHosts =
    [
        "jumbomail",
        "jumbo.me",
        "wetransfer",
        "we.tl",
        "drive.google",
        "docs.google",
        "dropbox",
        "onedrive",
        "1drv.ms",
        "sharepoint",
        "transfernow",
        "sendspace",
        "mega.nz",
        "mediafire",
    ];

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern { get; }

    public static bool HasExternalDownloadLink(string? bodyText) =>
        ExtractUrls(bodyText).Count > 0;

    public static IReadOnlyList<string> ExtractUrls(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return [];
        }

        var matches = UrlPattern.Matches(bodyText);
        if (matches.Count == 0)
        {
            return [];
        }

        var urls = new List<string>();
        foreach (Match match in matches)
        {
            var url = TrimTrailingPunctuation(match.Value);
            if (IsExternalDownloadUrl(url))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    public static bool IsExternalDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        foreach (var known in KnownHosts)
        {
            if (host.Contains(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string TrimTrailingPunctuation(string url)
    {
        while (url.Length > 0 && ",.;)]}".Contains(url[^1]))
        {
            url = url[..^1];
        }

        return url;
    }
}
