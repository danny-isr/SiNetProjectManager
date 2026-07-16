using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Abstraction over a file storage backend (local file server, ACC, Google Drive). Every backend
/// provides the same operations so the tree layer consumes only this interface and does not care
/// where a file physically lives. Clean-layer port of the legacy <c>SiNetSQL.FileIndex.IFileStore</c>.
/// <para>
/// Implementations live in the matching infrastructure module:
/// <c>SiNet.Infrastructure.FileSystem</c> (FileServer), <c>SiNet.Infrastructure.Autodesk</c> (ACC),
/// <c>SiNet.Infrastructure.Google</c> (Drive). Write operations (<see cref="UploadAsync"/>) are gated
/// behind the ACC-write policy and may fail-fast until enabled for a given store.
/// </para>
/// </summary>
public interface IFileStore
{
    /// <summary>Which destination this store handles.</summary>
    FileStorageDestination Destination { get; }

    /// <summary>
    /// Resolves a project folder (DB <c>ProjectFolder.Id</c>) to this store's native folder handle
    /// (absolute path / ACC folder id / Drive folder id). Returns <see langword="null"/> when no
    /// mapping exists for this store.
    /// </summary>
    Task<string?> ResolveFolderHandleAsync(int projectId, int projectFolderId, CancellationToken cancellationToken = default);

    /// <summary>Enumerates all files currently present under the given folder handle.</summary>
    IAsyncEnumerable<ScannedFile> ListFilesAsync(string folderHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the given file to a local temp path and returns that path, so the caller can open it
    /// with a desktop application. For FileServer files this is typically the file's existing path.
    /// </summary>
    Task<string> DownloadToLocalAsync(ScannedFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a locally-staged file into the given folder handle under <paramref name="targetFileName"/>
    /// (the canonical project file name) and returns the descriptor as it now lives in the destination
    /// (with the authoritative <c>NativeId</c>). When a file with the same name already exists this
    /// places a new version (FileServer archives the previous copy; ACC adds a native version).
    /// ACC writes are gated by <see cref="IAccWritePolicy"/> and fail-fast when the gate is closed.
    /// </summary>
    Task<ScannedFile> UploadAsync(string folderHandle, string localSourcePath, string targetFileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the given file from its destination. FileServer deletes the file (and its sidecar);
    /// ACC hides the item (soft delete). ACC deletes are gated by <see cref="IAccWritePolicy"/>.
    /// </summary>
    Task DeleteAsync(ScannedFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the given file within its current folder and returns the updated descriptor. Supported
    /// on FileServer (move on disk); ACC rename is not supported and throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    Task<ScannedFile> RenameAsync(ScannedFile file, string newFileName, CancellationToken cancellationToken = default);
}
