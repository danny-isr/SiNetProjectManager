using System.Net;
using System.Net.Http;
using SiOffice.AccService.Contracts;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

[Trait("Category", LiveFactAttribute.Category)]
public sealed class AccServiceHealthLiveTests
{
    [LiveFact]
    public async Task WhenLiveEnabledThenHealthEndpointRespondsOkWithoutApiKey()
    {
        using var handler = CreateHandler();
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(LiveEnvironment.HealthUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("status", body, StringComparison.OrdinalIgnoreCase);
    }

    [LiveFact]
    public async Task WhenLiveEnabledThenDiagEndpointRequiresApiKeyAndSucceedsWithVaultKey()
    {
        var apiKey = LiveEnvironment.TryResolveAccApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Fail("Vault missing AccService API key — cannot call /v1/acc/diag.");
        }

        using var handler = CreateHandler();
        using var client = new HttpClient(handler);

        using (var unauthorized = await client.GetAsync(LiveEnvironment.DiagUrl))
        {
            Assert.True(
                unauthorized.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Expected 401/403 without key, got {(int)unauthorized.StatusCode}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, LiveEnvironment.DiagUrl);
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);
        using var authorized = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var body = await authorized.Content.ReadAsStringAsync();
        Assert.DoesNotContain(apiKey!, body, StringComparison.Ordinal);
    }

    private static HttpClientHandler CreateHandler() =>
        new()
        {
            // Local AccService uses a developer certificate.
            ServerCertificateCustomValidationCallback = static (_, _, _, _) => true,
        };
}
