using SiNet.Application.Email.Acc;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host bridge: delegates ACC inbox recovery to legacy <see cref="IAccInboxRecoveryService"/>.
/// </summary>
internal sealed class LegacyEmailAccRecoveryExecutor(
    IAccInboxRecoveryService recoveryService,
    IGoogleIngestSessionEnsurer? sessionEnsurer = null)
    : IEmailAccRecoveryExecutor
{
    private readonly IAccInboxRecoveryService _recoveryService =
        recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
    private readonly IGoogleIngestSessionEnsurer? _sessionEnsurer = sessionEnsurer;

    public async Task RecoverMissingAttachmentsAsync(
        int inboxMessageId,
        string gmailMessageId,
        IReadOnlyList<int> missingAttachmentIds,
        string actingUserLogin,
        CancellationToken cancellationToken = default)
    {
        if (missingAttachmentIds.Count == 0)
        {
            return;
        }

        // #region agent log
        try
        {
            var dbg = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = "487a8a",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                runId = "post-fix-inbox",
                hypothesisId = "H-G",
                location = "LegacyEmailAccRecoveryExecutor.RecoverMissingAttachmentsAsync",
                message = "recovery starting; ensuring Gmail auth",
                data = new
                {
                    inboxMessageId,
                    missingCount = missingAttachmentIds.Count,
                    hasEnsurer = _sessionEnsurer is not null,
                },
            });
            System.IO.File.AppendAllText(@"d:\repos2026\debug-487a8a.log", dbg + Environment.NewLine);
        }
        catch { }
        // #endregion

        if (_sessionEnsurer is not null)
        {
            var ready = await _sessionEnsurer
                .EnsureAuthenticatedForAccIngestAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!ready)
            {
                AppLogger.Warn(
                    "[InboxRecovery] Gmail legacy session not ready — skipping recovery until reconnect.");
                return;
            }
        }

        await _recoveryService.RecoverMessageAsync(
            inboxMessageId,
            gmailMessageId,
            missingAttachmentIds,
            AccInboxRecoveryReason.MissingInAcc,
            actingUserLogin,
            cancellationToken).ConfigureAwait(false);
    }
}
