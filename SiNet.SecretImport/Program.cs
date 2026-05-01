using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;

namespace SiNet.SecretImport;

/// <summary>
/// Tiny CLI that imports a SiNet.secrets file into Windows Credential Manager
/// of the CURRENT Windows user. Designed to be copied to the server and run
/// once under the same account that will host the AccService Windows Service
/// (or the MasterPlan.SyncEngine scheduled task).
///
/// Usage:
///   SiNet.SecretImport.exe import &lt;path-to-.secrets&gt; [password]
///   SiNet.SecretImport.exe status
///   SiNet.SecretImport.exe whoami
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

            return command switch
            {
                "import" => RunImport(args),
                "status" => RunStatus(),
                "whoami" => RunWhoAmI(),
                _ => PrintHelp()
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.ResetColor();
            return 2;
        }
    }

    private static int RunImport(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: SiNet.SecretImport.exe import <path-to-.secrets> [password]");
            return 1;
        }

        var filePath = args[1];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        if (!SecretProvisioningService.IsProvisioningFile(filePath))
        {
            Console.Error.WriteLine("File is not a valid SiNet provisioning file (bad magic header).");
            return 1;
        }

        var password = args.Length >= 3 ? args[2] : PromptPassword("Enter package password: ");
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("Password is required.");
            return 1;
        }

        PrintWhoAmI();
        Console.WriteLine($"Importing secrets from: {filePath}");

        var imported = SecretProvisioningService.ImportFromFile(filePath, password);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"OK: {imported} secret(s) imported into Windows Credential Manager " +
                          $"of user '{Environment.UserDomainName}\\{Environment.UserName}'.");
        Console.ResetColor();

        Console.WriteLine();
        PrintStatus();
        return 0;
    }

    private static int RunStatus()
    {
        PrintWhoAmI();
        PrintStatus();
        return 0;
    }

    private static int RunWhoAmI()
    {
        PrintWhoAmI();
        return 0;
    }

    private static void PrintWhoAmI()
    {
        Console.WriteLine("==============================================================");
        Console.WriteLine($" Current Windows user : {Environment.UserDomainName}\\{Environment.UserName}");
        Console.WriteLine($" Machine              : {Environment.MachineName}");
        Console.WriteLine(" Vault scope          : per-user (DPAPI). Secrets written here");
        Console.WriteLine("                        are visible ONLY to this user account.");
        Console.WriteLine("==============================================================");
        Console.WriteLine();
    }

    private static void PrintStatus()
    {
        Console.WriteLine("Vault status:");
        var status = CredentialVaultService.GetVaultStatus();
        foreach (var (key, present) in status)
        {
            Console.ForegroundColor = present ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.WriteLine($"  [{(present ? "X" : " ")}] {key}");
            Console.ResetColor();
        }
    }

    private static string PromptPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
        return sb.ToString();
    }

    private static int PrintHelp()
    {
        Console.WriteLine("SiNet.SecretImport - portable Credential Manager provisioner");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  import <file.secrets> [password]   Import secrets into the current user's vault.");
        Console.WriteLine("  status                             Show which secrets are present in the vault.");
        Console.WriteLine("  whoami                             Show which Windows account this process runs as.");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Windows Credential Manager is per-user. Run this tool under the SAME");
        Console.WriteLine("    account that will host the Windows Service / Scheduled Task.");
        Console.WriteLine("  - If you run as Administrator via 'Run as different user', confirm with 'whoami'.");
        return 0;
    }
}
