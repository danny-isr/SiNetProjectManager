using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        CancellationToken cancellationToken)
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

        return await TestNetworkDiagnosticAsync(localKey, baseUrl, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AccServiceDiagnosticResultDto> TestNetworkDiagnosticAsync(
        string localKey,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var localHashPrefix = ComputeHashPrefix(localKey);
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, _, _, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None)
                    {
                        return true;
                    }

                    if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
                    {
                        var host = msg?.RequestUri?.Host;
                        return msg?.RequestUri?.IsLoopback == true || IsApprovedHost(host);
                    }

                    return false;
                },
            };

            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var diagUrl = baseUrl.TrimEnd('/') + "/v1/acc/diag";
            using var response = await httpClient.GetAsync(diagUrl, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
            var serverHashPrefix = root.TryGetProperty("keyHashPrefix", out var p) ? p.GetString() : "(none)";
            var keysMatch = localHashPrefix == serverHashPrefix && localHashPrefix != "(none)";

            if (keysMatch)
            {
                return new AccServiceDiagnosticResultDto(
                    true,
                    SecretStatusLevel.Valid,
                    "AccService — המפתחות תואמים (network diag).",
                    IsNetworkTest: true,
                    $"hash: {localHashPrefix}");
            }

            if (!serverHasKey)
            {
                return new AccServiceDiagnosticResultDto(
                    false,
                    SecretStatusLevel.Incomplete,
                    "AccService — מפתח קיים בלקוח, חסר בשרת.",
                    IsNetworkTest: true,
                    "server has no key");
            }

            return new AccServiceDiagnosticResultDto(
                false,
                SecretStatusLevel.Invalid,
                "AccService — hash mismatch.",
                IsNetworkTest: true,
                $"local={localHashPrefix}, server={serverHashPrefix}");
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

    private static string ComputeHashPrefix(string key)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
    }

    private static bool IsApprovedHost(string? host) =>
        !string.IsNullOrEmpty(host)
        && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("SI-WIN-2K19", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".si-eng.local", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("192.168.", StringComparison.Ordinal));

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
