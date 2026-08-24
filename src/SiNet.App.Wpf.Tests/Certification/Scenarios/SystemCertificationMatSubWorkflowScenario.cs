namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// MAT is exercised as a sub-workflow from PLN/REV parent scenarios rather than a standalone email start.
/// </summary>
internal sealed class SystemCertificationMatSubWorkflowScenario : ISystemCertificationScenario
{
    public const string Id = "cert.mat";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["MaterialIntake"];

    public ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Blocked($"{Id}.live", "MAT is covered via parent PLN/REV scenarios — not started yet.");
        return ValueTask.CompletedTask;
    }
}
