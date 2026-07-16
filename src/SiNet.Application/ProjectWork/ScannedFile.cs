using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// A file discovered by an <see cref="IFileStore"/> scan. Represents the single source of truth for
/// file presence at runtime — no database row is required for a file to be "known" by the system.
/// Clean-layer port of the legacy <c>SiNetSQL.FileIndex.ScannedFile</c>.
/// </summary>
/// <param name="Source">Which storage destination this file lives in.</param>
/// <param name="FileName">The file's display name (no directory). Used as the logical identity.</param>
/// <param name="NativeId">
/// Store-native identifier used to open/download the file: full path for FileServer, ItemId URN for
/// ACC, File.Id for Google Drive.
/// </param>
/// <param name="SizeBytes">File size in bytes, or 0 if unknown.</param>
/// <param name="LastModified">Last-modified timestamp, or <see langword="null"/> if unknown.</param>
/// <param name="Parsed">
/// Parsed pattern components if the filename matched the canonical project pattern;
/// <see langword="null"/> otherwise (file is "unfiled").
/// </param>
/// <param name="SourceFileName">
/// Original filename as received from an external party (sidecar on FileServer / <c>SiSourceFileName</c>
/// ACC attribute). <see langword="null"/> when the file has no sidecar / attribute.
/// </param>
/// <param name="IsManualUpload">
/// <see langword="true"/> when an ACC item was uploaded manually from a non-DB-mapped folder. Always
/// <see langword="false"/> for FileServer / Drive results.
/// </param>
/// <param name="OriginalFolderPath">
/// For manual ACC uploads, the absolute source file-server folder path at upload time;
/// <see langword="null"/> otherwise.
/// </param>
/// <param name="AccViewerUrl">
/// For ACC-backed files, the resolved viewer URL used by the embedded ACC viewer;
/// <see langword="null"/> for non-ACC files or when unresolved.
/// </param>
/// <param name="AccProjectId">
/// For ACC-backed files, the owning ACC project id — needed to download/open the item;
/// <see langword="null"/> for non-ACC files.
/// </param>
public sealed record ScannedFile(
    FileStorageDestination Source,
    string FileName,
    string NativeId,
    long SizeBytes,
    DateTime? LastModified,
    ParsedProjectFileName? Parsed,
    string? SourceFileName = null,
    bool IsManualUpload = false,
    string? OriginalFolderPath = null,
    string? AccViewerUrl = null,
    string? AccProjectId = null);
