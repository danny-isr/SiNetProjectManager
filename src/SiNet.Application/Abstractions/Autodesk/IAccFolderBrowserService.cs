namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only ACC folder browser. When the folder ID argument is null/empty, the service should
/// resolve the project's "Project Files" root and return its contents.
/// </summary>
public interface IAccFolderBrowserService
{
    Task<AccFolderBrowseResult?> BrowseAsync(
        string projectId,
        string? folderId = null,
        CancellationToken cancellationToken = default);
}
