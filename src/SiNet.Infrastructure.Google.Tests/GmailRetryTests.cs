using System.Net;
using Google;
using SiNet.Application.Abstractions.Logging;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

/// <summary>
/// Offline tests for the #7 Gmail read-path retry helper <see cref="GmailRetry"/>. A zero-delay
/// provider is injected so the backoff schedule does not slow the suite; the retry/rethrow logic is
/// what is under test. Synthetic <see cref="GoogleApiException"/> instances stand in for real API
/// failures (same construction pattern as <c>GmailSendErrorMappingTests</c>).
/// </summary>
public sealed class GmailRetryTests
{
    private static readonly Func<int, TimeSpan> NoDelay = _ => TimeSpan.Zero;

    private static GoogleApiException ApiException(HttpStatusCode status) =>
        new("gmail", "synthetic test error") { HttpStatusCode = status };

    [Fact]
    public async Task ExecuteAsync_retries_transient_failures_then_returns_value()
    {
        var attempts = 0;
        var result = await GmailRetry.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < GmailRetry.MaxAttempts)
                {
                    throw ApiException(HttpStatusCode.TooManyRequests);
                }

                return Task.FromResult(42);
            },
            new NullLogger(),
            "test",
            CancellationToken.None,
            NoDelay);

        Assert.Equal(42, result);
        Assert.Equal(GmailRetry.MaxAttempts, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_exhausts_retries_and_rethrows_last_transient()
    {
        var attempts = 0;
        var ex = await Assert.ThrowsAsync<GoogleApiException>(() =>
            GmailRetry.ExecuteAsync<int>(
                _ =>
                {
                    attempts++;
                    throw ApiException(HttpStatusCode.ServiceUnavailable);
                },
                new NullLogger(),
                "test",
                CancellationToken.None,
                NoDelay));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.HttpStatusCode);
        Assert.Equal(GmailRetry.MaxAttempts, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_retry_non_transient_failures()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<GoogleApiException>(() =>
            GmailRetry.ExecuteAsync<int>(
                _ =>
                {
                    attempts++;
                    throw ApiException(HttpStatusCode.BadRequest);
                },
                new NullLogger(),
                "test",
                CancellationToken.None,
                NoDelay));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_retry_on_cancellation()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            GmailRetry.ExecuteAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new OperationCanceledException();
                },
                new NullLogger(),
                "test",
                CancellationToken.None,
                NoDelay));

        Assert.Equal(1, attempts);
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
