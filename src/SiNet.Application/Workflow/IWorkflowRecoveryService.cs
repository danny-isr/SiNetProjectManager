namespace SiNet.Application.Workflow;

/// <summary>
/// Detects active workflow instances whose current stage has no open tasks and attempts their
/// safe recovery through the workflow command port.
/// </summary>
public interface IWorkflowRecoveryService
{
    ValueTask<IReadOnlyList<WorkflowRecoveryCandidate>> DetectStalledAsync(CancellationToken ct);

    ValueTask<int> AttemptRecoveryAsync(
        IReadOnlyList<WorkflowRecoveryCandidate> stalled,
        int systemUserId,
        CancellationToken ct);
}

/// <summary>Application-level description of a workflow instance eligible for recovery.</summary>
public sealed record WorkflowRecoveryCandidate(
    int InstanceId,
    int DefinitionId,
    int StageId,
    string StageName,
    int? ProjectId,
    int? MostRecentClosedTaskId,
    int TotalTasks,
    int OpenTasks);
