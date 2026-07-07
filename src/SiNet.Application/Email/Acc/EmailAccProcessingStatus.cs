namespace SiNet.Application.Email.Acc;

/// <summary>
/// Aggregate ACC inbox processing status for a message. Computed from DB cache + ACC reconciliation —
/// not persisted as a separate enum.
/// </summary>
public enum EmailAccProcessingStatus
{
    Unknown = 0,
    NotInDatabase = 1,
    PendingUpload = 2,
    UploadInProgress = 3,
    LockedByOtherUser = 4,
    PartiallyUploaded = 5,
    UploadedToAcc = 6,
    MovedToProject = 7,
    MissingInAcc = 8,
    Failed = 9,
    ReconciliationRequired = 10,
    NotChecked = 11,
}
