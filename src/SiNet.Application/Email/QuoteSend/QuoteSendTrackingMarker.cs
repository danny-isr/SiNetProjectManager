using SiNet.Application.Abstractions.Email;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// Builds a unique tracking token embedded in quote-send compose subject/body so Sent can be verified.
/// Format: <c>SINET-QS-{instanceId}-{token}</c>.
/// </summary>
public static class QuoteSendTrackingMarker
{
    public const string Prefix = "SINET-QS-";

    public static string Create(int workflowInstanceId, string? token = null)
    {
        if (workflowInstanceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(workflowInstanceId), workflowInstanceId, "Instance id must be positive.");

        var t = string.IsNullOrWhiteSpace(token)
            ? Guid.NewGuid().ToString("N")[..12]
            : token.Trim();

        return $"{Prefix}{workflowInstanceId}-{t}";
    }

    public static bool LooksLikeMarker(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains(Prefix, StringComparison.OrdinalIgnoreCase);

    public static EmailMailboxQuery BuildSentSearchQuery(string marker, int pageSize = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        return new EmailMailboxQuery
        {
            MailboxScope = EmailMailboxScope.Sent,
            FreeText = marker.Trim(),
            PageSize = pageSize,
        };
    }
}
