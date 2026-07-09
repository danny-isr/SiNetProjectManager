namespace SiNet.Application.Email.Detail;

public interface IEmailMoveToProjectEligibilityService
{
    Task<EmailMoveToProjectEligibility> EvaluateAsync(
        EmailMoveToProjectEligibilityQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record EmailMoveToProjectEligibilityQuery(
    int InboxMessageId,
    int ProjectId,
    int AttachmentCount,
    bool IsEmailFiledToProject);

public sealed record EmailMoveToProjectEligibility(
    bool CanMove,
    string? BlockReason);
