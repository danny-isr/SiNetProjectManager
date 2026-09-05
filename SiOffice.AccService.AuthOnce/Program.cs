using MyOffice.AutodeskConnector;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;

namespace SiOffice.AccService.AuthOnce;

/// <summary>
/// Interactive one-shot Autodesk 3-legged auth for AccService Admin.
/// Writes exclusively to the dedicated AccService token store:
/// %LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json
/// Never writes the desktop/user-context store under %LOCALAPPDATA%\SiNet\Autodesk\.
/// </summary>
internal static class Program
{
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
        var tokenDir = AutodeskTokenStorePaths.GetDefaultDirectory(AutodeskTokenStorePurpose.AccServiceAdmin);
        var tokenPath = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.AccServiceAdmin);
        var desktopTokenPath = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.UserContext);

        Console.WriteLine("==============================================================");
        Console.WriteLine("  SiOffice AccService — Autodesk Admin token (AuthOnce)");
        Console.WriteLine("==============================================================");
        Console.WriteLine($"Windows user     : {Environment.UserDomainName}\\{Environment.UserName}");
        Console.WriteLine($"Token purpose    : {AutodeskTokenStorePurpose.AccServiceAdmin}");
        Console.WriteLine($"AccService path  : {tokenPath}");
        Console.WriteLine($"Exists now       : {File.Exists(tokenPath)}");
        Console.WriteLine($"Desktop path     : {desktopTokenPath} (NOT written by AuthOnce)");
        Console.WriteLine($"Force re-auth    : {force}");
        Console.WriteLine();
        Console.WriteLine("Sign in as AccBootstrapAdminEmail (steady-state: siad@si-eng.co.il).");
        Console.WriteLine("If the browser is already signed in as another Autodesk user, sign out first.");
        Console.WriteLine();

        if (force && File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
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
            Console.Error.WriteLine(
                "Import SiNet.secrets for this Windows account first (Server\\Install-Full.cmd / SecretImport).");
            Console.ResetColor();
            Pause();
            return 2;
        }

        Console.WriteLine("If needed, a browser will open for Autodesk login.");
        Console.WriteLine();

        try
        {
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
                    $"FAIL: TokenProvider path mismatch. Expected AccService store:{Environment.NewLine}" +
                    $"  {tokenPath}{Environment.NewLine}" +
                    $"Actual:{Environment.NewLine}" +
                    $"  {provider.ThreeLeggedRefreshTokenStoragePath}");
                Console.ResetColor();
                Pause();
                return 1;
            }

            var accessToken = await provider.GetThreeLeggedAdminTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Auth finished but access token was empty.");
                Console.ResetColor();
                Pause();
                return 1;
            }

            var exists = File.Exists(tokenPath);
            Console.WriteLine();
            if (exists)
            {
                Directory.CreateDirectory(tokenDir);
                var okMarker = Path.Combine(tokenDir, "auth_once_last_ok.txt");
                await File.WriteAllTextAsync(okMarker, DateTime.UtcNow.ToString("o")).ConfigureAwait(false);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"OK — AccService Admin refresh token is on disk for {Environment.UserName}.");
                Console.WriteLine($"File: {tokenPath}");
                Console.WriteLine("Desktop token store was not modified.");
                Console.ResetColor();
                Pause();
                return 0;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Access token obtained, but AccService refresh_token.json was not found on disk.");
            Console.WriteLine("Check TokenProvider logs above.");
            Console.ResetColor();
            Pause();
            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            Console.ResetColor();
            Pause();
            return 1;
        }
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Press Enter to close...");
        try { Console.ReadLine(); } catch { /* non-interactive */ }
    }
}
