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

/// <summary>
/// Stable read-only reconciliation classification that future New System windows can depend on
/// without knowing every legacy operational nuance.
/// </summary>
public enum AccInboxAttachmentTruthStatus
{
    Exists,
    Missing,
    Stale,
    Unknown,
}

public static class AccInboxAttachmentPresenceStatusExtensions
{
    public static AccInboxAttachmentTruthStatus ToTruthStatus(this AccInboxAttachmentPresenceStatus status) => status switch
    {
        AccInboxAttachmentPresenceStatus.ExistsInAcc => AccInboxAttachmentTruthStatus.Exists,
        AccInboxAttachmentPresenceStatus.Locked => AccInboxAttachmentTruthStatus.Exists,
        AccInboxAttachmentPresenceStatus.MissingInAcc => AccInboxAttachmentTruthStatus.Missing,
        AccInboxAttachmentPresenceStatus.AlreadyMovedToProject => AccInboxAttachmentTruthStatus.Stale,
        AccInboxAttachmentPresenceStatus.FiledButMoveMetadataFailed => AccInboxAttachmentTruthStatus.Stale,
        AccInboxAttachmentPresenceStatus.UnknownAccInboxFile => AccInboxAttachmentTruthStatus.Unknown,
        AccInboxAttachmentPresenceStatus.MetadataReadFailed => AccInboxAttachmentTruthStatus.Unknown,
        _ => AccInboxAttachmentTruthStatus.Unknown,
    };
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
    IReadOnlyDictionary<string, string?> Attributes)
{
    public AccInboxAttachmentTruthStatus TruthStatus => Status.ToTruthStatus();
}

public sealed record AccInboxReconciliationResult(
    int EmailMessageId,
    string? InboxAccProjectId,
    string? InboxAccFolderId,
    IReadOnlyList<AccInboxAttachmentReconciliationItem> Attachments);
