using System.Diagnostics;
using System.IO;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Provisions secrets directly on the AccService host machine.
///
/// Account context — by design we write under the *current interactive user*.
/// Windows Credential Manager generic credentials are stored per Windows user,
/// and the AccService Windows service is configured to log on under that same
/// account, so a single vault namespace covers both sides. We deliberately do
/// NOT use schtasks /RU SYSTEM here, because that path fails with "Access
/// denied" unless the WPF app is running elevated, and routes secrets into the
/// SYSTEM vault — which is the wrong namespace for this deployment.
///
/// Workflow on the server (RDP session):
///   1. Operator opens the secrets dialog and fills the fields.
///   2. Clicks "save on server" — secrets are written to the current user's vault.
///   3. The Windows service is restarted so it re-reads the new values.
/// </summary>
public static class AccServiceLocalSystemProvisioner
{
    private const string ServiceName = "SiOfficeAccService";
    private const string DefaultExePath = @"C:\AccService\SiOffice.AccService.exe";

    public sealed record ProvisionResult(bool Success, string Output);

    /// <summary>Writes a single secret into the current user's vault, with round-trip verification.</summary>
    public static ProvisionResult ImportSecret(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        try
        {
            CredentialVaultService.SetSecret(key, value);

            // Verify round-trip — guards against silent vault failures (quota,
            // stale credential, ACL on the credential blob, etc.).
            var roundtrip = CredentialVaultService.GetSecret(key);
            if (!string.Equals(roundtrip, value, StringComparison.Ordinal))
            {
                return new ProvisionResult(false,
                    $"כתיבה נכשלה: לאחר SetSecret הקריאה החוזרת לא החזירה את הערך הצפוי עבור '{key}'.");
            }

            return new ProvisionResult(true, $"✅ {key}");
        }
        catch (Exception ex)
        {
            return new ProvisionResult(false, $"❌ {key}: {ex.Message}");
        }
    }

    /// <summary>Writes multiple secrets in a single batch.</summary>
    public static IReadOnlyList<(string Key, ProvisionResult Result)> ImportMany(
        IEnumerable<KeyValuePair<string, string>> secrets)
    {
        var results = new List<(string, ProvisionResult)>();
        foreach (var (key, value) in secrets)
            results.Add((key, ImportSecret(key, value)));
        return results;
    }

    /// <summary>
    /// Restarts the Windows service so it re-reads the secrets from the vault.
    /// Uses <c>net.exe stop/start</c> to avoid a NuGet dependency on
    /// System.ServiceProcess.ServiceController.
    /// </summary>
    public static string RestartService()
    {
        try
        {
            // 'net stop' may legitimately fail if already stopped — ignore exit code.
            RunNet($"stop {ServiceName}");

            var start = RunNet($"start {ServiceName}");
            if (start.ExitCode != 0)
                return $"❌ הפעלה מחדש של {ServiceName} נכשלה:\n{start.Output}\n\n" +
                       "טיפ: ייתכן שצריך להריץ את האפליקציה כמנהל (Run as administrator) " +
                       "כדי להפעיל מחדש את השירות.";

            return $"✅ {ServiceName} הופעל מחדש.";
        }
        catch (Exception ex)
        {
            return $"❌ הפעלה מחדש של {ServiceName} נכשלה: {ex.Message}";
        }
    }

    /// <summary>
    /// True if the AccService executable is found at the expected install path.
    /// Used to enable/disable the "save on server" UI on machines that are not
    /// the service host.
    /// </summary>
    public static bool IsAccServiceInstalledLocally(string? exePath = null) =>
        File.Exists(exePath ?? DefaultExePath);

    /// <summary>
    /// Returns the Windows account configured to run the SiOfficeAccService service
    /// (e.g. <c>DOMAIN\\User</c>, <c>LocalSystem</c>, <c>NT AUTHORITY\\NetworkService</c>).
    /// Used to detect the common misconfiguration where the service runs as
    /// LocalSystem but the secrets are written into an interactive user's vault.
    /// </summary>
    public static string? GetServiceLogonAccount()
    {
        try
        {
            // 'sc.exe qc' includes a 'SERVICE_START_NAME' line with the logon account.
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"qc {ServiceName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10_000);

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var label = line[..idx].Trim();
                if (label.Equals("SERVICE_START_NAME", StringComparison.OrdinalIgnoreCase))
                    return line[(idx + 1)..].Trim();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True if the service is configured to run as LocalSystem (the default for
    /// Windows services). Secrets written by an interactive user are NOT visible
    /// to a LocalSystem-hosted service, which produces the
    /// "AccService API key is not configured" warning even after a successful save.
    /// </summary>
    public static bool IsServiceRunningAsLocalSystem()
    {
        var account = GetServiceLogonAccount();
        if (string.IsNullOrEmpty(account)) return false;
        return account.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)
            || account.Equals(@"NT AUTHORITY\SYSTEM", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reconfigures the service to log on as the given Windows account (typically
    /// the current interactive user) so that the credential vault written by the
    /// WPF app is the same vault the service reads from.
    ///
    /// The account must already have the "Log on as a service" right (otherwise
    /// the service will fail to start). If the right is missing, the message in
    /// the result includes a hint about Local Security Policy.
    /// </summary>
    public static (bool Success, string Output) ConfigureServiceLogonAccount(
        string account, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // sc.exe config SiOfficeAccService obj= "DOMAIN\User" password= "..."
        // Note: sc.exe is unusual — it requires a SPACE after '=' in 'obj=' and 'password='.
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("config");
        psi.ArgumentList.Add(ServiceName);
        psi.ArgumentList.Add("obj=");
        psi.ArgumentList.Add(account);
        psi.ArgumentList.Add("password=");
        psi.ArgumentList.Add(password);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sc.exe");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(15_000);

        var combined = (stdout + stderr).Trim();
        if (p.ExitCode != 0)
        {
            return (false,
                $"sc.exe config נכשל (exit {p.ExitCode}):\n{combined}\n\n" +
                "טיפים:\n" +
                "• הרץ את האפליקציה כמנהל (Run as administrator).\n" +
                "• ודא שהמשתמש קיים ושהסיסמה נכונה.\n" +
                "• ודא שלמשתמש יש את ההרשאה 'Log on as a service'\n" +
                "  (secpol.msc → Local Policies → User Rights Assignment).");
        }

        return (true, combined);
    }

    private static (int ExitCode, string Output) RunNet(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "net.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start net.exe");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        return (p.ExitCode, (stdout + stderr).Trim());
    }
}
