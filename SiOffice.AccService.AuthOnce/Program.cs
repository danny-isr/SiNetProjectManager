using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyOffice.AutodeskConnector;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;

namespace SiOffice.AccService.AuthOnce;

/// <summary>
/// Interactive one-shot Autodesk 3-legged auth for AccService Admin.
/// Writes exclusively to the dedicated AccService token store and verifies the
/// Autodesk profile email against AccBootstrapAdminEmail (default: siad@si-eng.co.il).
/// </summary>
internal static class Program
{
    private const string DefaultExpectedAdminEmail = "siad@si-eng.co.il";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        TokenProvider.LogInfo = msg => Console.WriteLine(msg);
        TokenProvider.LogWarn = msg =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
            Console.ResetColor();
        };
        TokenProvider.LogError = msg =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(msg);
            Console.ResetColor();
        };

        var force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
        var verifyOnly = args.Any(a => string.Equals(a, "--verify", StringComparison.OrdinalIgnoreCase));
        var noPause = args.Any(a => string.Equals(a, "--no-pause", StringComparison.OrdinalIgnoreCase));
        var expectedAdmin = ResolveExpectedEmail(args);

        var tokenDir = AutodeskTokenStorePaths.GetDefaultDirectory(AutodeskTokenStorePurpose.AccServiceAdmin);
        var tokenPath = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.AccServiceAdmin);
        var desktopTokenPath = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.UserContext);
        var identityPath = Path.Combine(tokenDir, "token_identity.txt");

        Console.WriteLine("==============================================================");
        Console.WriteLine("  SiOffice AccService — Autodesk Admin token (AuthOnce)");
        Console.WriteLine("==============================================================");
        Console.WriteLine($"Windows user     : {Environment.UserDomainName}\\{Environment.UserName}");
        Console.WriteLine($"Token purpose    : {AutodeskTokenStorePurpose.AccServiceAdmin}");
        Console.WriteLine($"AccService path  : {tokenPath}");
        Console.WriteLine($"Exists now       : {File.Exists(tokenPath)}");
        Console.WriteLine($"Desktop path     : {desktopTokenPath} (NOT written by AuthOnce)");
        Console.WriteLine($"Expected Admin   : {expectedAdmin}");
        Console.WriteLine($"Mode             : {(verifyOnly ? "verify-only" : force ? "force-auth" : "auth-if-needed")}");
        Console.WriteLine();

        if (!verifyOnly)
        {
            Console.WriteLine("Sign in as AccBootstrapAdminEmail.");
            Console.WriteLine("If the browser is already signed in as another Autodesk user, sign out first.");
            Console.WriteLine();
        }

        if (!verifyOnly && force && File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
            if (File.Exists(identityPath))
            {
                File.Delete(identityPath);
            }

            Console.WriteLine("Deleted existing AccService refresh_token.json (--force).");
            Console.WriteLine();
        }

        var clientId = CredentialVault.GetSecret(SecretCatalog.AutodeskClientId);
        var clientSecret = CredentialVault.GetSecret(SecretCatalog.AutodeskClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(
                "Autodesk ClientId/Secret are missing from THIS user's Windows Credential Manager.");
            Console.ResetColor();
            Pause(noPause);
            return 2;
        }

        try
        {
            if (verifyOnly && !File.Exists(tokenPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"FAIL: AccService token missing at {tokenPath}");
                Console.ResetColor();
                Pause(noPause);
                return 1;
            }

            var provider = new TokenProvider(
                clientId,
                clientSecret,
                AutodeskTokenStoreOptions.AccServiceAdmin);

            if (!string.Equals(
                    provider.ThreeLeggedRefreshTokenStoragePath,
                    tokenPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(
                    $"FAIL: TokenProvider path mismatch.{Environment.NewLine}" +
                    $"Expected: {tokenPath}{Environment.NewLine}" +
                    $"Actual:   {provider.ThreeLeggedRefreshTokenStoragePath}");
                Console.ResetColor();
                Pause(noPause);
                return 1;
            }

            if (provider.TokenStorePurpose != AutodeskTokenStorePurpose.AccServiceAdmin)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("FAIL: TokenPurpose is not AccServiceAdmin.");
                Console.ResetColor();
                Pause(noPause);
                return 1;
            }

            var accessToken = await provider.GetThreeLeggedAdminTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Auth finished but access token was empty.");
                Console.ResetColor();
                Pause(noPause);
                return 1;
            }

            if (!File.Exists(tokenPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Access token obtained, but AccService refresh_token.json was not found on disk.");
                Console.ResetColor();
                Pause(noPause);
                return 1;
            }

            var profile = await ResolveUserInfoAsync(accessToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(profile.Email))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"FAIL: Autodesk profile email unavailable ({profile.Detail}).");
                Console.ResetColor();
                Pause(noPause);
                return 1;
            }

            var emailMatch = string.Equals(
                expectedAdmin.Trim(),
                profile.Email.Trim(),
                StringComparison.OrdinalIgnoreCase);

            WriteIdentitySidecar(
                identityPath,
                expectedAdmin,
                profile.Email,
                profile.UserId,
                tokenPath);

            Console.WriteLine();
            Console.WriteLine($"Actual Admin     : {profile.Email}");
            Console.WriteLine($"Autodesk UserId  : {profile.UserId ?? "(n/a)"}");
            Console.WriteLine($"EmailMatch       : {emailMatch}");
            Console.WriteLine($"Identity sidecar : {identityPath}");

            if (!emailMatch)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine();
                Console.Error.WriteLine("FAIL: AccService token identity mismatch.");
                Console.Error.WriteLine($"Expected: {expectedAdmin}");
                Console.Error.WriteLine($"Actual:   {profile.Email}");
                Console.Error.WriteLine("Do NOT export this token. Sign in as the configured Admin and retry.");
                Console.ResetColor();
                Pause(noPause);
                return 3;
            }

            Directory.CreateDirectory(tokenDir);
            var okMarker = Path.Combine(tokenDir, "auth_once_last_ok.txt");
            await File.WriteAllTextAsync(okMarker, DateTime.UtcNow.ToString("o")).ConfigureAwait(false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine($"OK — AccService Admin token verified for {Environment.UserName}.");
            Console.WriteLine($"File: {tokenPath}");
            Console.WriteLine("Desktop token store was not modified.");
            Console.ResetColor();
            Pause(noPause);
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            Console.ResetColor();
            Pause(noPause);
            return 1;
        }
    }

    private static string ResolveExpectedEmail(string[] args)
    {
        foreach (var arg in args)
        {
            const string prefix = "--expected-email=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[prefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return DefaultExpectedAdminEmail;
    }

    private static void WriteIdentitySidecar(
        string identityPath,
        string expected,
        string actual,
        string? userId,
        string tokenPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(identityPath)!);
        var lines = new List<string>
        {
            "TokenPurpose=AccServiceAdmin",
            $"ExpectedAdminEmail={expected.Trim()}",
            $"ActualAdminEmail={actual.Trim()}",
            $"ExportedUtc={DateTime.UtcNow:o}",
            $"SourceMachine={Environment.MachineName}",
            $"SourcePath={tokenPath}",
        };
        if (!string.IsNullOrWhiteSpace(userId))
        {
            lines.Insert(3, $"AutodeskUserId={userId.Trim()}");
        }

        File.WriteAllLines(identityPath, lines);
    }

    private static async Task<(string? Email, string? UserId, string Detail)> ResolveUserInfoAsync(string accessToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.userprofile.autodesk.com/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return (null, null, $"userinfo HTTP {(int)resp.StatusCode}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? Get(string name) =>
                root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;

            var email = Get("email");
            var userId = Get("sub") ?? Get("userId") ?? Get("userid");
            if (string.IsNullOrWhiteSpace(email))
            {
                var m = Regex.Match(body, "\"email\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success)
                {
                    email = m.Groups[1].Value;
                }
            }

            return (email, userId, "OK");
        }
        catch (Exception ex)
        {
            return (null, null, ex.GetType().Name);
        }
    }

    private static void Pause(bool noPause)
    {
        if (noPause)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Press Enter to close...");
        try { Console.ReadLine(); } catch { /* non-interactive */ }
    }
}
