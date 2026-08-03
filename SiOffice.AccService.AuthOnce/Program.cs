using MyOffice.AutodeskConnector;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;

namespace SiOffice.AccService.AuthOnce;

/// <summary>
/// Interactive one-shot Autodesk 3-legged auth for the Windows account that hosts
/// AccService (typically SI-ENG\sieng). Writes refresh_token.json under that user's
/// %LOCALAPPDATA%\SiNet\Autodesk\. Invoked by Refresh-AccService-Token.cmd via runas.
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
        var tokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNet",
            "Autodesk");
        var tokenPath = Path.Combine(tokenDir, "refresh_token.json");

        Console.WriteLine("==============================================================");
        Console.WriteLine("  SiOffice AccService — Autodesk token (AuthOnce)");
        Console.WriteLine("==============================================================");
        Console.WriteLine($"Windows user : {Environment.UserDomainName}\\{Environment.UserName}");
        Console.WriteLine($"Token path   : {tokenPath}");
        Console.WriteLine($"Exists now   : {File.Exists(tokenPath)}");
        Console.WriteLine($"Force re-auth: {force}");
        Console.WriteLine();

        if (force && File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
            Console.WriteLine("Deleted existing refresh_token.json (--force).");
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
                "Import SiNet.secrets as SI-ENG\\sieng first (Server\\Install-Full.cmd / SecretImport).");
            Console.ResetColor();
            Pause();
            return 2;
        }

        Console.WriteLine("If needed, a browser will open for Autodesk login.");
        Console.WriteLine("Sign in with an ACC Account Admin user, then return here.");
        Console.WriteLine();

        try
        {
            var provider = new TokenProvider(clientId, clientSecret);
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
                Console.WriteLine($"OK — refresh token is on disk for {Environment.UserName}.");
                Console.WriteLine($"File: {tokenPath}");
                Console.ResetColor();
                Pause();
                return 0;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Access token obtained, but refresh_token.json was not found on disk.");
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
