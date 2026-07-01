namespace SiNet.Application.Projects;

/// <summary>
/// Read query passed to <see cref="IProjectQueryService.SearchProjectsAsync"/> to search/filter
/// selectable projects for the shared Project Selector (see <c>docs/PROJECTS.md</c> §6).
/// <para>
/// All members are optional; an empty query returns the default project list (active projects, sorted
/// by number descending, dummy numbers excluded — the selector's parity behavior). This is a runtime
/// query object only; it does not describe or touch database schema.
/// </para>
/// </summary>
/// <param name="SearchText">Free-text search across number / name / place / company; <see langword="null"/> or empty means no text filter.</param>
/// <param name="JobType">Restrict to a single job type / discipline; <see langword="null"/> means all.</param>
/// <param name="Status">Restrict to a single project status; <see langword="null"/> means all.</param>
/// <param name="AssignedUserId">Restrict to projects assigned to this user id; <see langword="null"/> means all users.</param>
/// <param name="IncludeClosed"><see langword="true"/> to include closed/inactive projects; defaults to <see langword="false"/> (active only).</param>
/// <param name="MaxResults">
/// Optional cap on the number of returned rows (applied after parity ordering, so the highest project
/// numbers win). <see langword="null"/> or a non-positive value means "no cap". This is a
/// <b>responsiveness</b> guard for the shared selector so a very large project table never floods a
/// non-virtualized ComboBox; it is a runtime query concern only and does not describe/touch schema.
/// </param>
public sealed record ProjectSearchQuery(
    string? SearchText = null,
    string? JobType = null,
    string? Status = null,
    int? AssignedUserId = null,
    bool IncludeClosed = false,
    int? MaxResults = null);
