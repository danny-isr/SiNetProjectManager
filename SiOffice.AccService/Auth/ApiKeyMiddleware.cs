using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;
using SiNetSQL.Services.AccBootstrap.Contracts;

namespace SiOffice.AccService.Auth;

/// <summary>
/// Validates the <c>X-AccService-Key</c> header against the shared service API key.
/// Only the health endpoint (<c>/v1/acc/health</c>) is auth-exempt so load balancers
/// and uptime monitors can poll without the key. <c>/v1/acc/diag</c> requires the key.
/// </summary>
/// <remarks>
/// Resolution order — same vault → appsettings fallback as the WPF client uses
/// for every other secret:
/// <list type="number">
///   <item>Windows Credential Manager: <c>SiNet/AccService/ApiKey</c> (<see cref="SecretCatalog.AccServiceApiKey"/>)</item>
///   <item>appsettings: <c>AccService:ApiKey</c></item>
/// </list>
/// </remarks>
public sealed class ApiKeyMiddleware
{
    private static readonly string HeaderName = AccServiceContracts.ApiKeyHeader;
    private const string HealthPath = "/v1/acc/health";

    private readonly RequestDelegate _next;
    private readonly string? _expectedKey;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _expectedKey =
            CredentialVault.GetSecret(SecretCatalog.AccServiceApiKey)
            ?? configuration["AccService:ApiKey"];

        if (string.IsNullOrWhiteSpace(_expectedKey))
        {
            _logger.LogWarning(
                "AccService API key is not configured (vault key '{VaultKey}' or appsettings 'AccService:ApiKey'). " +
                "All non-health requests will be rejected with 401.",
                SecretCatalog.AccServiceApiKey);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Health endpoint is exempt from API key validation (uptime monitors).
        if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_expectedKey))
        {
            await WriteUnauthorizedAsync(context, "Service is not configured (missing API key).");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), _expectedKey))
        {
            _logger.LogWarning("Rejected request to {Path} from {Ip}: invalid or missing {Header}.",
                context.Request.Path, context.Connection.RemoteIpAddress, HeaderName);
            await WriteUnauthorizedAsync(context, $"Missing or invalid {HeaderName} header.");
            return;
        }

        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync($"{{\"error\":\"{message}\"}}");
    }

    /// <summary>
    /// Constant-time string compare to avoid timing side channels on key check.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
