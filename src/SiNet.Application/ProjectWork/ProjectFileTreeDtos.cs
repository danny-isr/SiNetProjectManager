using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Read-only snapshot of a project's DB-defined folder/file structure, returned by
/// <see cref="IProjectFileQueryService.GetProjectFileTreeAsync"/>. Physical alternatives/versions are
/// discovered separately by scanning (<see cref="IFileIndexService"/>); this snapshot carries only the
/// database-defined skeleton plus the project number used to match scanned files.
/// </summary>
/// <param name="ProjectId">Owning project id.</param>
/// <param name="ProjectNumber">Owning project number (used to match scanned filenames).</param>
/// <param name="RootFolders">Top-level project folders (children of the synthetic project-root folder).</param>
public sealed record ProjectFileTreeDto(
    int ProjectId,
    int ProjectNumber,
    IReadOnlyList<ProjectFolderDto> RootFolders);

/// <summary>
/// DB-defined folder node with its subfolders and file definitions. Folder identity comes from the
/// shared folder template tree; a folder's physical path is resolved per-project at scan time via
/// <see cref="IProjectFolderPathResolver"/>.
/// </summary>
/// <param name="FolderId">DB id of the folder.</param>
/// <param name="Name">Folder display name.</param>
/// <param name="ParentFolderId">Parent folder id, or <see langword="null"/> for a root folder.</param>
/// <param name="Children">Child folders.</param>
/// <param name="Files">File definitions declared directly in this folder for the project.</param>
public sealed record ProjectFolderDto(
    int FolderId,
    string Name,
    int? ParentFolderId,
    IReadOnlyList<ProjectFolderDto> Children,
    IReadOnlyList<ProjectFileDefinitionDto> Files);

/// <summary>
/// DB-defined file definition (the logical file). Physical alternatives/versions come from scanning and
/// are matched to this definition by <see cref="ProjectType"/> + <see cref="Number"/> (mirroring the
/// legacy canonical filename identity).
/// </summary>
/// <param name="FileId">DB id of the <c>ProjectFile</c>.</param>
/// <param name="BaseName">Canonical base name/title of the file.</param>
/// <param name="Extension">Extension including the leading dot, derived from the title; may be empty.</param>
/// <param name="StorageDestination">Configured default storage destination.</param>
/// <param name="FolderId">Parent folder id.</param>
/// <param name="ProjectType">Project type id (legacy <c>TypeProjId</c>) used to match scanned files.</param>
/// <param name="Number">File number used to match scanned files; <see langword="null"/> when unset.</param>
/// <param name="TemplateLocation">Template source path, when a template exists; <see langword="null"/> otherwise.</param>
public sealed record ProjectFileDefinitionDto(
    int FileId,
    string BaseName,
    string Extension,
    FileStorageDestination StorageDestination,
    int FolderId,
    int? ProjectType,
    int? Number,
    string? TemplateLocation);
