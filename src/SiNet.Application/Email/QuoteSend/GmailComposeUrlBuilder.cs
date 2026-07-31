using System.Text;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>Builds a Gmail web compose URL with subject/body (no silent API send).</summary>
public static class GmailComposeUrlBuilder
{
    public static string Build(string subject, string body, string? to = null)
    {
        var sb = new StringBuilder("https://mail.google.com/mail/?view=cm&fs=1");
        if (!string.IsNullOrWhiteSpace(to))
            sb.Append("&to=").Append(Uri.EscapeDataString(to.Trim()));
        if (!string.IsNullOrWhiteSpace(subject))
            sb.Append("&su=").Append(Uri.EscapeDataString(subject.Trim()));
        if (!string.IsNullOrWhiteSpace(body))
            sb.Append("&body=").Append(Uri.EscapeDataString(body.Trim()));
        return sb.ToString();
    }

    /// <summary>
    /// Legacy URL helper retained for tests. Subject must not include the tracking marker;
    /// marker lives only in a small body footer (SendQuote internal compose policy).
    /// </summary>
    public static (string Subject, string Body) BuildQuoteSendContent(string marker, int projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        var subject = $"הצעת מחיר — פרויקט {projectId}";
        var body = QuoteReplyAllComposer.BuildBody(marker, isReply: false);
        return (subject, body);
    }
}
