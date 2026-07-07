namespace SiNet.Application.Email.Acc;

/// <summary>
/// Move tagged inbox attachments from ACC Inbox to the target project via the existing handler pipeline.
/// </summary>
public sealed record EmailMoveToProjectCommand(
    int InboxMessageId,
    int ProjectId,
    int? UserId = null,
    int? TaskId = null);
