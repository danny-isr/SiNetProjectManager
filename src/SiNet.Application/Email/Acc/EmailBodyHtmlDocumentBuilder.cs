using System.Net;
using System.Text;

namespace SiNet.Application.Email.Acc;

/// <summary>
/// Builds a printable HTML document for ACC Inbox body PDF (parity with legacy
/// <c>EmailIngestionService.BuildEmailHtmlDocument</c>).
/// <para>
/// CID images are left as <c>cid:</c> references; the PDF renderer rewrites them to a
/// virtual host (same pattern as the UI body viewer) so large Base64 data-URIs are avoided.
/// </para>
/// </summary>
public static class EmailBodyHtmlDocumentBuilder
{
    public static string Build(
        string? subject,
        string? fromDisplay,
        DateTimeOffset receivedAt,
        string? internetMessageId,
        string bodyContent,
        bool isPlainTextFallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyContent);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"he\" dir=\"rtl\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine($"  <title>{WebUtility.HtmlEncode(subject ?? string.Empty)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif; margin: 20px; font-size: 12px; line-height: 1.5; }");
        sb.AppendLine("    .email-header { background: #f5f5f5; border: 1px solid #ddd; border-radius: 4px; padding: 15px; margin-bottom: 20px; }");
        sb.AppendLine("    .email-header h2 { margin: 0 0 10px 0; color: #333; font-size: 16px; }");
        sb.AppendLine("    .email-header p { margin: 5px 0; color: #666; font-size: 12px; }");
        sb.AppendLine("    .email-header .label { font-weight: 600; color: #444; }");
        sb.AppendLine("    .email-header .ids { font-size: 10px; color: #999; margin-top: 10px; padding-top: 10px; border-top: 1px solid #ddd; }");
        sb.AppendLine("    .email-body { padding: 10px; }");
        sb.AppendLine("    .email-body img { max-width: 100%; height: auto; }");
        if (isPlainTextFallback)
        {
            sb.AppendLine("    .email-body { white-space: pre-wrap; font-family: 'Courier New', monospace; }");
        }

        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"email-header\">");
        sb.AppendLine($"    <h2>{WebUtility.HtmlEncode(subject ?? string.Empty)}</h2>");
        sb.AppendLine($"    <p><span class=\"label\">מאת:</span> {WebUtility.HtmlEncode(fromDisplay ?? string.Empty)}</p>");
        sb.AppendLine($"    <p><span class=\"label\">תאריך:</span> {WebUtility.HtmlEncode(receivedAt.ToString("u"))}</p>");
        if (!string.IsNullOrWhiteSpace(internetMessageId))
        {
            sb.AppendLine($"    <div class=\"ids\">Message-ID: {WebUtility.HtmlEncode(internetMessageId)}</div>");
        }

        sb.AppendLine("  </div>");
        sb.AppendLine("  <div class=\"email-body\">");
        if (isPlainTextFallback)
        {
            sb.Append(WebUtility.HtmlEncode(bodyContent));
        }
        else
        {
            sb.Append(bodyContent);
        }

        sb.AppendLine();
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }
}
