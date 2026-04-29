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
