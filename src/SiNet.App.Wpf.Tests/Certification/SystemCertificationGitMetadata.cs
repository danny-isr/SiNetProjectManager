using System.ComponentModel;
using System.Diagnostics;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>Resolves repository metadata used to bind preflight evidence to a specific commit.</summary>
internal static class SystemCertificationGitMetadata
{
    public const string CommitShaEnv = "SINET_SYSTEM_CERT_COMMIT_SHA";

    internal sealed record CommitShaResolution(string? Sha, string? Violation);

    public static CommitShaResolution ResolveHeadCommitSha()
    {
        var gitSha = TryGitRevParseHead();
        var envSha = ReadTrimmedEnv(CommitShaEnv);

        if (gitSha is not null)
        {
            if (envSha is not null)
            {
                if (!IsFullGitSha(envSha))
                {
                    return new CommitShaResolution(
                        null,
                        $"{CommitShaEnv} must be a full 40-character hex SHA when git is available; got '{envSha}'.");
                }

                if (!string.Equals(envSha, gitSha, StringComparison.OrdinalIgnoreCase))
                {
                    return new CommitShaResolution(
                        null,
                        $"{CommitShaEnv} '{envSha}' does not match git HEAD '{gitSha}'.");
                }
            }

            return new CommitShaResolution(gitSha, null);
        }

        if (string.IsNullOrWhiteSpace(envSha))
        {
            return new CommitShaResolution(
                null,
                $"git HEAD unavailable and {CommitShaEnv} is not set.");
        }

        if (!IsFullGitSha(envSha))
        {
            return new CommitShaResolution(
                null,
                $"{CommitShaEnv} must be a full 40-character hex SHA; got '{envSha}'.");
        }

        return new CommitShaResolution(envSha, null);
    }

    public static string? TryResolveHeadCommitSha()
    {
        var resolution = ResolveHeadCommitSha();
        return resolution.Violation is null ? resolution.Sha : null;
    }

    internal static bool IsFullGitSha(string sha) =>
        sha.Length == 40 && sha.All(static c => Uri.IsHexDigit(c));

    private static string? ReadTrimmedEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TryGitRevParseHead()
    {
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
            return process.ExitCode == 0 && IsFullGitSha(output) ? output : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
