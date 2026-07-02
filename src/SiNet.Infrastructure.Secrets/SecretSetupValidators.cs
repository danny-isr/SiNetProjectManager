using System.DirectoryServices.AccountManagement;
using System.Net.Http.Headers;
using Microsoft.Data.SqlClient;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

internal static class SecretSetupValidators
{
    public static (bool Exists, bool Success, string? Detail) TestDatabaseFromVault(
        ISecretVaultStore vault,
        string secretKey)
    {
        if (!vault.HasSecret(secretKey))
        {
            return (false, false, null);
        }

        var connStr = vault.GetSecret(secretKey)!;
        if (TestConnectionString(connStr, out var error))
        {
            try
            {
                var csb = new SqlConnectionStringBuilder(connStr);
                return (true, true, $"{csb.DataSource}/{csb.InitialCatalog}");
            }
            catch
            {
                return (true, true, null);
            }
        }

        return (true, false, error);
    }

    public static async Task<(bool Exists, bool Success, string? Detail)> TestGeminiFromVaultAsync(
        ISecretVaultStore vault,
        CancellationToken cancellationToken)
    {
        if (!vault.HasSecret(SecretCatalog.GeminiApiKey))
        {
            return (false, false, null);
        }

        var apiKey = vault.GetSecret(SecretCatalog.GeminiApiKey)!;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}",
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (true, true, "Gemini API");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (true, false, $"HTTP {(int)response.StatusCode}: {Truncate(body, 150)}");
        }
        catch (Exception ex)
        {
            return (true, false, ex.Message);
        }
    }

    public static async Task<(bool BothExist, bool Success, string? Detail)> TestAutodeskFromVaultAsync(
        ISecretVaultStore vault,
        CancellationToken cancellationToken)
    {
        var hasId = vault.HasSecret(SecretCatalog.AutodeskClientId);
        var hasSecret = vault.HasSecret(SecretCatalog.AutodeskClientSecret);

        if (!hasId || !hasSecret)
        {
            var missing = !hasId && !hasSecret ? null
                : !hasId ? "חסר Client ID" : "חסר Client Secret";
            return (false, false, missing);
        }

        var clientId = vault.GetSecret(SecretCatalog.AutodeskClientId)!;
        var clientSecret = vault.GetSecret(SecretCatalog.AutodeskClientSecret)!;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var authBytes = System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://developer.api.autodesk.com/authentication/v2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(authBytes));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "data:read",
            });
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (true, true, "Autodesk APS");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (true, false, $"HTTP {(int)response.StatusCode}: {Truncate(body, 150)}");
        }
        catch (Exception ex)
        {
            return (true, false, ex.Message);
        }
    }

    public static (bool Exists, bool Success, string? Detail) TestGoogleFromVault(ISecretVaultStore vault)
    {
        if (!vault.HasSecret(SecretCatalog.GoogleClientSecrets))
        {
            return (false, false, null);
        }

        var json = vault.GetSecret(SecretCatalog.GoogleClientSecrets)!;
        var (success, detail) = GoogleClientSecretsValidator.ValidateJsonContent(json);
        return (true, success, detail);
    }

    public static (bool BothExist, bool Success, string? Detail) TestAdFromVault(
        ISecretVaultStore vault,
        ISecretSetupHostConfiguration hostConfiguration)
    {
        var hasUser = vault.HasSecret(SecretCatalog.AdUsername);
        var hasPass = vault.HasSecret(SecretCatalog.AdPassword);

        if (!hasUser || !hasPass)
        {
            var missing = !hasUser && !hasPass ? null
                : !hasUser ? "חסר שם משתמש" : "חסרה סיסמה";
            return (false, false, missing);
        }

        var username = vault.GetSecret(SecretCatalog.AdUsername)!;
        var password = vault.GetSecret(SecretCatalog.AdPassword)!;
        try
        {
            var domainName = hostConfiguration.ActiveDirectoryDomainName;
            using var context = !string.IsNullOrEmpty(domainName)
                ? new PrincipalContext(ContextType.Domain, domainName)
                : new PrincipalContext(ContextType.Domain);

            if (context.ValidateCredentials(username, password, ContextOptions.SimpleBind))
            {
                return (true, true, username);
            }

            return (true, false, "שם משתמש או סיסמה שגויים");
        }
        catch (PrincipalServerDownException)
        {
            return (true, false, "שרת ה-Domain לא זמין — ודא VPN פעיל");
        }
        catch (Exception ex)
        {
            return (true, false, ex.Message);
        }
    }

    public static (bool Exists, bool Success, string? Detail) TestPresenceOnly(
        ISecretVaultStore vault,
        string secretKey)
    {
        var exists = vault.HasSecret(secretKey);
        return (exists, exists, exists ? "מוגדר ב-Vault" : null);
    }

    private static bool TestConnectionString(string connectionString, out string? error)
    {
        error = null;
        try
        {
            var csb = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 5,
            };
            using var conn = new SqlConnection(csb.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
