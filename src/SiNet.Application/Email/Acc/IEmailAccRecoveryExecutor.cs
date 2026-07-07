namespace SiNet.Application.Email.Acc;

/// <summary>
/// Host bridge: re-uploads missing ACC inbox attachments discovered by reconciliation.
/// </summary>
public interface IEmailAccRecoveryExecutor
{
    Task RecoverMissingAttachmentsAsync(
        int inboxMessageId,
        string gmailMessageId,
        IReadOnlyList<int> missingAttachmentIds,
        string actingUserLogin,
        CancellationToken cancellationToken = default);
}
