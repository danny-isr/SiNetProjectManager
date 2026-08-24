using System.IO;
using System.Text.Json;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Validates that a saved preflight report exists and reached <see cref="SystemCertificationEvidence.CertifiedVerdict"/>.
/// PRP live writes must not start without this proof.
/// </summary>
internal static class SystemCertificationPreflightEvidence
{
    /// <summary>
    /// Returns a violation message when the preflight evidence gate fails; otherwise <see langword="null"/>.
    /// </summary>
    public static string? TryValidate(out string? evidencePath)
    {
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
            if (!document.RootElement.TryGetProperty("Verdict", out var verdictElement))
            {
                return "Preflight evidence JSON does not contain a Verdict property.";
            }

            var verdict = verdictElement.GetString();
            if (!string.Equals(verdict, SystemCertificationEvidence.CertifiedVerdict, StringComparison.Ordinal))
            {
                return $"Preflight evidence verdict is '{verdict ?? "<null>"}', not "
                       + $"'{SystemCertificationEvidence.CertifiedVerdict}'.";
            }
        }
        catch (JsonException ex)
        {
            return $"Preflight evidence file is not valid JSON: {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"Preflight evidence file could not be read: {ex.Message}";
        }

        return null;
    }
}
