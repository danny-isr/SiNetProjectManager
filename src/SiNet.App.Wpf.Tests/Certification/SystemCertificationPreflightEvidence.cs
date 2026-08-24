using System.IO;
using System.Text.Json;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Validates that a saved preflight report is <see cref="SystemCertificationEvidence.CertifiedVerdict"/>,
/// bound to the current runtime, and fresh enough for PRP live writes.
/// </summary>
internal static class SystemCertificationPreflightEvidence
{
    /// <summary>
    /// Returns a violation message when the preflight evidence gate fails; otherwise <see langword="null"/>.
    /// </summary>
    public static string? TryValidate(
        SystemCertificationEnvironment.Target target,
        SystemCertificationEnvironment.GmailLayer gmail,
        SystemCertificationEnvironment.AccLayer acc,
        out string? evidencePath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(gmail);
        ArgumentNullException.ThrowIfNull(acc);

        evidencePath = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        if (string.IsNullOrWhiteSpace(evidencePath))
        {
            return $"{SystemCertificationEnvironment.PreflightEvidenceEnv} must point to a saved preflight "
                   + "evidence JSON from a CERTIFIED DEV read-only preflight run.";
        }

        evidencePath = evidencePath.Trim();
        if (!File.Exists(evidencePath))
        {
            return $"Preflight evidence file not found: '{evidencePath}'.";
        }

        try
        {
            using var stream = File.OpenRead(evidencePath);
            using var document = JsonDocument.Parse(stream);

            if (!SystemCertificationPreflightBinding.TryParse(document, out var binding, out var parseError))
            {
                return parseError;
            }

            var commitResolution = SystemCertificationGitMetadata.ResolveHeadCommitSha();
            if (commitResolution.Violation is not null)
            {
                return commitResolution.Violation;
            }

            var runtimeViolation = binding!.ValidateAgainstCurrentRuntime(
                target,
                gmail,
                acc,
                commitResolution.Sha);

            return runtimeViolation;
        }
        catch (JsonException ex)
        {
            return $"Preflight evidence file is not valid JSON: {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"Preflight evidence file could not be read: {ex.Message}";
        }
    }
}
