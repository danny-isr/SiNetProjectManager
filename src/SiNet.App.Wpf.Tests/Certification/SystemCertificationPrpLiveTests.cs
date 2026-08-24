using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// PRP live certification. Writes only when <see cref="SystemCertificationEnvironment.PrpLiveEnabledEnv"/>
/// is set and <see cref="SystemCertificationEnvironment.PreflightEvidenceEnv"/> points to a CERTIFIED
/// preflight report.
/// </summary>
[Collection(SystemCertificationTestCollection.Name)]
public sealed class SystemCertificationPrpLiveTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SystemCertificationFact]
    public async Task Prp_full_corridor_through_production_seams()
    {
        var evidence = SystemCertificationEvidence.Create();
        evidence.Fact("Scenario", Scenarios.SystemCertificationPrpScenario.Id);
        evidence.Declare(
            "cert.prp.write_authorization",
            CertificationRequirement.Required,
            "Central write guard passes before PRP writes");

        var auth = await SystemCertificationHost.TryCreateAuthorizedWriteHostAsync(CancellationToken.None);
        if (auth.Violation is not null || auth.Host is null)
        {
            evidence.Fail("cert.prp.write_authorization", auth.Violation ?? "write authorization failed");
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        evidence.Pass("cert.prp.write_authorization", "write host authorized");

        await using var host = auth.Host;
        var scenario = new Scenarios.SystemCertificationPrpScenario();
        await scenario.RunAsync(host, evidence, CancellationToken.None);

        Report(evidence);
        evidence.FinalizeCertification();
    }

    private void Report(SystemCertificationEvidence evidence) =>
        _output.WriteLine($"PRP certification: {evidence.Verdict}. Evidence: {evidence.MarkdownPath}");
}
