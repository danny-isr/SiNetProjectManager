namespace SiNet.Application.FileCatalog;

/// <summary>
/// Mutates the global admin file/folder catalog (DB only — no ACC/FileServer I/O).
/// </summary>
public interface IFileCatalogWriteService
{
    Task<FileCatalogWriteResult> CreateJobTypeAsync(string title, CancellationToken cancellationToken = default);

    Task<FileCatalogWriteResult> RenameJobTypeAsync(int jobTypeId, string title, CancellationToken cancellationToken = default);

    Task<FileCatalogWriteResult> CreateFolderAsync(
        int parentFolderId,
        string title,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an empty catalog folder (no child folders and no file definitions).
    /// </summary>
    Task<FileCatalogWriteResult> DeleteFolderAsync(int folderId, CancellationToken cancellationToken = default);

    Task<FileCatalogWriteResult> CreateFileAsync(
        int folderId,
        int jobTypeId,
        CancellationToken cancellationToken = default);

    Task<FileCatalogWriteResult> DeleteFileAsync(int fileId, CancellationToken cancellationToken = default);

    Task<FileCatalogWriteResult> AssignFileToFolderAsync(
        int fileId,
        int folderId,
        CancellationToken cancellationToken = default);

    Task<FileCatalogWriteResult> SaveFileEditsAsync(
        IReadOnlyList<FileCatalogFileEditDto> edits,
        CancellationToken cancellationToken = default);
}
