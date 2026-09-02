using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// OUT live certification. Writes only when <see cref="SystemCertificationEnvironment.OutLiveEnabledEnv"/>
/// is set and <see cref="SystemCertificationEnvironment.PreflightEvidenceEnv"/> points to a CERTIFIED
/// preflight report.
/// </summary>
[Collection(SystemCertificationTestCollection.Name)]
public sealed class SystemCertificationOutLiveTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SystemCertificationFact]
    public async Task Out_contract_a_through_production_seams()
    {
        var evidence = SystemCertificationEvidence.Create();
        evidence.Fact("Scenario", Scenarios.SystemCertificationOutScenario.Id);
        evidence.Declare(
            "cert.out.write_authorization",
            CertificationRequirement.Required,
            "Central write guard passes before OUT writes");

        var auth = await SystemCertificationHost.TryCreateAuthorizedWriteHostAsync(CancellationToken.None);
        if (auth.Violation is not null || auth.Host is null)
        {
            evidence.Fail("cert.out.write_authorization", auth.Violation ?? "write authorization failed");
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        evidence.Pass("cert.out.write_authorization", "write host authorized");

        await using var host = auth.Host;
        var scenario = new Scenarios.SystemCertificationOutScenario();
        await scenario.RunAsync(host, evidence, CancellationToken.None);

        Report(evidence);
        evidence.FinalizeCertification();
    }

    private void Report(SystemCertificationEvidence evidence) =>
        _output.WriteLine($"OUT certification: {evidence.Verdict}. Evidence: {evidence.MarkdownPath}");
}
