namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Runnable certification scenario. Registry disposition (<see cref="ScenarioDisposition.ScenarioRequired"/>)
/// names which workflows must have an implementation; the runtime result after
/// <see cref="RunAsync"/> is recorded separately in <see cref="SystemCertificationEvidence"/>.
/// </summary>
internal interface ISystemCertificationScenario
{
    string ScenarioId { get; }

    IReadOnlyList<string> WorkflowDefinitionCodes { get; }

    ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default);
}
