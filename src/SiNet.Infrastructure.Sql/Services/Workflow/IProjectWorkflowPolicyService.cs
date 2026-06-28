using SiNetSQL.Models;

namespace SiNetSQL.Services.Workflow;

/// <summary>
/// Read-only port that resolves which <see cref="WorkflowDefinition"/> are allowed for a
/// project based on the project's ProjectType (JobType) mappings.
/// <para>
/// Co-located in <c>SiNet.Infrastructure.Sql</c> for the transitional Workflow read slice.
/// For this round the port intentionally exposes the EF <see cref="WorkflowDefinition"/>
/// entity. Entity leakage is a temporary compromise to be removed in a later round.
/// </para>
/// </summary>
public interface IProjectWorkflowPolicyService
{
    /// <summary>
    /// Returns allowed workflow definitions for a project, resolved via its ProjectTypes.
    /// </summary>
    ValueTask<List<WorkflowDefinition>> GetAllowedWorkflowsAsync(int projectId, CancellationToken ct);

    /// <summary>Returns allowed workflow definitions for a set of ProjectType IDs.</summary>
    ValueTask<List<WorkflowDefinition>> GetAllowedWorkflowsForProjectTypesAsync(
        IReadOnlyList<int> projectTypeIds, CancellationToken ct);

    /// <summary>Checks whether a specific workflow definition is allowed for a project.</summary>
    ValueTask<bool> IsWorkflowAllowedAsync(int projectId, int workflowDefinitionId, CancellationToken ct);
}
