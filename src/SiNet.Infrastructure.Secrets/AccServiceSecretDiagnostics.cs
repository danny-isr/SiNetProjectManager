using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

internal static class AccServiceSecretDiagnostics
{
    public static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public static async Task<AccServiceDiagnosticResultDto> TestAsync(
        ISecretVaultStore vault,
        ISecretSetupHostConfiguration hostConfiguration,
        IReadOnlyList<string>? pinnedCertificateThumbprints = null,
        CancellationToken cancellationToken = default)
    {
        if (!vault.HasSecret(SecretCatalog.AccServiceApiKey))
        {
            return new AccServiceDiagnosticResultDto(
                false,
                SecretStatusLevel.Missing,
                "AccService API Key חסר ב-Vault.",
                IsNetworkTest: false,
                "לא הוגדר מפתח");
        }

        var localKey = vault.GetSecret(SecretCatalog.AccServiceApiKey)!;
        if (string.IsNullOrWhiteSpace(localKey))
        {
            return new AccServiceDiagnosticResultDto(
                false,
                SecretStatusLevel.Invalid,
                "AccService API Key ריק.",
                IsNetworkTest: false,
                "ערך ריק");
        }

        if (localKey.Length < 16)
        {
            return new AccServiceDiagnosticResultDto(
                false,
                SecretStatusLevel.Invalid,
                "AccService API Key קצר מדי (presence/format).",
                IsNetworkTest: false,
                "אורך מפתח לא תקין");
        }

        var baseUrl = hostConfiguration.AccServiceBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new AccServiceDiagnosticResultDto(
                true,
                SecretStatusLevel.Valid,
                "AccService API Key קיים (בדיקת presence/format בלבד — AccService:BaseUrl לא מוגדר).",
                IsNetworkTest: false,
                "מוגדר ב-Vault");
        }

        return await TestNetworkDiagnosticAsync(
            localKey,
            baseUrl,
            pinnedCertificateThumbprints ?? [],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AccServiceDiagnosticResultDto> TestNetworkDiagnosticAsync(
        string localKey,
        string baseUrl,
        IReadOnlyList<string> pinnedCertificateThumbprints,
        CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, certificate, _, errors) =>
                    ValidateServerCertificate(msg, certificate, errors, pinnedCertificateThumbprints),
            };

            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var diagUrl = baseUrl.TrimEnd('/') + "/v1/acc/diag";
            using var request = new HttpRequestMessage(HttpMethod.Get, diagUrl);
            request.Headers.Add("X-AccService-Key", localKey);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new AccServiceDiagnosticResultDto(
                    false,
                    SecretStatusLevel.Invalid,
                    "AccService — המפתח בלקוח נדחה על ידי השרת (401).",
                    IsNetworkTest: true,
                    "HTTP 401");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AccServiceDiagnosticResultDto(
                    false,
                    SecretStatusLevel.Invalid,
                    $"AccService diag HTTP {(int)response.StatusCode}",
                    IsNetworkTest: true,
                    Truncate(body, 200));
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var serverHasKey = root.TryGetProperty("hasApiKey", out var h) && h.GetBoolean();
            var keySource = root.TryGetProperty("keySource", out var s) ? s.GetString() : "(unknown)";

            if (!serverHasKey)
            {
                return new AccServiceDiagnosticResultDto(
                    false,
                    SecretStatusLevel.Incomplete,
                    "AccService — מפתח תקף בלקוח, חסר בשרת.",
                    IsNetworkTest: true,
                    $"server keySource={keySource}");
            }

            return new AccServiceDiagnosticResultDto(
                true,
                SecretStatusLevel.Valid,
                "AccService — אימות מפתח הצליח (network diag).",
                IsNetworkTest: true,
                $"keySource={keySource}");
        }
        catch (Exception ex)
        {
            return new AccServiceDiagnosticResultDto(
                false,
                SecretStatusLevel.Invalid,
                "AccService network test failed.",
                IsNetworkTest: true,
                ex.Message);
        }
    }

    private static bool ValidateServerCertificate(
        HttpRequestMessage? message,
        X509Certificate2? certificate,
        SslPolicyErrors errors,
        IReadOnlyList<string> pinnedCertificateThumbprints)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors == SslPolicyErrors.RemoteCertificateChainErrors)
        {
            if (message?.RequestUri?.IsLoopback == true)
            {
                return true;
            }

            return IsPinnedThumbprint(certificate, pinnedCertificateThumbprints);
        }

        return false;
    }

    private static bool IsPinnedThumbprint(
        X509Certificate2? certificate,
        IReadOnlyList<string> pinnedCertificateThumbprints)
    {
        if (certificate is null || pinnedCertificateThumbprints.Count == 0)
        {
            return false;
        }

        var serverThumbprint = NormalizeThumbprint(certificate.Thumbprint);
        return pinnedCertificateThumbprints.Any(pin =>
            string.Equals(NormalizeThumbprint(pin), serverThumbprint, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeThumbprint(string? thumbprint) =>
        thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
