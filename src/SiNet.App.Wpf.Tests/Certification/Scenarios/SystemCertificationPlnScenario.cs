namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

internal sealed class SystemCertificationPlnScenario : ISystemCertificationScenario
{
    public const string Id = "cert.pln";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["PlanningWorkflow"];

    public ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Blocked($"{Id}.live", "PLN live scenario not started yet.");
        return ValueTask.CompletedTask;
    }
}
