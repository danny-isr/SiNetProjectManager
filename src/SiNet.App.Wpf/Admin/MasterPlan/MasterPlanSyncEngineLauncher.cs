using System.Diagnostics;
using System.IO;
using System.Text;

namespace SiNet.App.Wpf.Admin.MasterPlan;

/// <summary>
/// Resolves and launches the published / local MasterPlan.SyncEngine.exe as a separate process.
/// SMO restore stays out of the WPF process (docs/MASTER_PLAN_MIGRATION.md, DEV-018).
/// </summary>
public static class MasterPlanSyncEngineLauncher
{
    public const string PublishedExePath =
        @"\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\MasterPlan.SyncEngine.exe";

    public static string? ResolveExecutablePath()
    {
        if (File.Exists(PublishedExePath))
        {
            return PublishedExePath;
        }

        var baseDir = AppContext.BaseDirectory;
        // Climb from App.Wpf bin toward the repo root looking for SyncEngine build output.
        for (var dir = new DirectoryInfo(baseDir); dir is not null; dir = dir.Parent)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName,
                    "MasterPlan.SyncEngine",
                    "bin",
                    config,
                    "net10.0",
                    "MasterPlan.SyncEngine.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                break;
            }
        }

        return null;
    }

    public static string BuildMonthlyArguments(string backupPath, bool allowOlderOrEqualBackup = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var args = $"--monthly --backup \"{backupPath}\"";
        if (allowOlderOrEqualBackup)
        {
            args += " --allow-older-backup";
        }

        return args;
    }

    public static async Task<(int ExitCode, string CombinedOutput)> RunMonthlyAsync(
        string exePath,
        string backupPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool allowOlderOrEqualBackup = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("MasterPlan.SyncEngine.exe לא נמצא.", exePath);
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("קובץ הגיבוי לא נמצא.", backupPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = BuildMonthlyArguments(backupPath, allowOlderOrEqualBackup),
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Append(string? line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(line);
            }

            progress?.Report(line);
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Exited += (_, _) =>
        {
            try
            {
                tcs.TrySetResult(process.ExitCode);
            }
            catch (InvalidOperationException)
            {
                tcs.TrySetResult(-1);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("הפעלת MasterPlan.SyncEngine נכשלה.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using (cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // already exited
            }

            tcs.TrySetCanceled(cancellationToken);
        }))
        {
            var exitCode = await tcs.Task.ConfigureAwait(false);
            // Drain a moment for late DataReceived callbacks.
            await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            string combined;
            lock (output)
            {
                combined = output.ToString();
            }

            return (exitCode, combined);
        }
    }
}
