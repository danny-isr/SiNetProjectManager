using System.Net;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Retry/backoff helper for transient Gmail API failures (HTTP 429 / 5xx). The Gmail read path
/// historically made a single <c>ExecuteAsync</c> call and, on any failure, only logged and
/// returned an empty/null fallback — so a momentary rate-limit or server blip surfaced as "no
/// mail". This helper retries transient failures with exponential backoff + jitter before letting
/// the final exception propagate to the caller's existing fallback handling.
/// <para>
/// Non-transient errors (4xx other than 429) are never retried. <see cref="OperationCanceledException"/>
/// is never caught, so cancellation stays immediate.
/// </para>
/// </summary>
internal static class GmailRetry
{
    /// <summary>Total attempts including the first try (mirrors the legacy throttle service).</summary>
    internal const int MaxAttempts = 4;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 16000;
    private const int JitterMaxMs = 500;

    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        IAppLogger logger,
        string context,
        CancellationToken cancellationToken,
        Func<int, TimeSpan>? delayProvider = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (global::Google.GoogleApiException ex) when (IsTransient(ex) && attempt < MaxAttempts - 1)
            {
                var delay = (delayProvider ?? ComputeDelay)(attempt);
                logger.Warn(
                    $"[Gmail] Transient error {(int)ex.HttpStatusCode} on {context}; " +
                    $"retry {attempt + 1}/{MaxAttempts - 1} in {delay.TotalMilliseconds:n0}ms: {ex.Message}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Transient = rate limited (429) or a server-side 5xx; safe to retry.</summary>
    internal static bool IsTransient(global::Google.GoogleApiException ex) =>
        ex.HttpStatusCode == HttpStatusCode.TooManyRequests
        || (int)ex.HttpStatusCode >= 500;

    private static TimeSpan ComputeDelay(int attempt)
    {
        var backoff = Math.Min(BaseDelayMs * (1 << attempt), MaxDelayMs);
        var jitter = Random.Shared.Next(0, JitterMaxMs);
        return TimeSpan.FromMilliseconds(backoff + jitter);
    }
}
