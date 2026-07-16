namespace SiNet.Application.ProjectWork;

/// <summary>
/// Read port that loads a project's DB-defined folder/file skeleton as clean DTOs for the ProjectWork
/// work surface. Returns display DTOs only — never EF entities — so the WPF layer stays free of
/// <c>DbContext</c>. The production implementation lives in <c>SiNet.Infrastructure.Sql</c> (read-only,
/// <c>AsNoTracking()</c> via <c>IDbContextFactory&lt;SiNetDbContext&gt;</c>).
/// </summary>
public interface IProjectFileQueryService
{
    /// <summary>
    /// Loads the DB-defined folder/file tree for a project, or <see langword="null"/> when the project
    /// does not exist. Physical versions are not included — scan with <see cref="IFileIndexService"/>.
    /// </summary>
    /// <param name="projectId">The project id to load.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<ProjectFileTreeDto?> GetProjectFileTreeAsync(int projectId, CancellationToken cancellationToken = default);
}
