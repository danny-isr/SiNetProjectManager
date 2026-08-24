using System.ComponentModel;
using System.Diagnostics;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>Resolves repository metadata used to bind preflight evidence to a specific commit.</summary>
internal static class SystemCertificationGitMetadata
{
    public const string CommitShaEnv = "SINET_SYSTEM_CERT_COMMIT_SHA";

    public static string? TryResolveHeadCommitSha()
    {
        var fromEnv = Environment.GetEnvironmentVariable(CommitShaEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
