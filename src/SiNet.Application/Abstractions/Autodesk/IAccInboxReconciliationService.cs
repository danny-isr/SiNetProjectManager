namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only ACC inbox reconciliation contract. Resolves message/attachment presence against ACC
/// truth without mutating ACC state and without treating cached DB identifiers as proof.
/// </summary>
public interface IAccInboxReconciliationService
{
    Task<AccInboxReconciliationResult?> ReconcileByMessageIdAsync(
        int emailMessageId,
        CancellationToken cancellationToken = default);

    Task<AccInboxReconciliationResult?> ReconcileByMessageUniqueIdAsync(
        string messageUniqueId,
        CancellationToken cancellationToken = default);
}

public enum AccInboxAttachmentPresenceStatus
{
    ExistsInAcc,
    MissingInAcc,
    UnknownAccInboxFile,
    Locked,
    AlreadyMovedToProject,
    MetadataReadFailed,
    FiledButMoveMetadataFailed
}

public sealed record AccInboxAttachmentReconciliationItem(
    int? InboxAttachmentId,
    int AttachmentIndex,
    string FileName,
    string? AccItemId,
    string? AccVersionId,
    string? OpenAccProjectId,
    string? OpenAccFolderId,
    string? OpenAccItemId,
    bool ExistsInAcc,
    AccInboxAttachmentPresenceStatus Status,
    string StatusText,
    int? ProjectFileId,
    int? ProjectAlternativeId,
    bool LockedForEditing,
    bool MovedToProject,
    bool MetadataReadFailed,
    IReadOnlyDictionary<string, string?> Attributes);

public sealed record AccInboxReconciliationResult(
    int EmailMessageId,
    string? InboxAccProjectId,
    string? InboxAccFolderId,
    IReadOnlyList<AccInboxAttachmentReconciliationItem> Attachments);
