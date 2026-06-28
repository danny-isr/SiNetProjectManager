using SiNetSQL.Models;

namespace SiNetSQL.Services.Workflow;

/// <summary>
/// Read-only port for workflow queries (definitions, instances, transition history,
/// and cross-project dashboard snapshots).
/// <para>
/// Co-located in <c>SiNet.Infrastructure.Sql</c> for the transitional Workflow read slice.
/// For this round the port intentionally exposes EF entities
/// (<see cref="WorkflowDefinition"/>, <see cref="WorkflowInstance"/>,
/// <see cref="WorkflowStageDefinition"/>) so existing consumers can adopt the abstraction
/// without a parallel DTO surface. Entity leakage is a temporary compromise to be removed
/// in a later round.
/// </para>
/// </summary>
public interface IWorkflowQueryService
{
    /// <summary>Returns all active workflow definitions.</summary>
    ValueTask<List<WorkflowDefinition>> GetActiveDefinitionsAsync(CancellationToken ct);

    /// <summary>Returns a single definition with its stages and transition rules.</summary>
    ValueTask<WorkflowDefinition?> GetDefinitionAsync(int definitionId, CancellationToken ct);

    /// <summary>Returns all workflow instances for a project, optionally filtered by status.</summary>
    ValueTask<List<WorkflowInstance>> GetByProjectAsync(
        int projectId,
        WorkflowStatus? statusFilter,
        CancellationToken ct);

    /// <summary>Returns active workflows for a project (status = Active or Paused).</summary>
    ValueTask<List<WorkflowInstance>> GetActiveByProjectAsync(int projectId, CancellationToken ct);

    /// <summary>Returns a single workflow instance with full details (definition, stages, transitions).</summary>
    ValueTask<WorkflowInstance?> GetInstanceDetailAsync(int instanceId, CancellationToken ct);

    /// <summary>Returns the allowed target stages for the current stage of a workflow instance.</summary>
    ValueTask<List<WorkflowStageDefinition>> GetAllowedNextStagesAsync(
        int instanceId,
        CancellationToken ct);

    /// <summary>Returns all non-ended projects LEFT-JOINed with their latest workflow instance.</summary>
    ValueTask<List<ProjectWorkflowSnapshot>> GetAllProjectWorkflowSnapshotsAsync(CancellationToken ct);

    /// <summary>Returns ALL workflow instances (regardless of project) with full stage info.</summary>
    ValueTask<List<WorkflowInstanceSnapshot>> GetAllWorkflowInstanceSnapshotsAsync(CancellationToken ct);

    /// <summary>Returns the distinct workflow definition names that have at least one instance.</summary>
    ValueTask<List<string>> GetDistinctWorkflowNamesAsync(CancellationToken ct);
}
