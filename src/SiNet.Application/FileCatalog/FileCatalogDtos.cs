using SiNet.Domain.Files;

namespace SiNet.Application.FileCatalog;

public sealed record FileCatalogJobTypeDto(int Id, string Title);

public sealed record FileCatalogFolderDto(
    int FolderId,
    string Title,
    int? ParentFolderId,
    IReadOnlyList<FileCatalogFolderDto> Children);

public sealed record FileCatalogFileDto(
    int FileId,
    string? Title,
    float? Number,
    string? Typefile,
    bool? LookAtDes,
    bool? OutSidData,
    FileStorageDestination StorageDestination,
    string? TemplateLocation,
    string? Description,
    int? FolderId,
    int? JobTypeId,
    bool IsRequired,
    string? Code);

public sealed record FileCatalogSnapshotDto(
    IReadOnlyList<FileCatalogJobTypeDto> JobTypes,
    IReadOnlyList<FileCatalogFolderDto> FolderRoots,
    IReadOnlyList<FileCatalogFileDto> Files,
    IReadOnlyList<string> FileExtensions);

public sealed record FileCatalogFileEditDto(
    int FileId,
    string? Title,
    string? Typefile,
    bool? LookAtDes,
    bool? OutSidData,
    FileStorageDestination StorageDestination,
    string? TemplateLocation,
    string? Description,
    bool IsRequired);

public sealed record FileCatalogWriteResult(bool Success, string? ErrorMessage, int? NewId = null)
{
    public static FileCatalogWriteResult Ok(int? newId = null) => new(true, null, newId);

    public static FileCatalogWriteResult Fail(string message) => new(false, message, null);
}
