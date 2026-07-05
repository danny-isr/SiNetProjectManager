using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Workflow transition action code mapping for dev seed (subset of legacy ActionDefinitionRegistry).
/// </summary>
internal static class WorkflowTransitionActionCodeMapper
{
    public static string MapFromWorkflowTransitionActionType(WorkflowTransitionActionType type) => type switch
    {
        WorkflowTransitionActionType.CreateStageTasks => "CreateStageTasks",
        WorkflowTransitionActionType.ClosePreviousStageTasks => "ClosePreviousStageTasks",
        WorkflowTransitionActionType.SendNotification => "SendNotification",
        WorkflowTransitionActionType.StartSubWorkflow => "StartSubWorkflow",
        WorkflowTransitionActionType.SetProjectStatus => "SetProjectStatus",
        WorkflowTransitionActionType.RecordTaskResult => "RecordTaskResult",
        WorkflowTransitionActionType.SetBillingPending => "SetBillingPending",
        WorkflowTransitionActionType.CloseProject => "CloseProject",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped WorkflowTransitionActionType."),
    };
}

/// <summary>Suggested-action codes referenced by workflow seed data.</summary>
internal static class DevToolsSuggestedActionCodes
{
    public const string ApproveOrClose = nameof(ApproveOrClose);
}
