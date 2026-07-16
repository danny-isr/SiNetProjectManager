namespace SiNet.Application.ProjectWork;

/// <summary>
/// DTO snapshot of an "active" project file in a folder, with its alternatives and versions. Plain
/// immutable records so consumers in other surfaces (Inspection, Email) can use them without taking a
/// dependency on WPF tree node types. Clean-layer port of the legacy
/// <c>SiNetSQL.Services.ActiveFileQuery.ActiveFileInfo</c>.
/// </summary>
/// <param name="FileId">DB id of the underlying <c>ProjectFile</c>, when known.</param>
/// <param name="FileName">Display name of the file (without extension).</param>
/// <param name="Extension">File extension including the leading dot (e.g. ".dwg").</param>
/// <param name="ProjectNumber">Owning project number.</param>
/// <param name="FolderId">Parent folder id (DB).</param>
/// <param name="StorageDestination">Configured storage destination label, if available.</param>
/// <param name="Alternatives">Alternatives belonging to the file.</param>
public sealed record ActiveFileInfo(
    int? FileId,
    string FileName,
    string Extension,
    int ProjectNumber,
    int FolderId,
    string? StorageDestination,
    IReadOnlyList<ActiveAlternativeInfo> Alternatives);

/// <summary>DTO snapshot of an alternative under a file.</summary>
/// <param name="AlternativeName">Alternative label.</param>
/// <param name="Versions">Versions belonging to the alternative.</param>
public sealed record ActiveAlternativeInfo(
    string AlternativeName,
    IReadOnlyList<ActiveVersionInfo> Versions);

/// <summary>DTO snapshot of a single version under an alternative.</summary>
/// <param name="VersionNumber">Version number.</param>
/// <param name="Description">Optional description/base name.</param>
/// <param name="FullPath">Full path for FileServer versions; <see langword="null"/> otherwise.</param>
/// <param name="Size">Formatted size for display; <see langword="null"/> when unknown.</param>
/// <param name="Date">Formatted last-modified date for display; <see langword="null"/> when unknown.</param>
/// <param name="AccItemId">ACC item id for ACC-backed versions; <see langword="null"/> otherwise.</param>
/// <param name="AccViewerUrl">ACC viewer URL for ACC-backed versions; <see langword="null"/> otherwise.</param>
/// <param name="DriveFileId">Google Drive file id for Drive-backed versions; <see langword="null"/> otherwise.</param>
public sealed record ActiveVersionInfo(
    int VersionNumber,
    string? Description,
    string? FullPath,
    string? Size,
    string? Date,
    string? AccItemId,
    string? AccViewerUrl,
    string? DriveFileId = null);

/// <summary>
/// Hierarchical folder snapshot used by tree-style pickers (e.g. the Inspection reviewed-plan / note-
/// linked-file picker). Mirrors the folder → file → alternative → version structure of the live work
/// surface without depending on WPF tree-node types.
/// </summary>
/// <param name="FolderId">DB folder id.</param>
/// <param name="Title">Folder display title.</param>
/// <param name="FullPath">Resolved path when known; <see langword="null"/> otherwise.</param>
/// <param name="Files">Active files directly under this folder.</param>
/// <param name="Children">Child folders.</param>
public sealed record ActiveFolderInfo(
    int FolderId,
    string Title,
    string? FullPath,
    IReadOnlyList<ActiveFileInfo> Files,
    IReadOnlyList<ActiveFolderInfo> Children);
