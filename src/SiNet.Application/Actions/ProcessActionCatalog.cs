namespace SiNet.Application.Actions;

/// <summary>
/// Foundation action catalog for the Application layer. Heavy/legacy actions remain in SiNetSQL
/// until explicitly migrated.
/// </summary>
public static class ProcessActionCatalog
{
    private static readonly IReadOnlyList<ActionDefinitionDto> FoundationActions =
    [
        new(ProcessActionCodes.SendNotification, "WorkflowTransition", IsFoundationReady: true),
        new(ProcessActionCodes.SetProjectStatus, "WorkflowTransition", IsFoundationReady: true),
        new(ProcessActionCodes.RecordTaskResult, "WorkflowTransition", IsFoundationReady: true),
        new(ProcessActionCodes.CreateStageTasks, "WorkflowTransition", IsFoundationReady: true,
            Notes: "Marker/no-op — task provisioning remains orchestrator-owned."),
        new(ProcessActionCodes.SetBillingPending, "WorkflowTransition", IsFoundationReady: true),
    ];

    public static IReadOnlyList<ActionDefinitionDto> Foundation => FoundationActions;

    public static bool IsFoundationAction(string actionCode)
        => FoundationActions.Any(a => string.Equals(a.Code, actionCode, StringComparison.Ordinal));
}
