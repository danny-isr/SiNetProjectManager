namespace SiNet.Application.Workflow;

/// <summary>
/// Minimal project reference used inside workflow read DTOs.
/// Workflows may be project-bound or project-independent, so this can be absent.
/// </summary>
/// <param name="Id">Project identifier.</param>
/// <param name="Number">Project number, when assigned. Matches the EF entity type (<c>float?</c>).</param>
/// <param name="Title">Project title.</param>
public sealed record WorkflowProjectRefDto(int Id, float? Number, string? Title);
