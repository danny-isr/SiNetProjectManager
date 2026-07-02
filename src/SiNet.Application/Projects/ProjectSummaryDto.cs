namespace SiNet.Application.Projects;

/// <summary>
/// Read-only display projection of a project used by the shared Project Selector and any screen that
/// needs to show "which project" without binding to an EF entity (see <c>docs/PROJECTS.md</c> §5/§12).
/// <para>
/// This is a UI display DTO: <see cref="ProjectNumber"/> is a formatted string (the selector searches
/// and displays number / name / place / company as text), deliberately <b>not</b> the EF numeric type
/// used by domain-facing references such as <c>WorkflowProjectRefDto.Number</c>. It carries no behavior,
/// is not a domain entity, and never leaks EF types into the WPF layer.
/// </para>
/// </summary>
/// <param name="ProjectId">Project identifier.</param>
/// <param name="ProjectNumber">Formatted project number shown/searched in the selector (e.g. <c>"1042"</c>).</param>
/// <param name="ProjectName">Project title/name.</param>
/// <param name="PlaceName">Place / city, for display and search; <see langword="null"/> when unknown.</param>
/// <param name="CompanyName">Company / client, for display and search; <see langword="null"/> when unknown.</param>
/// <param name="JobType">Job type / discipline used by the Job Type filter; <see langword="null"/> when unset.</param>
/// <param name="Status">Project status used by the Status filter; <see langword="null"/> when unset.</param>
/// <param name="AssignedUserName">Assigned/responsible user display name used by the User filter; <see langword="null"/> when unassigned.</param>
/// <param name="IsActive"><see langword="true"/> for active projects; <see langword="false"/> for closed ones (drives include-closed filtering).</param>
/// <param name="StatusId">Project status id for id-based filtering; <see langword="null"/> when unknown.</param>
/// <param name="JobTypeIds">Linked job / project type ids for id-based filtering; empty when none.</param>
public sealed record ProjectSummaryDto(
    int ProjectId,
    string ProjectNumber,
    string ProjectName,
    string? PlaceName,
    string? CompanyName,
    string? JobType,
    string? Status,
    string? AssignedUserName,
    bool IsActive,
    int? StatusId = null,
    IReadOnlyList<int>? JobTypeIds = null);
