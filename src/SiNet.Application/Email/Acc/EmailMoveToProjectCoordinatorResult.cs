namespace SiNet.Application.Email.Acc;

public enum EmailMoveToProjectOutcome
{
    Succeeded = 0,
    DeferredRequiresUi = 1,
    Failed = 2,
    BackendNotAvailable = 3,
    NotSupported = 4,
}

public sealed record EmailMoveToProjectCoordinatorResult(
    EmailMoveToProjectOutcome Outcome,
    string Message,
    int MovedCount = 0,
    int FailedCount = 0,
    IReadOnlyList<EmailMoveToProjectAttachmentFailure>? AttachmentFailures = null,
    int TotalCount = 0,
    int AlreadySameSourceCount = 0)
{
    /// <summary>True only when every tagged file was filed or already the same source.</summary>
    public bool AllFilesTransferred =>
        Outcome == EmailMoveToProjectOutcome.Succeeded
        && FailedCount == 0
        && TotalCount > 0
        && (MovedCount + AlreadySameSourceCount) >= TotalCount;
}
