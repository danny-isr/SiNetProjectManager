namespace SiNet.Application.Projects;

/// <summary>
/// Read-only Application port for the Projects Overview Dashboard
/// (see <c>docs/PROJECTS_DASHBOARD.md</c>).
/// <para>
/// Returns aggregated project rows (status, types, dates, open workflows, open tasks) without
/// exposing EF entities. Performs no writes and does not mutate workflow or tasks.
/// </para>
/// </summary>
public interface IProjectDashboardQueryService
{
    /// <summary>
    /// Loads dashboard rows. Callers apply client-side filters except
    /// <see cref="ProjectDashboardQuery.IncludeClosed"/> which is applied server-side.
    /// </summary>
    Task<IReadOnlyList<ProjectDashboardRowDto>> GetRowsAsync(
        ProjectDashboardQuery query,
        CancellationToken cancellationToken = default);
}
