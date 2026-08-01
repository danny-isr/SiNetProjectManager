namespace SiNet.Application.Workflow;

/// <summary>
/// After client quote approval: validate every project type has a workflow mapping,
/// then start one project-bound instance per JobType track
/// (<c>ProjectId + WorkflowDefinitionId + JobTypeId</c>).
/// </summary>
public interface IProjectTypeContinuationStarter
{
    /// <summary>
    /// Fails when the project has no project types, or any type lacks an enabled mapping
    /// to an active workflow definition.
    /// </summary>
    Task<ProjectTypeContinuationResult> ValidateMappingsAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates mappings then starts missing Active/Paused track instances
    /// (one per JobType; same definition may start multiple times).
    /// </summary>
    Task<ProjectTypeContinuationResult> StartContinuationsAsync(
        int projectId,
        int actingUserId,
        CancellationToken cancellationToken = default);
}
