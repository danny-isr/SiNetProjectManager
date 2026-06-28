using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiNet.Domain.Workflow;

namespace SiNet.Application.Workflow;

/// <summary>
/// Read-only port for workflow queries (definitions, instances, transition history,
/// and cross-project dashboard snapshots).
/// <para>
/// Lives in the Application layer and exposes clean DTOs only; EF entities never
/// cross this boundary. The SQL infrastructure implements this port and maps
/// entities to DTOs internally.
/// </para>
/// </summary>
public interface IWorkflowQueryService
{
    /// <summary>Returns all active workflow definitions.</summary>
    ValueTask<List<WorkflowDefinitionDto>> GetActiveDefinitionsAsync(CancellationToken ct);

    /// <summary>Returns a single definition with its stages.</summary>
    ValueTask<WorkflowDefinitionDto?> GetDefinitionAsync(int definitionId, CancellationToken ct);

    /// <summary>Returns all workflow instances for a project, optionally filtered by status.</summary>
    ValueTask<List<WorkflowInstanceDto>> GetByProjectAsync(
        int projectId,
        WorkflowStatus? statusFilter,
        CancellationToken ct);

    /// <summary>Returns active workflows for a project (status = Active or Paused).</summary>
    ValueTask<List<WorkflowInstanceDto>> GetActiveByProjectAsync(int projectId, CancellationToken ct);

    /// <summary>Returns a single workflow instance with full details (definition, stages, transitions).</summary>
    ValueTask<WorkflowInstanceDto?> GetInstanceDetailAsync(int instanceId, CancellationToken ct);

    /// <summary>Returns the allowed target stages for the current stage of a workflow instance.</summary>
    ValueTask<List<WorkflowStageDefinitionDto>> GetAllowedNextStagesAsync(
        int instanceId,
        CancellationToken ct);

    /// <summary>Returns all non-ended projects LEFT-JOINed with their latest workflow instance.</summary>
    ValueTask<List<ProjectWorkflowSnapshotDto>> GetAllProjectWorkflowSnapshotsAsync(CancellationToken ct);

    /// <summary>Returns ALL workflow instances (regardless of project) with full stage info.</summary>
    ValueTask<List<WorkflowInstanceSnapshotDto>> GetAllWorkflowInstanceSnapshotsAsync(CancellationToken ct);

    /// <summary>Returns the distinct workflow definition names that have at least one instance.</summary>
    ValueTask<List<string>> GetDistinctWorkflowNamesAsync(CancellationToken ct);
}
