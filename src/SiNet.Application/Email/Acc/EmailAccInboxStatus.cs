namespace SiNet.Application.Email.Acc;

/// <summary>
/// Read model for ACC inbox state of one email message.
/// </summary>
public sealed record EmailAccInboxStatus(
    string MessageUniqueId,
    int? InboxMessageId,
    EmailAccProcessingStatus ProcessingStatus,
    EmailAccLockStatus? LockStatus,
    string StatusDisplay,
    string? InboxAccFolderId,
    int TotalAttachments,
    int ExistingInAccCount,
    int MissingInAccCount,
    IReadOnlyList<EmailAttachmentAccStatus> Attachments)
{
    public bool HasPartialFailure => MissingInAccCount > 0 && ExistingInAccCount > 0;

    public bool IsLockedByOtherUser =>
        ProcessingStatus == EmailAccProcessingStatus.LockedByOtherUser
        || (LockStatus?.IsLocked == true && LockStatus.IsHeldByCurrentUser == false);
}
