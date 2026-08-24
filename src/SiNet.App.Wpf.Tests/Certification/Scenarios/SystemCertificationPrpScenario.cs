namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// PRP certification scenario. Registered and linked from the registry; live writes are gated separately
/// and must not run until DEV Preflight PASS and operator approval.
/// </summary>
internal sealed class SystemCertificationPrpScenario : ISystemCertificationScenario
{
    public const string Id = "cert.prp";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["Proposal"];

    public ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);

        // Live PRP writes are intentionally not started in this slice. The scenario exists so coverage
        // proves a runnable implementation is linked before any write tier is switched on.
        evidence.Blocked(
            $"{Id}.live",
            "PRP live writes are not started until DEV Preflight PASS and operator approval.");
        return ValueTask.CompletedTask;
    }
}
