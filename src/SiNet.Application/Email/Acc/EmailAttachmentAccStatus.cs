namespace SiNet.Application.Email.Acc;

/// <summary>
/// Per-attachment ACC inbox truth for UI display. Mapped from reconciliation items when available.
/// </summary>
public sealed record EmailAttachmentAccStatus(
    int? InboxAttachmentId,
    int AttachmentIndex,
    string FileName,
    EmailAccAttachmentPresence Presence,
    string StatusText,
    bool LockedForEditing,
    bool MovedToProject,
    int? ProjectFileId,
    int? ProjectAlternativeId);

public enum EmailAccAttachmentPresence
{
    Unknown = 0,
    ExistsInAcc = 1,
    MissingInAcc = 2,
    Locked = 3,
    AlreadyMovedToProject = 4,
    MetadataReadFailed = 5,
}
