namespace SiNet.Application.Projects;

/// <summary>
/// Read-only filter option lists for the shared Project Selector. Loaded independently from project
/// search results so <c>MaxResults</c> never truncates filter dropdowns.
/// </summary>
/// <param name="Statuses">All selectable project statuses.</param>
/// <param name="JobTypes">All selectable job / project types.</param>
/// <param name="Users">Assignable users when semantics are clear; otherwise empty.</param>
public sealed record ProjectFilterOptionsDto(
    IReadOnlyList<ProjectFilterOptionDto> Statuses,
    IReadOnlyList<ProjectFilterOptionDto> JobTypes,
    IReadOnlyList<ProjectFilterOptionDto> Users);
