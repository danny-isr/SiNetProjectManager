namespace SiNet.Application.Projects;

/// <summary>
/// Read query passed to <see cref="IProjectQueryService.SearchProjectsAsync"/> to search/filter
/// selectable projects for the shared Project Selector (see <c>docs/PROJECTS.md</c> §6).
/// </summary>
/// <param name="SearchText">Free-text search across number / name / place / company; <see langword="null"/> or empty means no text filter.</param>
/// <param name="JobType">Restrict to a single job type / discipline; <see langword="null"/> means all.</param>
/// <param name="Status">Restrict to a single project status; <see langword="null"/> means all.</param>
/// <param name="AssignedUserId">Restrict to projects assigned to this user id; <see langword="null"/> means all users.</param>
/// <param name="IncludeClosed"><see langword="true"/> to include closed/inactive projects; defaults to <see langword="false"/> (active only).</param>
/// <param name="MaxResults">Display cap only — applied after filtering on the full search source; <see langword="null"/> means no cap.</param>
public sealed record ProjectSearchQuery(
    string? SearchText = null,
    string? JobType = null,
    string? Status = null,
    int? AssignedUserId = null,
    bool IncludeClosed = false,
    int? MaxResults = null);
