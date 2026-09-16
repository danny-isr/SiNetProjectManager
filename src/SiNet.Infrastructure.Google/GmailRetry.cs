using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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
    internal const int HistoricalMaxAttempts = 6;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 16000;
    private const int JitterMaxMs = 500;

    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        IAppLogger logger,
        string context,
        CancellationToken cancellationToken,
        Func<int, TimeSpan>? delayProvider = null,
        int? maxAttempts = null,
        Action<GmailRetryDiagnostic>? onDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var attemptsAllowed = maxAttempts is > 0 ? maxAttempts.Value : MaxAttempts;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (global::Google.GoogleApiException ex) when (IsTransient(ex) && attempt < attemptsAllowed - 1)
            {
                var retryAfter = TryGetRetryAfter(ex);
                var delay = retryAfter ?? (delayProvider ?? ComputeDelay)(attempt);
                var reason = TryGetReason(ex);
                onDiagnostic?.Invoke(new GmailRetryDiagnostic(
                    (int)ex.HttpStatusCode,
                    reason,
                    retryAfter,
                    delay,
                    IsRateLimit(ex)));
                logger.Warn(
                    $"[Gmail] Transient error status={(int)ex.HttpStatusCode} reason={reason ?? "(none)"} " +
                    $"retryAfter={FormatRetryAfter(retryAfter)} on {context}; " +
                    $"retry {attempt + 1}/{attemptsAllowed - 1} in {delay.TotalMilliseconds:n0}ms: {ex.Message}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static bool IsTransient(global::Google.GoogleApiException ex) =>
        ex.HttpStatusCode == HttpStatusCode.TooManyRequests
        || (int)ex.HttpStatusCode >= 500
        || IsRateLimit(ex);

    internal static bool IsRateLimit(global::Google.GoogleApiException ex)
    {
        if (ex.HttpStatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return IsRateLimitReason(TryGetReason(ex)) || IsRateLimitReason(ex.Message);
    }

    internal static string? TryGetReason(global::Google.GoogleApiException ex)
    {
        var reason = ex.Error?.Errors?
            .Select(static error => error.Reason)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(reason) ? null : reason;
    }

    internal static TimeSpan? TryGetRetryAfter(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var text = ex.ToString();
        var match = Regex.Match(text, @"Retry-After:\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    internal static bool IsRateLimitReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase)
            || value.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase)
            || value.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tooManyRequests", StringComparison.OrdinalIgnoreCase)
            || value.Contains("backendError", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRetryAfter(TimeSpan? retryAfter)
        => retryAfter is { } value ? value.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) : "(none)";

    private static TimeSpan ComputeDelay(int attempt)
    {
        var backoff = Math.Min(BaseDelayMs * (1 << attempt), MaxDelayMs);
        var jitter = Random.Shared.Next(0, JitterMaxMs);
        return TimeSpan.FromMilliseconds(backoff + jitter);
    }
}

internal sealed record GmailRetryDiagnostic(
    int HttpStatus,
    string? GoogleReason,
    TimeSpan? RetryAfter,
    TimeSpan Delay,
    bool IsRateLimit);
