using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email.Acc;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Native ACC inbox recovery: clear stale Acc ids, then re-ingest via N1.
/// See <c>docs/NATIVE_EMAIL_ACC_INGEST.md</c> N3.
/// </summary>
public sealed class NativeEmailAccRecoveryExecutor(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IEmailAccIngestionExecutor ingestionExecutor,
    IAppLogger logger) : IEmailAccRecoveryExecutor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IEmailAccIngestionExecutor _ingestionExecutor =
        ingestionExecutor ?? throw new ArgumentNullException(nameof(ingestionExecutor));
    private readonly IAppLogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task RecoverMissingAttachmentsAsync(
        int inboxMessageId,
        string gmailMessageId,
        IReadOnlyList<int> missingAttachmentIds,
        string actingUserLogin,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gmailMessageId);
        missingAttachmentIds ??= Array.Empty<int>();
        if (missingAttachmentIds.Count == 0)
        {
            return;
        }

        string? internetMessageId;
        string gmailThreadId;
        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var message = await db.EmailInboxMessages
                .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken)
                .ConfigureAwait(false);
            if (message is null)
            {
                _logger.Warn($"[InboxRecovery] Message not found. Id={inboxMessageId}");
                return;
            }

            var attachmentsToRecover = await db.EmailInboxAttachments
                .Where(a => a.MessageId == inboxMessageId && missingAttachmentIds.Contains(a.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.Info(
                $"[InboxRecovery] START EmailInboxMessageId={inboxMessageId} " +
                $"MissingAttachmentIds=[{string.Join(",", missingAttachmentIds)}] " +
                $"Found={attachmentsToRecover.Count}");

            // External downloads (Jumbo/WeTransfer) are not Gmail attachments.
            // Clearing AccItemId + ingest would wipe valid URNs without re-upload.
            if (attachmentsToRecover.Count > 0
                && attachmentsToRecover.All(static a => a.IsExternalDownload))
            {
                _logger.Warn(
                    $"[InboxRecovery] SKIP external-download-only recovery. " +
                    $"EmailInboxMessageId={inboxMessageId} " +
                    $"AttachmentIds=[{string.Join(",", attachmentsToRecover.Select(a => a.Id))}] " +
                    "Reason=Cannot re-ingest link-only files via Gmail. " +
                    "העלאה חיצונית חלקית או נכשלה — לא ניתן לשחזר מקבצי Gmail. נסו להוריד שוב מהקישור.");
                return;
            }

            foreach (var att in attachmentsToRecover)
            {
                att.AccItemId = null;
                att.AccVersionId = null;
            }

            if (message.Status is EmailInboxStatus.Uploaded or EmailInboxStatus.Moved)
            {
                message.Status = EmailInboxStatus.Error;
                message.Error = "[InboxRecovery] Forced retry. Reason=MissingInAcc";
                message.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            internetMessageId = message.InternetMessageId;
            gmailThreadId = message.GmailThreadId ?? string.Empty;
        }

        var login = string.IsNullOrWhiteSpace(actingUserLogin)
            ? Environment.UserName
            : actingUserLogin.Trim();

        var command = new EmailAccUploadCommand(
            GmailMessageId: gmailMessageId.Trim(),
            GmailThreadId: gmailThreadId,
            InternetMessageId: internetMessageId,
            ActingUserLogin: login);

        EmailAccUploadResult ingestResult;
        try
        {
            _logger.Info(
                $"[InboxRecovery] REUPLOAD invoking IngestToInboxAsync. " +
                $"EmailInboxMessageId={inboxMessageId} GmailMessageId={gmailMessageId}");
            ingestResult = await _ingestionExecutor
                .IngestToInboxAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[InboxRecovery] IngestToInboxAsync threw: {ex.Message}", ex);
            return;
        }

        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var refreshed = await db.EmailInboxAttachments
                .AsNoTracking()
                .Where(a => a.MessageId == inboxMessageId && missingAttachmentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccItemId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var recovered = 0;
            var failed = 0;
            foreach (var id in missingAttachmentIds)
            {
                var row = refreshed.FirstOrDefault(a => a.Id == id);
                if (row is not null && !string.IsNullOrEmpty(row.AccItemId))
                {
                    recovered++;
                    _logger.Info(
                        $"[InboxRecovery] DB UPDATED EmailInboxMessageId={inboxMessageId} " +
                        $"AttachmentId={id} AccItemId={row.AccItemId}");
                }
                else
                {
                    failed++;
                }
            }

            if (failed > 0 || !ingestResult.Succeeded)
            {
                _logger.Warn(
                    $"[InboxRecovery] END incomplete. EmailInboxMessageId={inboxMessageId} " +
                    $"Recovered={recovered} Failed={failed} " +
                    $"IngestSucceeded={ingestResult.Succeeded} " +
                    $"Error={ingestResult.ErrorMessage ?? "(none)"}");
            }
            else
            {
                _logger.Info(
                    $"[InboxRecovery] END success. EmailInboxMessageId={inboxMessageId} " +
                    $"Recovered={recovered}");
            }
        }
    }
}
