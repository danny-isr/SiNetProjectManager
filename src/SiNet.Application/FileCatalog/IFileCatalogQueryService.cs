namespace SiNet.Application.FileCatalog;

/// <summary>
/// Loads the global admin file/folder catalog (JobTypes, ProjectFolders, ProjectFiles).
/// Not project-scoped — unlike <c>IProjectFileQueryService</c>.
/// </summary>
public interface IFileCatalogQueryService
{
    Task<FileCatalogSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
