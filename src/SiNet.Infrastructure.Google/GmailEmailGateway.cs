using System.Globalization;
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

        var summaries = new List<EmailSummary>();
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
                _logger.Error($"[Gmail] Messages.List failed for label '{labelPath}': {ex.Message}", ex);
                break;
            }

            if (listResponse.Messages == null || listResponse.Messages.Count == 0)
            {
                break;
            }

            foreach (var item in listResponse.Messages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var summary = await TryGetSummaryAsync(gmail, item.Id, cancellationToken).ConfigureAwait(false);
                if (summary != null)
                {
                    summaries.Add(summary);
                }
            }

            pageToken = listResponse.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

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
            hasAttachments);
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
}
