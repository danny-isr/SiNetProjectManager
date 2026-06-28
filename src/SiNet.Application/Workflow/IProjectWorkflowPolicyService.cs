using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SiNet.Application.Workflow;

/// <summary>
/// Read-only port that resolves which workflow definitions are allowed for a
/// project based on the project's ProjectType (JobType) mappings.
/// <para>
/// Lives in the Application layer and exposes clean DTOs only; EF entities never
/// cross this boundary.
/// </para>
/// </summary>
public interface IProjectWorkflowPolicyService
{
    /// <summary>
    /// Returns allowed workflow definitions for a project, resolved via its ProjectTypes.
    /// </summary>
    ValueTask<List<WorkflowDefinitionDto>> GetAllowedWorkflowsAsync(int projectId, CancellationToken ct);

    /// <summary>Returns allowed workflow definitions for a set of ProjectType IDs.</summary>
    ValueTask<List<WorkflowDefinitionDto>> GetAllowedWorkflowsForProjectTypesAsync(
        IReadOnlyList<int> projectTypeIds, CancellationToken ct);

    /// <summary>Checks whether a specific workflow definition is allowed for a project.</summary>
    ValueTask<bool> IsWorkflowAllowedAsync(int projectId, int workflowDefinitionId, CancellationToken ct);
}
