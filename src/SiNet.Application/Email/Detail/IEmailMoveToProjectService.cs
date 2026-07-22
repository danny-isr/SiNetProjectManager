namespace SiNet.Application.Email.Detail;

public interface IEmailMoveToProjectService
{
    Task<EmailMoveToProjectResult> MoveAsync(
        EmailMoveToProjectDetailCommand command,
        CancellationToken cancellationToken = default);

    bool IsAvailable { get; }
}

public sealed record EmailMoveToProjectDetailCommand(
    int InboxMessageId,
    int ProjectId,
    int? TaskId,
    string? TaskResultCode);

public sealed record EmailMoveToProjectResult(
    bool Succeeded,
    string Message,
    int MovedCount,
    IReadOnlyList<SiNet.Application.Email.Acc.EmailMoveToProjectAttachmentFailure>? AttachmentFailures = null,
    int FailedCount = 0,
    int TotalCount = 0,
    int AlreadySameSourceCount = 0)
{
    /// <summary>True only when every tagged file was filed or already the same source.</summary>
    public bool AllFilesTransferred =>
        Succeeded
        && FailedCount == 0
        && TotalCount > 0
        && (MovedCount + AlreadySameSourceCount) >= TotalCount;
}
