namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Semantic ACC upload port for placing a local file into an ACC folder lineage while preserving
/// same-source detection, best-effort metadata stamping, and optional companion-document upload.
/// </summary>
public interface IAccFileUploadService
{
    Task<AccFileUploadResult> UploadAsync(
        AccFileUploadRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for uploading a local file into ACC. Callers may supply either an already-resolved
/// <see cref="TargetFolderId"/> or a <see cref="RootFolderId"/> plus <see cref="PathSegments"/>
/// that the adapter must ensure.
/// </summary>
public sealed record AccFileUploadRequest(
    string ProjectId,
    string LocalSourcePath,
    string DisplayName)
{
    public string? TargetFolderId { get; init; }
    public string? RootFolderId { get; init; }
    public IReadOnlyList<string> PathSegments { get; init; } = Array.Empty<string>();
    public string? ExistingItemId { get; init; }
    public AccFileSourceIdentity? SourceIdentity { get; init; }
    public AccFileUploadSnapshot? Snapshot { get; init; }
    public AccFileUploadCompanionDocument? CompanionDocument { get; init; }
}

/// <summary>
/// Optional identity hints used to detect that an existing ACC item already represents the same
/// incoming source file and therefore should not receive a redundant new version.
/// </summary>
public sealed record AccFileSourceIdentity(
    string? GmailMessageId,
    DateTime? MessageDateUtc,
    string? OriginalFileName,
    long? FileSizeBytes,
    string? ContentSha256,
    int? AttachmentId);

/// <summary>
/// Optional ProjectFile/SI snapshot to stamp onto the uploaded ACC item as best-effort custom
/// attributes.
/// </summary>
public sealed record AccFileUploadSnapshot(
    string? LastFileName,
    long? LastSizeBytes,
    DateTime? LastSavedUtc,
    IReadOnlyList<string> SourceFileNames,
    string? Notes,
    bool IsManualUpload,
    string? OriginalFolderPath);

/// <summary>
/// Optional companion document that should be uploaded into the same target ACC folder after the
/// primary file upload succeeds. Used by folder-level metadata JSON flows.
/// </summary>
public sealed record AccFileUploadCompanionDocument(
    string FileName,
    string ContentText,
    string ContentType = "application/json");

/// <summary>
/// Outcome of an ACC semantic upload.
/// </summary>
public sealed record AccFileUploadResult(
    string FolderId,
    string ItemId,
    string? VersionId,
    string FileName,
    bool AlreadySameSource);
