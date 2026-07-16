using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Coordinator that fans a folder scan out to every registered <see cref="IFileStore"/> in parallel
/// and streams back <see cref="ScannedFile"/> results as they arrive. Also tracks in-flight uploads so
/// the UI can render a pending indicator for files that are being uploaded but not yet visible in their
/// authoritative destination. Clean-layer port of the legacy <c>SiNetSQL.FileIndex.FileIndexService</c>.
/// </summary>
public interface IFileIndexService
{
    /// <summary>Raised when a file transitions into or out of the in-flight set.</summary>
    event Action<InFlightChange>? InFlightChanged;

    /// <summary>Returns the store that owns the given destination, or <see langword="null"/>.</summary>
    IFileStore? GetStore(FileStorageDestination destination);

    /// <summary>The destinations that have a registered store.</summary>
    IReadOnlyList<FileStorageDestination> AvailableDestinations { get; }

    /// <summary>Marks a file as "upload in progress" for the given destination.</summary>
    void MarkInFlight(int projectId, string fileName, FileStorageDestination destination);

    /// <summary>Clears an in-flight marker after upload completion (success or failure).</summary>
    void ClearInFlight(int projectId, string fileName, FileStorageDestination destination);

    /// <summary>Returns <see langword="true"/> if the given file is currently uploading.</summary>
    bool IsInFlight(int projectId, string fileName, FileStorageDestination destination);

    /// <summary>
    /// Scans <paramref name="destinations"/> in parallel for the given project folder and streams back
    /// every file found.
    /// </summary>
    IAsyncEnumerable<ScannedFile> ScanFolderAsync(
        int projectId,
        int projectFolderId,
        IEnumerable<FileStorageDestination> destinations,
        CancellationToken cancellationToken = default);
}

/// <summary>Event payload for <see cref="IFileIndexService.InFlightChanged"/>.</summary>
/// <param name="ProjectId">Owning project id.</param>
/// <param name="FileName">File name whose in-flight state changed.</param>
/// <param name="Destination">Destination the upload targets.</param>
/// <param name="IsStarting"><see langword="true"/> when entering the in-flight set; otherwise leaving.</param>
public sealed record InFlightChange(
    int ProjectId,
    string FileName,
    FileStorageDestination Destination,
    bool IsStarting);
