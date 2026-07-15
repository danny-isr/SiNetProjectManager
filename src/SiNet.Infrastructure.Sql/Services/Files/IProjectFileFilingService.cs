using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Input to <see cref="IProjectFileFilingService.FileAsync"/>. Describes a single file (already
/// available locally) that should be placed into a project-file slot. Native port of the legacy
/// <c>SiNetSQL.Services.Files.FileProjectFileRequest</c>.
/// </summary>
public sealed record FileProjectFileRequest(
    int ProjectId,
    int ProjectFileId,
    int? ProjectAlternativeId,
    string SourceLocalPath,
    string OriginalFileName,
    FileInstanceSourceType SourceType,
    int? SourceEmailAttachmentId = null,
    string? EmailSubject = null,
    string? EmailFrom = null,
    string? EmailDate = null)
{
    public string? SourceGmailMessageId { get; init; }
    public DateTime? SourceMessageDateUtc { get; init; }
    public string? SourceOriginalFileName { get; init; }
    public long? SourceFileSizeBytes { get; init; }
    public string? SourceContentSha256 { get; init; }
    public int? SourceAttachmentId { get; init; }
    public string? FolderNameOverride { get; init; }
}

/// <summary>Result of <see cref="IProjectFileFilingService.FileAsync"/>.</summary>
public sealed record FileProjectFileResult(
    string PlacedFileName,
    string? PlacedFilePath,
    FileStorageDestination StorageDestination,
    int CurrentVersionNumber,
    ArchiveResult? ArchivedPreviousVersion)
{
    public FileStorageDestination TargetDestination { get; init; } = StorageDestination;
    public string TargetFileName { get; init; } = PlacedFileName;
    public string? TargetFilePath { get; init; } = PlacedFilePath;
    public string? TargetAccItemId { get; init; }
    public string? TargetAccVersionId { get; init; }
    public string? TargetAccFolderId { get; init; }
    public int TargetProjectId { get; init; }
    public int TargetProjectFileId { get; init; }
    public int? TargetProjectAlternativeId { get; init; }
    public bool AlreadySameSource { get; init; }
}

/// <summary>
/// Centralized service for placing a file into a project-file slot. Behavior is dispatched by
/// <c>ProjectFile.StorageDestination</c> (FileServer or ACC). Native port of the legacy service —
/// the New System engine calls this directly without the legacy SiNetSQL project reference.
/// </summary>
public interface IProjectFileFilingService
{
    Task<FileProjectFileResult> FileAsync(FileProjectFileRequest request, CancellationToken ct = default);
}
