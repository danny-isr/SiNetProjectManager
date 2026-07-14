using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Native Gmail implementation of <see cref="IEmailSender"/>. Builds an RFC 5322 MIME message and
/// submits it via <c>users.messages.send</c> through the shared <see cref="GmailClientProvider"/>,
/// with no WPF or legacy <c>GoogleService</c> dependency.
/// <para>
/// Non-throwing for expected failures: when the mailbox is unavailable (not signed in) it returns
/// a failed result; when the session lacks the send scope (a token persisted before the scope
/// expansion) the Gmail API returns an insufficient-permission error, which is mapped to
/// <see cref="EmailSendResult.ConsentRequired"/> so the caller can trigger a deliberate interactive
/// re-consent.
/// </para>
/// </summary>
public sealed class GmailEmailSender : IEmailSender
{
    private const string DefaultAttachmentContentType = "application/octet-stream";

    private readonly GmailClientProvider _provider;
    private readonly IAppLogger _logger;

    public GmailEmailSender(GmailClientProvider provider, IAppLogger logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.To is null || request.To.Count == 0)
        {
            return EmailSendResult.Fail("At least one 'To' recipient is required.");
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return EmailSendResult.Fail("Gmail mailbox is not available (not signed in).");
        }

        string raw;
        try
        {
            raw = BuildRawMessage(request, boundary: null);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Gmail] Failed to build outbound message: {ex.Message}", ex);
            return EmailSendResult.Fail($"Failed to build message: {ex.Message}");
        }

        var message = new Message { Raw = raw };
        if (!string.IsNullOrWhiteSpace(request.ThreadId))
        {
            message.ThreadId = request.ThreadId;
        }

        try
        {
            var sent = await gmail.Users.Messages
                .Send(message, "me")
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.Info($"[Gmail] Sent message {sent.Id}.");
            return EmailSendResult.Sent(sent.Id);
        }
        catch (global::Google.GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            _logger.Warn("[Gmail] Send failed: the current session lacks the send scope. Interactive re-consent required.");
            return EmailSendResult.ConsentRequired(
                "The Google session does not include send permission yet. Sign in again to grant send access.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (global::Google.GoogleApiException ex)
        {
            _logger.Error($"[Gmail] Send failed: {ex.Message}", ex);
            return EmailSendResult.Fail($"Gmail send failed: {ex.Message}", shouldRetry: IsTransient(ex));
        }
        catch (Exception ex)
        {
            _logger.Error($"[Gmail] Send failed: {ex.Message}", ex);
            return EmailSendResult.Fail($"Gmail send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A 403 with an insufficient-permission/scope reason indicates the token was granted before
    /// the send scope was added. Treated as "needs interactive re-consent" rather than a hard error.
    /// </summary>
    internal static bool IsInsufficientScope(global::Google.GoogleApiException ex)
    {
        if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason;
            if (string.Equals(reason, "insufficientPermissions", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "ACCESS_TOKEN_SCOPE_INSUFFICIENT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized
            && (ex.Message?.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    internal static bool IsTransient(global::Google.GoogleApiException ex) =>
        GmailRetry.IsTransient(ex);

    /// <summary>
    /// Builds an RFC 5322 message and returns it base64url-encoded as required by the Gmail API.
    /// Produces a single text part when there are no attachments, or a <c>multipart/mixed</c>
    /// envelope otherwise. Non-ASCII headers use RFC 2047 encoded-words; bodies/attachments are
    /// base64 transfer-encoded.
    /// <para>
    /// <paramref name="boundary"/> is for deterministic testing only: when <see langword="null"/>
    /// (production) a fresh random MIME boundary is generated; tests may inject a fixed value to
    /// assert exact multipart output.
    /// </para>
    /// </summary>
    internal static string BuildRawMessage(EmailSendRequest request, string? boundary = null)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.From))
        {
            sb.Append("From: ").Append(EncodeHeader(request.From!)).Append("\r\n");
        }

        sb.Append("To: ").Append(EncodeAddressList(request.To)).Append("\r\n");
        if (request.Cc.Count > 0)
        {
            sb.Append("Cc: ").Append(EncodeAddressList(request.Cc)).Append("\r\n");
        }
        if (request.Bcc.Count > 0)
        {
            sb.Append("Bcc: ").Append(EncodeAddressList(request.Bcc)).Append("\r\n");
        }

        sb.Append("Subject: ").Append(EncodeHeader(request.Subject ?? string.Empty)).Append("\r\n");

        if (!string.IsNullOrWhiteSpace(request.InReplyToMessageId))
        {
            var id = EnsureAngleBrackets(request.InReplyToMessageId!);
            sb.Append("In-Reply-To: ").Append(id).Append("\r\n");
            sb.Append("References: ").Append(id).Append("\r\n");
        }

        sb.Append("MIME-Version: 1.0\r\n");

        var bodyContentType = request.IsHtml ? "text/html" : "text/plain";

        if (request.Attachments is null || request.Attachments.Count == 0)
        {
            sb.Append("Content-Type: ").Append(bodyContentType).Append("; charset=\"UTF-8\"\r\n");
            sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
            sb.Append(ChunkBase64(Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Body ?? string.Empty))));
        }
        else
        {
            boundary ??= "==SiNet_" + Guid.NewGuid().ToString("N") + "==";
            sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n\r\n");

            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Type: ").Append(bodyContentType).Append("; charset=\"UTF-8\"\r\n");
            sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
            sb.Append(ChunkBase64(Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Body ?? string.Empty))));
            sb.Append("\r\n");

            foreach (var attachment in request.Attachments)
            {
                var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? DefaultAttachmentContentType
                    : attachment.ContentType!;
                var fileName = EncodeHeader(attachment.FileName ?? "attachment");

                sb.Append("--").Append(boundary).Append("\r\n");
                sb.Append("Content-Type: ").Append(contentType).Append("; name=\"").Append(fileName).Append("\"\r\n");
                sb.Append("Content-Disposition: attachment; filename=\"").Append(fileName).Append("\"\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                sb.Append(ChunkBase64(Convert.ToBase64String(attachment.Content.Span)));
                sb.Append("\r\n");
            }

            sb.Append("--").Append(boundary).Append("--");
        }

        return Base64UrlEncode(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    internal static string EncodeAddressList(IReadOnlyList<string> addresses) =>
        string.Join(", ", addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Select(EncodeHeader));

    /// <summary>
    /// RFC 2047 encoded-word for headers containing non-ASCII characters; pass-through otherwise so
    /// plain ASCII addresses/subjects stay human-readable.
    /// </summary>
    internal static string EncodeHeader(string value)
    {
        if (string.IsNullOrEmpty(value) || IsAscii(value))
        {
            return value;
        }

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return $"=?UTF-8?B?{encoded}?=";
    }

    internal static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 127)
            {
                return false;
            }
        }

        return true;
    }

    internal static string EnsureAngleBrackets(string messageId)
    {
        var trimmed = messageId.Trim();
        if (!trimmed.StartsWith('<'))
        {
            trimmed = "<" + trimmed;
        }
        if (!trimmed.EndsWith('>'))
        {
            trimmed += ">";
        }

        return trimmed;
    }

    /// <summary>Wraps a base64 payload at 76 characters per line per MIME conventions.</summary>
    internal static string ChunkBase64(string base64)
    {
        const int lineLength = 76;
        if (base64.Length <= lineLength)
        {
            return base64;
        }

        var sb = new StringBuilder(base64.Length + (base64.Length / lineLength * 2));
        for (var i = 0; i < base64.Length; i += lineLength)
        {
            sb.Append(base64, i, Math.Min(lineLength, base64.Length - i));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Base64url encoding (RFC 4648 §5) without padding, as required by the Gmail API.</summary>
    internal static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
