using System.Text.Json;
using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationPreflightEvidenceTests
{
    [Fact]
    public void TryValidate_fails_when_path_is_missing()
    {
        var previous = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, null);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(out var path);

            Assert.NotNull(violation);
            Assert.Null(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previous);
        }
    }

    [Fact]
    public void TryValidate_accepts_certified_json()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sinet-cert-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "preflight.json");
        File.WriteAllText(
            file,
            JsonSerializer.Serialize(new { Verdict = SystemCertificationEvidence.CertifiedVerdict }));

        var previous = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, file);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(out var path);

            Assert.Null(violation);
            Assert.Equal(file, path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previous);
            Directory.Delete(directory, recursive: true);
        }
    }
}
