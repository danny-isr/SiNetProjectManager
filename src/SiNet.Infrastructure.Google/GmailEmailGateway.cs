using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Domain.ValueObjects;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Native Gmail implementation of <see cref="IEmailGateway"/>. Reads project-scoped email
/// summaries directly from the Gmail API via <see cref="GmailClientProvider"/>, with no WPF or
/// legacy <c>GoogleService</c> dependency.
/// <para>
/// Mailbox layout mirrors the existing system: project emails are filed under the Gmail label
/// <c>{root}/{location}/{projectName}</c>. When the mailbox is unavailable (not signed in),
/// reads return empty / <c>null</c> rather than throwing, per the <see cref="IEmailGateway"/> contract.
/// </para>
/// </summary>
public sealed class GmailEmailGateway : IEmailGateway
{
    private const int PageSize = 100;
    private static readonly string[] MetadataHeaders = { "Subject", "From", "Date", "Message-ID" };

    private readonly GmailClientProvider _provider;
    private readonly IAppLogger _logger;

    public GmailEmailGateway(GmailClientProvider provider, IAppLogger logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
        string location,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return Array.Empty<EmailSummary>();
        }

        var labelPath = $"{_provider.RootLabel}/{location}/{projectName}";

        var labelId = await ResolveLabelIdAsync(gmail, labelPath, cancellationToken).ConfigureAwait(false);
        if (labelId == null)
        {
            _logger.Warn($"[Gmail] Label not found: {labelPath}");
            return Array.Empty<EmailSummary>();
        }

