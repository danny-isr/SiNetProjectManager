using System.Diagnostics;
using System.IO;
using System.Text;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Provisions secrets into the LocalSystem credential vault on the AccService host
/// machine, by invoking <c>SiOffice.AccService.exe --import-secret</c> through a
/// one-shot scheduled task that runs as <c>NT AUTHORITY\SYSTEM</c>.
///
/// This is the only reliable way to write into the same vault namespace that the
/// Windows service reads from at runtime, because Windows Credential Manager is
/// scoped per logon session — running <c>cmdkey</c> as the interactive admin
/// stores the secret under the wrong account.
///
/// Designed to run on the AccService host itself (admin RDPs into the server,
/// runs the WPF client there, fills in the secrets, clicks "save to LocalSystem").
/// </summary>
public static class AccServiceLocalSystemProvisioner
{
    private const string ServiceName = "SiOfficeAccService";
    private const string DefaultExePath = @"C:\AccService\SiOffice.AccService.exe";
    private const string TaskName = "SiOfficeAccService_ProvisionSecret";

    public sealed record ProvisionResult(bool Success, string Output);

    /// <summary>
    /// Writes a single secret into the LocalSystem vault on this machine.
    /// </summary>
    public static ProvisionResult ImportSecret(string key, string value, string? exePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var exe = exePath ?? DefaultExePath;
        if (!File.Exists(exe))
            return new ProvisionResult(false,
                $"לא נמצא קובץ ההפעלה של השירות בנתיב: {exe}\n" +
                "ודא שה-AccService מותקן במחשב הזה (C:\\AccService).");

        // Base64-encode the value so it cannot collide with task-scheduler argument
        // parsing (slashes, spaces, equals signs, quotes, etc.).
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        var sb = new StringBuilder();
        try
        {
            // 1. Create the one-shot task as SYSTEM, ready to run on demand.
            //    /F overwrites any leftover task from a previous failed run.
            var createArgs =
                $"/Create /F /TN \"{TaskName}\" /SC ONCE /ST 23:59 " +
                $"/RU \"NT AUTHORITY\\SYSTEM\" /RL HIGHEST " +
                $"/TR \"\\\"{exe}\\\" --import-secret {key} {b64}\"";
            var createOut = RunSchtasks(createArgs);
            sb.AppendLine(createOut);

            // 2. Run it now.
            var runOut = RunSchtasks($"/Run /TN \"{TaskName}\"");
            sb.AppendLine(runOut);

            // 3. Wait for it to finish (poll Last Result).
            var finalResult = WaitForTaskCompletion(TaskName, TimeSpan.FromSeconds(15));
            sb.AppendLine(finalResult.Log);

            // 4. Always clean up the task so leftover entries don't accumulate.
            try { RunSchtasks($"/Delete /F /TN \"{TaskName}\""); } catch { /* best-effort */ }

            return new ProvisionResult(finalResult.Success, sb.ToString());
        }
        catch (Exception ex)
        {
            sb.AppendLine($"שגיאה: {ex.Message}");
            return new ProvisionResult(false, sb.ToString());
        }
    }

    /// <summary>
    /// Imports multiple secrets in a single batch. Returns the per-key results.
    /// </summary>
    public static IReadOnlyList<(string Key, ProvisionResult Result)> ImportMany(
        IEnumerable<KeyValuePair<string, string>> secrets, string? exePath = null)
    {
        var results = new List<(string, ProvisionResult)>();
        foreach (var (key, value) in secrets)
        {
            results.Add((key, ImportSecret(key, value, exePath)));
        }
        return results;
    }

    /// <summary>
    /// Restarts the Windows service so it re-reads the secrets from the vault.
    /// Uses <c>net.exe stop/start</c> to avoid a NuGet dependency on
    /// System.ServiceProcess.ServiceController. Returns a human-readable status line.
    /// </summary>
    public static string RestartService()
    {
        try
        {
            var stop = RunNet($"stop {ServiceName}");
            // 'net stop' fails if already stopped — that's fine, ignore its exit code.

            var start = RunNet($"start {ServiceName}");
            if (start.ExitCode != 0)
                return $"❌ הפעלה מחדש של {ServiceName} נכשלה:\n{start.Output}";

            return $"✅ {ServiceName} הופעל מחדש.";
        }
        catch (Exception ex)
        {
            return $"❌ הפעלה מחדש של {ServiceName} נכשלה: {ex.Message}";
        }
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

    /// <summary>
    /// True if the AccService executable is found at the expected install path.
    /// Use this to enable/disable the "save to server" UI on machines that are
    /// not the service host.
    /// </summary>
    public static bool IsAccServiceInstalledLocally(string? exePath = null) =>
        File.Exists(exePath ?? DefaultExePath);

    // ─── helpers ──────────────────────────────────────────────────────────

    private static string RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start schtasks.exe");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);

        var combined = (stdout + stderr).Trim();
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"schtasks {arguments} failed (exit {p.ExitCode}): {combined}");
        return combined;
    }

    private sealed record TaskCompletion(bool Success, string Log);

    private static TaskCompletion WaitForTaskCompletion(string taskName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string lastQuery = "";
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
            lastQuery = SafeRunSchtasks($"/Query /TN \"{taskName}\" /V /FO LIST");

            // 'Last Result' line — the import process exit code we wrote in Program.cs.
            // 0 = success, anything else = failure. While the task is running the
            // value is 267009 (SCHED_S_TASK_RUNNING).
            var lastResult = ExtractLastResult(lastQuery);
            if (lastResult is null) continue;

            if (lastResult == 0)
                return new TaskCompletion(true, lastQuery);

            // Task scheduler 'still running' codes — keep polling.
            if (lastResult == 267009) continue;

            return new TaskCompletion(false, lastQuery);
        }
        return new TaskCompletion(false, "פג זמן ההמתנה לסיום המשימה.\n" + lastQuery);
    }

    private static string SafeRunSchtasks(string arguments)
    {
        try { return RunSchtasks(arguments); }
        catch (Exception ex) { return ex.Message; }
    }

    private static int? ExtractLastResult(string queryOutput)
    {
        // Locale-tolerant: match either 'Last Result' (en-US) or its he-IL equivalent.
        foreach (var line in queryOutput.Split('\n', StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var label = line[..idx];
            if (!label.Contains("Last Result", StringComparison.OrdinalIgnoreCase) &&
                !label.Contains("תוצאה אחרונה")) continue;

            var value = line[(idx + 1)..].Trim();
            if (int.TryParse(value, out var n)) return n;
        }
        return null;
    }
}
