namespace SiNet.Application.Projects;

/// <summary>
/// Server query for <see cref="IProjectDashboardQueryService.GetRowsAsync"/>.
/// MVP loads rows once; remaining filters run in the UI (see <c>docs/PROJECTS_DASHBOARD.md</c>).
/// </summary>
/// <param name="IncludeClosed">
/// When <see langword="false"/> (default), only active projects (<c>EndOfProject != true</c>) are returned.
/// </param>
public sealed record ProjectDashboardQuery(bool IncludeClosed = false);
