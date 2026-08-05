namespace SiNet.Application.ProjectWork;

/// <summary>
/// Clean Drive primitives for ProjectWork file storage. Implementation lives in
/// <c>SiNet.Infrastructure.Google</c> and must obtain its <c>DriveService</c> from the shared
/// Google user credential provider — never by opening a separate OAuth flow.
/// </summary>
public interface IGoogleDriveFileService
{
    /// <summary>Ensures each path segment exists under <paramref name="rootFolderId"/>; returns the innermost folder id.</summary>
    Task<string> EnsureFolderPathAsync(IReadOnlyList<string> pathSegments, string rootFolderId, CancellationToken cancellationToken = default);

    /// <summary>Lists non-folder, non-trashed files directly inside <paramref name="parentId"/>.</summary>
    Task<IReadOnlyList<GoogleDriveFileEntry>> ListFilesAsync(string parentId, CancellationToken cancellationToken = default);

    /// <summary>Finds all non-folder files with the exact <paramref name="fileName"/> in <paramref name="parentId"/>.</summary>
    Task<IReadOnlyList<GoogleDriveFileEntry>> FindFilesByNameAsync(string fileName, string parentId, CancellationToken cancellationToken = default);

    /// <summary>Uploads a local file as a new Drive file named <paramref name="targetName"/>.</summary>
    Task<GoogleDriveFileEntry> UploadFileAsync(string parentId, string localFilePath, string targetName, CancellationToken cancellationToken = default);

    /// <summary>Uploads a string payload (typically sidecar JSON) as a new Drive file.</summary>
    Task<GoogleDriveFileEntry> UploadStringAsync(string parentId, string content, string targetName, string mimeType = "application/json", CancellationToken cancellationToken = default);

    /// <summary>Downloads a Drive file by id into <paramref name="destination"/>.</summary>
    Task DownloadFileAsync(string fileId, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a Drive file (trash).</summary>
    Task TrashFileAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>Renames a Drive file in place; returns updated metadata.</summary>
    Task<GoogleDriveFileEntry> RenameFileAsync(string fileId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Finds a direct child folder by exact name under <paramref name="parentId"/>.</summary>
    Task<string?> FindFolderIdByNameAsync(string folderName, string parentId, CancellationToken cancellationToken = default);

    /// <summary>Returns parent folder ids for <paramref name="fileId"/> (Shared Drive aware).</summary>
    Task<IReadOnlyList<string>> GetParentIdsAsync(string fileId, CancellationToken cancellationToken = default);
}

/// <summary>Minimal Drive file metadata used across ProjectWork layer boundaries.</summary>
/// <param name="Id">Drive file id.</param>
/// <param name="Name">Display name.</param>
/// <param name="SizeBytes">Size in bytes, or 0 when unknown.</param>
/// <param name="LastModifiedUtc">Last modified UTC, or null when unknown.</param>
public sealed record GoogleDriveFileEntry(
    string Id,
    string Name,
    long SizeBytes,
    DateTime? LastModifiedUtc);

/// <summary>
/// Thrown when a Drive folder already contains a file (or sidecar) with the target name and the
/// store refuses to pick a winner.
/// </summary>
public sealed class FileStoreConflictException : Exception
{
    public FileStoreConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the shared Google session lacks a required Drive (or other) scope and the user must
/// perform a one-time interactive re-consent via <c>IConnectorAuthService.LoginAsync</c>.
/// </summary>
public sealed class GoogleConsentRequiredException : Exception
{
    public GoogleConsentRequiredException(string message) : base(message)
    {
    }
}
