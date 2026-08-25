using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// MAT-via-PLN live certification. Writes only when
/// <see cref="SystemCertificationEnvironment.MatLiveEnabledEnv"/> is set and
/// <see cref="SystemCertificationEnvironment.PreflightEvidenceEnv"/> points to a CERTIFIED
/// preflight report.
/// </summary>
[Collection(SystemCertificationTestCollection.Name)]
public sealed class SystemCertificationMatLiveTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SystemCertificationFact]
    public async Task Mat_via_pln_corridor_through_production_seams()
    {
        var evidence = SystemCertificationEvidence.Create();
        evidence.Fact("Scenario", Scenarios.SystemCertificationMatSubWorkflowScenario.Id);
        evidence.Declare(
            "cert.mat.write_authorization",
            CertificationRequirement.Required,
            "Central write guard passes before MAT writes");

        var auth = await SystemCertificationHost.TryCreateAuthorizedWriteHostAsync(CancellationToken.None);
        if (auth.Violation is not null || auth.Host is null)
        {
            evidence.Fail("cert.mat.write_authorization", auth.Violation ?? "write authorization failed");
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        evidence.Pass("cert.mat.write_authorization", "write host authorized");

        await using var host = auth.Host;
        var scenario = new Scenarios.SystemCertificationMatSubWorkflowScenario();
        await scenario.RunAsync(host, evidence, CancellationToken.None);

        Report(evidence);
        evidence.FinalizeCertification();
    }

    private void Report(SystemCertificationEvidence evidence) =>
        _output.WriteLine($"MAT certification: {evidence.Verdict}. Evidence: {evidence.MarkdownPath}");
}