        return await GetSummariesForLabelIdsAsync(gmail, [labelId], labelPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
        string projectLabelName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectLabelName))
        {
            return Array.Empty<EmailSummary>();
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return Array.Empty<EmailSummary>();
        }

        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var rootPrefix = _provider.RootLabel + "/";
        var labelIds = labels.Labels?
            .Where(static l => !string.IsNullOrWhiteSpace(l.Name) && !string.IsNullOrWhiteSpace(l.Id))
            .Where(l => l.Name!.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(l =>
            {
                var parts = l.Name!.Split('/');
                return parts.Length >= 2
                    && string.Equals(parts[^1], projectLabelName.Trim(), StringComparison.OrdinalIgnoreCase);
            })
            .Select(l => l.Id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (labelIds is null || labelIds.Length == 0)
        {
            _logger.Warn($"[Gmail] No project labels found for '{projectLabelName}'.");
            return Array.Empty<EmailSummary>();
        }

        return await GetSummariesForLabelIdsAsync(gmail, labelIds, projectLabelName.Trim(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<EmailSummary>> GetSummariesForLabelIdsAsync(
        GmailService gmail,
        IReadOnlyCollection<string> labelIds,
        string logLabel,
        CancellationToken cancellationToken)
    {
        var summaries = new List<EmailSummary>();
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var labelId in labelIds)
        {
            string? pageToken = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var listRequest = gmail.Users.Messages.List("me");
                listRequest.LabelIds = new[] { labelId };
                listRequest.MaxResults = PageSize;
                listRequest.PageToken = pageToken;

                ListMessagesResponse listResponse;
                try
                {
                    listResponse = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Gmail] Messages.List failed for label '{logLabel}': {ex.Message}", ex);
                    break;
                }

                if (listResponse.Messages == null || listResponse.Messages.Count == 0)
                {
                    break;
                }

                foreach (var item in listResponse.Messages)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(item.Id) || !seenMessageIds.Add(item.Id))
                    {
                        continue;
                    }

                    var summary = await TryGetSummaryAsync(gmail, item.Id, cancellationToken).ConfigureAwait(false);
                    if (summary != null)
                    {
                        summaries.Add(summary);
                    }
                }

                pageToken = listResponse.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        return summaries
            .OrderByDescending(e => e.ReceivedAt)
            .ToList();
    }

    public async Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return null;
        }

        return await TryGetSummaryAsync(gmail, messageId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return null;
        }

        try
        {
            var getRequest = gmail.Users.Messages.Get("me", messageId);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

            var message = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return MapDetails(message);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Gmail] Messages.Get(full) failed for id '{messageId}': {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> ResolveLabelIdAsync(
        GmailService gmail,
        string labelPath,
        CancellationToken cancellationToken)
    {
        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var match = labels.Labels?.FirstOrDefault(
            l => string.Equals(l.Name, labelPath, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    private async Task<EmailSummary?> TryGetSummaryAsync(
        GmailService gmail,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var getRequest = gmail.Users.Messages.Get("me", messageId);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getRequest.MetadataHeaders = MetadataHeaders;

            var message = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Map(message);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Gmail] Messages.Get failed for id '{messageId}': {ex.Message}");
            return null;
        }
    }

    private EmailSummary Map(Message message)
    {
        var headers = message.Payload?.Headers;

        var fromRaw = GetHeader(headers, "From");
        if (!EmailAddress.TryParse(fromRaw, out var from))
        {
            _logger.Warn($"[Gmail] Unparsable From header on message '{message.Id}': '{fromRaw}'");
            from = EmailAddress.CreateOrFallback(fromRaw);
        }

        var subject = GetHeader(headers, "Subject") ?? string.Empty;
        var receivedAt = ResolveReceivedAt(message, GetHeader(headers, "Date"));
        var hasAttachments = HasAttachments(message.Payload);

        return new EmailSummary(
            message.Id ?? string.Empty,
            message.ThreadId ?? string.Empty,
            from,
            subject,
            receivedAt,
            hasAttachments,
            GetHeader(headers, "Message-ID"));
    }

    private EmailMessageDetails MapDetails(Message message)
    {
        var summary = Map(message);
        var bodyText = ExtractBodyText(message.Payload);
        var attachments = ExtractAttachmentDetails(message.Payload);

        return new EmailMessageDetails(
            summary.MessageId,
            summary.ThreadId,
            summary.From,
            summary.Subject,
            summary.ReceivedAt,
            bodyText,
            attachments);
    }

    private static string? GetHeader(IList<MessagePartHeader>? headers, string name)
        => headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTimeOffset ResolveReceivedAt(Message message, string? dateHeader)
    {
        // Prefer the server-side internal date (epoch millis) when present; fall back to the
        // RFC 2822 Date header.
        if (message.InternalDate is long millis && millis > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis);
        }

        if (!string.IsNullOrWhiteSpace(dateHeader) &&
            DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.MinValue;
    }

    private static bool HasAttachments(MessagePart? payload)
    {
        // Best-effort from the metadata payload: a real attachment is a part with a filename and
        // an attachment body id. Inline images (signatures/logos) are intentionally not counted.
        if (payload?.Parts == null)
        {
            return false;
        }

        return AnyAttachmentPart(payload.Parts);
    }

    private static bool AnyAttachmentPart(IList<MessagePart> parts)
    {
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part.Filename) && part.Body?.AttachmentId != null)
            {
                return true;
            }

            if (part.Parts != null && AnyAttachmentPart(part.Parts))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractBodyText(MessagePart? payload)
    {
        if (payload == null)
        {
            return string.Empty;
        }

        string? plainBody = null;
        string? htmlBody = null;
        ExtractBodiesRecursive(payload, ref plainBody, ref htmlBody);

        if (!string.IsNullOrWhiteSpace(plainBody))
        {
            return plainBody.Trim();
        }

        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            return StripHtml(htmlBody);
        }

        return string.Empty;
    }

    private static void ExtractBodiesRecursive(MessagePart part, ref string? plainBody, ref string? htmlBody)
    {
        var mimeType = part.MimeType?.ToLowerInvariant() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(part.Body?.Data))
        {
            if (mimeType == "text/plain" && string.IsNullOrWhiteSpace(plainBody))
            {
                plainBody = DecodeBase64UrlSafe(part.Body.Data);
            }
            else if (mimeType == "text/html" && string.IsNullOrWhiteSpace(htmlBody))
            {
                htmlBody = DecodeBase64UrlSafe(part.Body.Data);
            }
        }

        if (part.Parts == null)
        {
            return;
        }

        foreach (var nested in part.Parts)
        {
            ExtractBodiesRecursive(nested, ref plainBody, ref htmlBody);
        }
    }

    private static IReadOnlyList<EmailMessageAttachmentDetails> ExtractAttachmentDetails(MessagePart? payload)
    {
        if (payload == null)
        {
            return [];
        }

        var attachments = new List<EmailMessageAttachmentDetails>();
        CollectAttachmentsRecursive(payload, attachments);
        return attachments;
    }

    private static void CollectAttachmentsRecursive(
        MessagePart part,
        ICollection<EmailMessageAttachmentDetails> attachments)
    {
        var filename = ResolveFileName(part);
        if (!string.IsNullOrWhiteSpace(filename) && part.Body?.AttachmentId is { Length: > 0 } attachmentId)
        {
            if (!IsInlineAttachment(part))
            {
                attachments.Add(new EmailMessageAttachmentDetails(
                    attachmentId,
                    filename,
                    string.IsNullOrWhiteSpace(part.MimeType) ? "application/octet-stream" : part.MimeType!,
                    part.Body.Size));
            }
        }

        if (part.Parts == null)
        {
            return;
        }

        foreach (var nested in part.Parts)
        {
            CollectAttachmentsRecursive(nested, attachments);
        }
    }

    private static string ResolveFileName(MessagePart part)
    {
        if (!string.IsNullOrWhiteSpace(part.Filename))
        {
            return part.Filename;
        }

        var disposition = GetHeader(part.Headers, "Content-Disposition");
        if (string.IsNullOrWhiteSpace(disposition))
        {
            return string.Empty;
        }

        var match = Regex.Match(disposition, "filename=\"?([^\"]+)\"?", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static bool IsInlineAttachment(MessagePart part)
    {
        var disposition = GetHeader(part.Headers, "Content-Disposition") ?? string.Empty;
        var contentId = GetHeader(part.Headers, "Content-ID");
        var mimeType = part.MimeType ?? string.Empty;

        if (disposition.Contains("inline", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(contentId)
            && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeBase64UrlSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0)
            {
                base64 += new string('=', 4 - padding);
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);

        normalized = Regex.Replace(normalized, "<[^>]+>", " ");
        normalized = WebUtility.HtmlDecode(normalized);
        normalized = Regex.Replace(normalized, @"[ \t]+\n", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        return normalized.Trim();
    }
}
