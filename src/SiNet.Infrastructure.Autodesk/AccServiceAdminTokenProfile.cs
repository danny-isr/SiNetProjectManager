using System.Net.Http.Headers;
using System.Text.Json;
using MyOffice.AutodeskConnector;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>Safe Autodesk userinfo readback for AccService Admin token (never returns token values).</summary>
public static class AccServiceAdminTokenProfile
{
    public const string UserInfoUrl = "https://api.userprofile.autodesk.com/userinfo";

    public static async Task<AccServiceAdminTokenProfileResult> ResolveAsync(
        ITokenProvider tokenProvider,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);

        if (!tokenProvider.HasThreeLeggedRefreshToken)
        {
            return AccServiceAdminTokenProfileResult.TokenMissing();
        }

        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetThreeLeggedAdminTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AccServiceAdminTokenProfileResult.Unavailable($"token: {ex.GetType().Name}");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return AccServiceAdminTokenProfileResult.TokenMissing();
        }

        var ownsClient = httpClient is null;
        httpClient ??= new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return AccServiceAdminTokenProfileResult.Unavailable($"userinfo HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var email = GetString(root, "email");
            var userId = GetString(root, "sub") ?? GetString(root, "userId") ?? GetString(root, "userid");
            var name = GetString(root, "name") ?? GetString(root, "preferred_username");

            if (string.IsNullOrWhiteSpace(email))
            {
                return AccServiceAdminTokenProfileResult.Unavailable("userinfo missing email");
            }

            return new AccServiceAdminTokenProfileResult(
                TokenAvailable: true,
                ProfileResolved: true,
                Email: email.Trim(),
                AutodeskUserId: userId,
                DisplayName: name,
                FailureReason: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AccServiceAdminTokenProfileResult.Unavailable($"profile: {ex.GetType().Name}");
        }
        finally
        {
            if (ownsClient)
            {
                httpClient.Dispose();
            }
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

public sealed record AccServiceAdminTokenProfileResult(
    bool TokenAvailable,
    bool ProfileResolved,
    string? Email,
    string? AutodeskUserId,
    string? DisplayName,
    string? FailureReason)
{
    public static AccServiceAdminTokenProfileResult TokenMissing() =>
        new(false, false, null, null, null, "3-legged Admin refresh token is missing.");

    public static AccServiceAdminTokenProfileResult Unavailable(string reason) =>
        new(true, false, null, null, null, reason);
}
