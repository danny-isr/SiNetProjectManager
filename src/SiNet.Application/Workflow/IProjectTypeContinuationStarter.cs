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
    /// to an active workflow definition. Mapping-only; does not evaluate Pilot.
    /// </summary>
    Task<ProjectTypeContinuationResult> ValidateMappingsAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates mappings and that Pilot policy allows each required <em>new</em> continuation
    /// start for <paramref name="actingUserId"/> (must be the real completion <c>command.UserId</c>).
    /// Same policy as <see cref="IPilotStartGate"/> / <c>NativeWorkflowCommandService.StartAsync</c>.
    /// </summary>
    Task<ProjectTypeContinuationResult> ValidateBeforeQuoteApprovalAsync(
        int projectId,
        int actingUserId,
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
