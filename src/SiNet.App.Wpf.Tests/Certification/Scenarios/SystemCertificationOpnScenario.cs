namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

internal sealed class SystemCertificationOpnScenario : ISystemCertificationScenario
{
    public const string Id = "cert.opn";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["Opinion"];

    public ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Blocked($"{Id}.live", "OPN live scenario not started yet.");
        return ValueTask.CompletedTask;
    }
}
