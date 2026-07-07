using SiNet.Application.Email.Acc;
using SiNetSQL.Services.EmailIngestion;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host bridge: delegates ACC inbox recovery to legacy <see cref="IAccInboxRecoveryService"/>.
/// </summary>
internal sealed class LegacyEmailAccRecoveryExecutor(IAccInboxRecoveryService recoveryService)
    : IEmailAccRecoveryExecutor
{
    private readonly IAccInboxRecoveryService _recoveryService =
        recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));

    public Task RecoverMissingAttachmentsAsync(
        int inboxMessageId,
        string gmailMessageId,
        IReadOnlyList<int> missingAttachmentIds,
        string actingUserLogin,
        CancellationToken cancellationToken = default)
    {
        if (missingAttachmentIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _recoveryService.RecoverMessageAsync(
            inboxMessageId,
            gmailMessageId,
            missingAttachmentIds,
            AccInboxRecoveryReason.MissingInAcc,
            actingUserLogin,
            cancellationToken);
    }
}
