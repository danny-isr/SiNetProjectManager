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
    IReadOnlyList<SiNet.Application.Email.Acc.EmailMoveToProjectAttachmentFailure>? AttachmentFailures = null);
