using System.Net;
using Google;
using Google.Apis.Requests;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

/// <summary>
/// Deterministic, offline tests for the Gmail send error-classification helpers
/// (<see cref="GmailEmailSender.IsInsufficientScope"/> and <see cref="GmailEmailSender.IsTransient"/>).
/// These assert the mapping rules that drive <c>RequiresConsent</c> vs. retryable failures without
/// any Gmail API, OAuth, or network access: synthetic <see cref="GoogleApiException"/> instances are
/// constructed in-memory and passed straight to the classifiers.
/// </summary>
public sealed class GmailSendErrorMappingTests
{
    private static GoogleApiException ApiException(HttpStatusCode status, string? reason = null)
    {
        RequestError? error = null;
        if (reason is not null)
        {
            error = new RequestError
            {
                Errors = new List<SingleError> { new() { Reason = reason } },
            };
        }

        return new GoogleApiException("gmail", "synthetic test error")
        {
            HttpStatusCode = status,
            Error = error,
        };
    }

    [Theory]
    [InlineData("insufficientPermissions")]
    [InlineData("ACCESS_TOKEN_SCOPE_INSUFFICIENT")]
    [InlineData("INSUFFICIENTPERMISSIONS")] // case-insensitive
    public void IsInsufficientScope_Forbidden_WithScopeReason_IsTrue(string reason)
    {
        var ex = ApiException(HttpStatusCode.Forbidden, reason);
        Assert.True(GmailEmailSender.IsInsufficientScope(ex));
    }

    [Fact]
    public void IsInsufficientScope_Forbidden_WithUnrelatedReason_IsFalse()
    {
        var ex = ApiException(HttpStatusCode.Forbidden, "rateLimitExceeded");
        Assert.False(GmailEmailSender.IsInsufficientScope(ex));
    }

    [Fact]
    public void IsInsufficientScope_Forbidden_WithNoErrorDetail_IsFalse()
    {
        var ex = ApiException(HttpStatusCode.Forbidden);
        Assert.False(GmailEmailSender.IsInsufficientScope(ex));
    }

    [Fact]
    public void IsInsufficientScope_Unauthorized_WithInsufficientMessage_IsTrue()
    {
        var ex = new GoogleApiException("gmail", "Request had insufficient authentication scopes.")
        {
            HttpStatusCode = HttpStatusCode.Unauthorized,
        };

        Assert.True(GmailEmailSender.IsInsufficientScope(ex));
    }

    [Fact]
    public void IsInsufficientScope_Unauthorized_WithoutInsufficientMessage_IsFalse()
    {
        var ex = new GoogleApiException("gmail", "Invalid credentials")
        {
            HttpStatusCode = HttpStatusCode.Unauthorized,
        };

        Assert.False(GmailEmailSender.IsInsufficientScope(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void IsInsufficientScope_NonForbiddenStatuses_AreFalse(HttpStatusCode status)
    {
        var ex = ApiException(status, "insufficientPermissions");
        Assert.False(GmailEmailSender.IsInsufficientScope(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void IsTransient_RateLimitAndServerErrors_AreRetryable(HttpStatusCode status)
    {
        var ex = ApiException(status);
        Assert.True(GmailEmailSender.IsTransient(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void IsTransient_ClientErrors_AreNotRetryable(HttpStatusCode status)
    {
        var ex = ApiException(status);
        Assert.False(GmailEmailSender.IsTransient(ex));
    }
}
