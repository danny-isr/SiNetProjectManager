namespace SiNet.Application.Projects;

/// <summary>
/// Read port that searches and loads projects as <see cref="ProjectSummaryDto"/> for the shared
/// Project Selector and other project-scoped screens (see <c>docs/PROJECTS.md</c> §5/§12).
/// <para>
/// It returns UI display DTOs only — never EF entities — so the WPF layer stays free of
/// <c>DbContext</c>. The production implementation is <c>ProjectQueryService</c> in
/// <c>SiNet.Infrastructure.Sql</c> (read-only, <c>AsNoTracking()</c> via
/// <c>IDbContextFactory&lt;SiNetDbContext&gt;</c>). Design-time and tests may use
/// <c>FakeProjectQueryService</c> behind this same interface.
/// </para>
/// </summary>
public interface IProjectQueryService
{
    /// <summary>
    /// Returns the projects matching <paramref name="query"/>, applying the selector's parity ordering
    /// (project number descending) and dummy-number exclusion. Never returns <see langword="null"/>.
    /// </summary>
    /// <param name="query">The search/filter criteria; an empty query returns the default active list.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
        ProjectSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single project by id, or <see langword="null"/> when no project with that id exists.
    /// </summary>
    /// <param name="projectId">The project id to load.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<ProjectSummaryDto?> GetProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default);
}
