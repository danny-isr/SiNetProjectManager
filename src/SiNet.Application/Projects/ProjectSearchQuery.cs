namespace SiNet.Application.Projects;

/// <summary>
/// Read query passed to <see cref="IProjectQueryService.SearchProjectsAsync"/> to search/filter
/// selectable projects for the shared Project Selector (see <c>docs/PROJECTS.md</c> §6).
/// </summary>
/// <param name="SearchText">Free-text search across number / name / place / company; <see langword="null"/> or empty means no text filter.</param>
/// <param name="JobType">Legacy display-name filter; prefer <paramref name="JobTypeId"/>.</param>
/// <param name="Status">Legacy display-name filter; prefer <paramref name="StatusId"/>.</param>
/// <param name="JobTypeId">Restrict to projects linked to this job / project type id; <see langword="null"/> means all.</param>
/// <param name="StatusId">Restrict to this project status id; <see langword="null"/> means all.</param>
/// <param name="AssignedUserId">Restrict to projects assigned to this user id; <see langword="null"/> means all users.</param>
/// <param name="IncludeClosed"><see langword="true"/> to include closed/inactive projects; defaults to <see langword="false"/> (active only).</param>
/// <param name="MaxResults">Display cap only — applied after filtering on the full search source; <see langword="null"/> means no cap.</param>
public sealed record ProjectSearchQuery(
    string? SearchText = null,
    string? JobType = null,
    string? Status = null,
    int? JobTypeId = null,
    int? StatusId = null,
    int? AssignedUserId = null,
    bool IncludeClosed = false,
    int? MaxResults = null);
