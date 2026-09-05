using System.Net.Http.Headers;
using MyOffice.AutodeskConnector;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Light read-only Autodesk Admin API probe for AccService Admin identity health.
/// Endpoint: <c>GET /construction/admin/v1/accounts/{accountId}/projects?limit=1</c>.
/// </summary>
public static class AccServiceAdminApiProbe
{
    public const string RelativePathTemplate =
        "construction/admin/v1/accounts/{0}/projects?limit=1&offset=0";

    public const string AbsoluteUrlTemplate =
        "https://developer.api.autodesk.com/" + RelativePathTemplate;

    /// <summary>
    /// Returns <c>200</c>, <c>403</c>, or <c>unavailable:…</c>. Never returns token material.
    /// </summary>
    public static async Task<string> ProbeListProjectsAsync(
        ITokenProvider tokenProvider,
        string accountOrHubId,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        if (string.IsNullOrWhiteSpace(accountOrHubId))
        {
            return "unavailable:no-account";
        }

        var accountId = accountOrHubId.Trim();
        if (accountId.StartsWith("b.", StringComparison.OrdinalIgnoreCase))
        {
            accountId = accountId[2..];
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return "unavailable:no-account";
        }

        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetThreeLeggedAdminTokenAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"unavailable:token:{ex.GetType().Name}";
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return "unavailable:token-empty";
        }

        var url = string.Format(AbsoluteUrlTemplate, Uri.EscapeDataString(accountId));
        var ownsClient = httpClient is null;
        httpClient ??= new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (code is >= 200 and < 300)
            {
                return "200";
            }

            if (code == 403)
            {
                return "403";
            }

            return $"unavailable:HTTP {code}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"unavailable:{ex.GetType().Name}";
        }
        finally
        {
            if (ownsClient)
            {
                httpClient.Dispose();
            }
        }
    }
}
