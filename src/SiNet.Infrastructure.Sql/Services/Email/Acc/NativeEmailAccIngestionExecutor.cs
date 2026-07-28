using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Standalone ACC inbox ingest orchestrator. Uses native Gmail + AccService ports
/// (no SiNetSQL <c>EmailIngestionService</c> / V2 bridge). See <c>docs/NATIVE_EMAIL_ACC_INGEST.md</c>.
/// </summary>
public sealed class NativeEmailAccIngestionExecutor(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IEmailGateway emailGateway,
    IAccInboxBootstrapService inboxBootstrap,
    IAccFolderPathService folderPathService,
    IAccFileUploadService fileUploadService,
    IAppLogger logger) : IEmailAccIngestionExecutor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IEmailGateway _emailGateway =
        emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
    private readonly IAccInboxBootstrapService _inboxBootstrap =
        inboxBootstrap ?? throw new ArgumentNullException(nameof(inboxBootstrap));
    private readonly IAccFolderPathService _folderPathService =
        folderPathService ?? throw new ArgumentNullException(nameof(folderPathService));
    private readonly IAccFileUploadService _fileUploadService =
        fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
    private readonly IAppLogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<EmailAccUploadResult> IngestToInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.GmailMessageId);

        var stopwatch = Stopwatch.StartNew();
        var actingLogin = string.IsNullOrWhiteSpace(command.ActingUserLogin)
            ? Environment.UserName
            : command.ActingUserLogin.Trim();

        // #region agent log
        AgentDebugNdjson.Write(
            "H3",
            "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
            "ingest start",
            new Dictionary<string, object?>
            {
                ["gmailMessageIdLen"] = command.GmailMessageId.Length,
                ["hasInternetMessageId"] = !string.IsNullOrWhiteSpace(command.InternetMessageId),
            });
        // #endregion

        EmailMessageDetails? details;
        try
        {
            details = await _emailGateway
                .GetDetailsAsync(command.GmailMessageId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H3",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "gmail details exception",
                new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().Name, ["message"] = ex.Message });
            // #endregion
            var authMessage = EmailAccIngestGates.MapAuthFailureMessage(ex.Message);
            var mid = EmailMessageIdentity.GetMessageUniqueId(command.InternetMessageId, command.GmailMessageId);
            return Failed(mid, null, authMessage ?? ex.Message, stopwatch.ElapsedMilliseconds);
        }

        if (details is null)
        {
            // #region agent log
            AgentDebugNdjson.Write("H3", "NativeEmailAccIngestionExecutor.IngestToInboxAsync", "gmail details null");
            // #endregion
            var mid = EmailMessageIdentity.GetMessageUniqueId(command.InternetMessageId, command.GmailMessageId);
            return Failed(
                mid,
                null,
                "לא ניתן לטעון את המייל מ-Gmail (לא מחובר או ההודעה לא נמצאה).",
                stopwatch.ElapsedMilliseconds);
        }

        var internetMessageId = FirstNonEmpty(details.InternetMessageId, command.InternetMessageId);
        if (string.IsNullOrWhiteSpace(internetMessageId))
        {
            // #region agent log
            AgentDebugNdjson.Write("H3", "NativeEmailAccIngestionExecutor.IngestToInboxAsync", "missing Message-ID");
            // #endregion
            return Failed(
                null,
                null,
                "Email rejected: missing RFC 2822 Message-ID header.",
                stopwatch.ElapsedMilliseconds);
        }

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(internetMessageId, details.MessageId);
        var attachments = details.Attachments ?? [];
        // #region agent log
        AgentDebugNdjson.Write(
            "H3",
            "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
            "gmail details loaded",
            new Dictionary<string, object?>
            {
                ["attachmentCount"] = attachments.Count,
                ["hasInternetMessageId"] = true,
            });
        // #endregion
        if (attachments.Count == 0)
        {
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.SkippedNoAttachments,
                messageUniqueId,
                null,
                0,
                0,
                null,
                stopwatch.ElapsedMilliseconds);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await db.EmailInboxMessages
            .FirstOrDefaultAsync(m => m.MessageUniqueId == messageUniqueId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            var shortCircuit = await TryShortCircuitAlreadyProcessedAsync(
                    db, existing, attachments.Count, messageUniqueId, stopwatch, cancellationToken)
                .ConfigureAwait(false);
            if (shortCircuit is not null)
            {
                return shortCircuit;
            }

            if (existing.Status == EmailInboxStatus.Processing)
            {
                var leaseAge = existing.ProcessingStartedAtUtc.HasValue
                    ? DateTime.UtcNow - existing.ProcessingStartedAtUtc.Value
                    : TimeSpan.MaxValue;
                if (leaseAge.TotalMinutes < EmailAccLeasePolicy.LeaseTtlMinutes)
                {
                    return new EmailAccUploadResult(
                        EmailAccUploadOutcome.InProgress,
                        messageUniqueId,
                        existing.Id,
                        0,
                        attachments.Count,
                        null,
                        stopwatch.ElapsedMilliseconds);
                }
            }
        }
        else
        {
            var defaultProjectId = await ResolveDefaultOfficeProjectIdAsync(db, cancellationToken)
                .ConfigureAwait(false);
            if (defaultProjectId <= 0)
            {
                return Failed(
                    messageUniqueId,
                    null,
                    "לא נמצא פרויקט ברירת מחדל למשרד (DefaultProjectTitle).",
                    stopwatch.ElapsedMilliseconds);
            }

            var threadUniqueId = EmailMessageIdentity.GetThreadUniqueId(
                details.References, details.InReplyTo, internetMessageId);
            var threadKey = EmailMessageIdentity.GetThreadKey(threadUniqueId);

            existing = new EmailInboxMessage
            {
                MessageUniqueId = messageUniqueId,
                GmailThreadId = FirstNonEmpty(details.ThreadId, command.GmailThreadId),
                InternetMessageId = internetMessageId,
                InReplyTo = details.InReplyTo,
                References = details.References,
                ThreadUniqueId = threadUniqueId,
                ThreadKey = threadKey,
                ProjectId = defaultProjectId,
                Subject = Truncate(details.Subject, 500),
                FromAddress = Truncate(details.From.ToString(), 320),
                ReceivedUtc = details.ReceivedAt == DateTimeOffset.MinValue
                    ? DateTime.UtcNow
                    : details.ReceivedAt.UtcDateTime,
                Status = EmailInboxStatus.Pending,
                CreatedByLogin = actingLogin,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            db.EmailInboxMessages.Add(existing);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                db.Entry(existing).State = EntityState.Detached;
                existing = await db.EmailInboxMessages
                    .FirstOrDefaultAsync(m => m.MessageUniqueId == messageUniqueId, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    return Failed(messageUniqueId, null, "Race on insert — row missing after conflict.", stopwatch.ElapsedMilliseconds);
                }

                var shortCircuit = await TryShortCircuitAlreadyProcessedAsync(
                        db, existing, attachments.Count, messageUniqueId, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
                if (shortCircuit is not null)
                {
                    return shortCircuit;
                }
            }
        }

        bool leaseAcquired;
        try
        {
            leaseAcquired = await TryAcquireLeaseAsync(db, existing, actingLogin, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H6",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "lease acquire threw",
                new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().Name,
                    ["message"] = ex.Message,
                });
            // #endregion
            throw;
        }

        if (!leaseAcquired)
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H6",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "lease not acquired — InProgress");
            // #endregion
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.InProgress,
                messageUniqueId,
                existing.Id,
                0,
                attachments.Count,
                null,
                stopwatch.ElapsedMilliseconds);
        }

        try
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H2",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "calling inbox bootstrap",
                new Dictionary<string, object?> { ["messageDbId"] = existing.Id });
            // #endregion
            var bootstrap = await _inboxBootstrap.EnsureAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bootstrap.AccProjectId)
                || string.IsNullOrWhiteSpace(bootstrap.AccInboxFolderId))
            {
                // #region agent log
                AgentDebugNdjson.Write(
                    "H2",
                    "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                    "bootstrap returned empty ids");
                // #endregion
                return await FailAndReleaseAsync(
                        db,
                        existing,
                        messageUniqueId,
                        attachments.Count,
                        "ACC Inbox bootstrap did not return project/folder ids. Is AccService running?",
                        stopwatch.ElapsedMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var inboxProjectId = bootstrap.AccProjectId;
            var inboxRootFolderId = bootstrap.AccInboxFolderId;
            var messageKey = EmailMessageIdentity.GetMessageKey(messageUniqueId);
            var pathSegments = AccInboxLayout.BuildMessageFolderPath(existing.ThreadKey, messageKey);

            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "ensuring MSG folder path",
                new Dictionary<string, object?>
                {
                    ["pathSegments"] = string.Join("/", pathSegments),
                });
            // #endregion
            var msgFolderId = await _folderPathService
                .EnsurePathAsync(inboxProjectId, inboxRootFolderId, pathSegments, cancellationToken)
                .ConfigureAwait(false);
            var attachmentsFolderId = await _folderPathService
                .EnsurePathAsync(inboxProjectId, msgFolderId, [AccInboxLayout.AttachmentsFolderName], cancellationToken)
                .ConfigureAwait(false);
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "folder path ensured",
                new Dictionary<string, object?>
                {
                    ["hasMsgFolderId"] = !string.IsNullOrWhiteSpace(msgFolderId),
                    ["hasAttachmentsFolderId"] = !string.IsNullOrWhiteSpace(attachmentsFolderId),
                });
            // #endregion

            existing.InboxAccProjectId = inboxProjectId;
            existing.InboxAccFolderId = msgFolderId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var uploadedCount = 0;
            var attachmentIndex = 0;
            foreach (var attachment in attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? tempPath = null;
                try
                {
                    var data = await _emailGateway
                        .DownloadAttachmentAsync(details.MessageId, attachment.AttachmentId, cancellationToken)
                        .ConfigureAwait(false);
                    if (data is null || data.Length == 0)
                    {
                        _logger.Warn($"[NativeAccIngest] Empty attachment skipped: {attachment.FileName}");
                        attachmentIndex++;
                        continue;
                    }

                    var sha256 = EmailMessageIdentity.ComputeSha256Hex(data);
                    var existingAttachment = await db.EmailInboxAttachments
                        .FirstOrDefaultAsync(
                            a => a.MessageId == existing.Id && a.ContentSha256 == sha256,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (existingAttachment is not null && !string.IsNullOrEmpty(existingAttachment.AccItemId))
                    {
                        uploadedCount++;
                        attachmentIndex++;
                        continue;
                    }

                    var safeFileName = EmailMessageIdentity.SanitizeFileName(attachment.FileName);
                    EmailInboxAttachment dbAttachment;
                    if (existingAttachment is not null)
                    {
                        dbAttachment = existingAttachment;
                    }
                    else
                    {
                        dbAttachment = new EmailInboxAttachment
                        {
                            MessageId = existing.Id,
                            AttachmentIndex = attachmentIndex,
                            OriginalFileName = Truncate(attachment.FileName, 260),
                            SavedFileName = Truncate(safeFileName, 260),
                            ContentSha256 = sha256,
                        };
                        db.EmailInboxAttachments.Add(dbAttachment);
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    tempPath = Path.Combine(Path.GetTempPath(), $"email_ingest_{Guid.NewGuid():N}_{safeFileName}");
                    await File.WriteAllBytesAsync(tempPath, data, cancellationToken).ConfigureAwait(false);

                    var upload = await _fileUploadService
                        .UploadAsync(
                            new AccFileUploadRequest(inboxProjectId, tempPath, safeFileName)
                            {
                                TargetFolderId = attachmentsFolderId,
                                ExistingItemId = dbAttachment.AccItemId,
                                SourceIdentity = new AccFileSourceIdentity(
                                    details.MessageId,
                                    existing.ReceivedUtc,
                                    attachment.FileName,
                                    data.LongLength,
                                    sha256,
                                    dbAttachment.Id),
                            },
                            cancellationToken)
                        .ConfigureAwait(false);

                    dbAttachment.AccItemId = upload.ItemId;
                    dbAttachment.AccVersionId = upload.VersionId;
                    dbAttachment.SavedFileName = Truncate(safeFileName, 260);
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    uploadedCount++;
                }
                catch (Exception ex)
                {
                    // #region agent log
                    AgentDebugNdjson.Write(
                        "H5",
                        "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                        "attachment upload failed",
                        new Dictionary<string, object?>
                        {
                            ["fileName"] = attachment.FileName,
                            ["exceptionType"] = ex.GetType().Name,
                            ["message"] = ex.Message,
                        });
                    // #endregion
                    _logger.Warn($"[NativeAccIngest] Attachment failed '{attachment.FileName}': {ex.Message}");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        try { File.Delete(tempPath); }
                        catch { /* best-effort */ }
                    }

                    attachmentIndex++;
                }
            }

            await TryUploadManifestAsync(
                    existing,
                    details,
                    messageKey,
                    inboxProjectId,
                    msgFolderId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (uploadedCount == 0)
            {
                return await FailAndReleaseAsync(
                        db,
                        existing,
                        messageUniqueId,
                        attachments.Count,
                        "לא הועלה אף צרופה ל-ACC.",
                        stopwatch.ElapsedMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            existing.Status = EmailInboxStatus.Uploaded;
            existing.Error = null;
            existing.ProcessingByLogin = null;
            existing.ProcessingStartedAtUtc = null;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"[NativeAccIngest] Uploaded {uploadedCount}/{attachments.Count} for MessageUniqueId={messageUniqueId}");

            // #region agent log
            AgentDebugNdjson.Write(
                "H6",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "ingest succeeded",
                new Dictionary<string, object?>
                {
                    ["uploadedCount"] = uploadedCount,
                    ["totalAttachments"] = attachments.Count,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds,
                    ["inboxMessageId"] = existing.Id,
                });
            // #endregion

            return new EmailAccUploadResult(
                EmailAccUploadOutcome.Succeeded,
                messageUniqueId,
                existing.Id,
                uploadedCount,
                attachments.Count,
                null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H6",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "ingest failed",
                new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().Name,
                    ["message"] = ex.Message,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds,
                });
            // #endregion
            _logger.Warn($"[NativeAccIngest] Failed: {ex.Message}");
            return await FailAndReleaseAsync(
                    db,
                    existing,
                    messageUniqueId,
                    attachments.Count,
                    ex.Message,
                    stopwatch.ElapsedMilliseconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<EmailAccUploadResult?> TryShortCircuitAlreadyProcessedAsync(
        SiNetSQLDbContext db,
        EmailInboxMessage existing,
        int expectedAttachmentCount,
        string messageUniqueId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (existing.Status is not (EmailInboxStatus.Uploaded or EmailInboxStatus.Moved))
        {
            return null;
        }

        var uploadedAttachmentCount = await db.EmailInboxAttachments
            .CountAsync(a => a.MessageId == existing.Id && a.AccItemId != null, cancellationToken)
            .ConfigureAwait(false);

        if (uploadedAttachmentCount > 0 && uploadedAttachmentCount >= expectedAttachmentCount)
        {
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.AlreadyProcessed,
                messageUniqueId,
                existing.Id,
                uploadedAttachmentCount,
                expectedAttachmentCount,
                null,
                stopwatch.ElapsedMilliseconds);
        }

        existing.Status = EmailInboxStatus.Error;
        existing.Error =
            $"DB cache stale: {uploadedAttachmentCount}/{expectedAttachmentCount} attachments have AccItemId. Re-ingesting.";
        existing.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static async Task<bool> TryAcquireLeaseAsync(
        SiNetSQLDbContext db,
        EmailInboxMessage message,
        string currentLogin,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleThreshold = now.AddMinutes(-EmailAccLeasePolicy.LeaseTtlMinutes);

        // Named args required: positional CancellationToken is captured by params object[]
        // and EF then throws "no store type mapping for CancellationToken".
        var rowsAffected = await db.Database.ExecuteSqlRawAsync(
            @"UPDATE EmailInboxMessage 
              SET Status = {0}, 
                  ProcessingByLogin = {1}, 
                  ProcessingStartedAtUtc = {2}, 
                  UpdatedAtUtc = {3}
              WHERE Id = {4} 
                AND (Status = {5} OR Status = {6} 
                     OR (Status = {7} AND ProcessingStartedAtUtc < {8}))",
            cancellationToken: cancellationToken,
            parameters:
            [
                (int)EmailInboxStatus.Processing,
                currentLogin,
                now,
                now,
                message.Id,
                (int)EmailInboxStatus.Pending,
                (int)EmailInboxStatus.Error,
                (int)EmailInboxStatus.Processing,
                staleThreshold,
            ]).ConfigureAwait(false);

        // #region agent log
        AgentDebugNdjson.Write(
            "H6",
            "NativeEmailAccIngestionExecutor.TryAcquireLeaseAsync",
            "lease update completed",
            new Dictionary<string, object?>
            {
                ["rowsAffected"] = rowsAffected,
                ["messageId"] = message.Id,
            });
        // #endregion

        if (rowsAffected <= 0)
        {
            return false;
        }

        await db.Entry(message).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task TryUploadManifestAsync(
        EmailInboxMessage message,
        EmailMessageDetails details,
        string messageKey,
        string inboxProjectId,
        string msgFolderId,
        CancellationToken cancellationToken)
    {
        string? manifestPath = null;
        try
        {
            var payload = new
            {
                messageKey,
                message.MessageUniqueId,
                message.ThreadKey,
                message.InternetMessageId,
                gmailMessageId = details.MessageId,
                subject = details.Subject,
                from = details.From.ToString(),
                receivedUtc = message.ReceivedUtc,
                attachmentCount = details.Attachments.Count,
            };
            manifestPath = Path.Combine(Path.GetTempPath(), $"manifest_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(
                    manifestPath,
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);

            await _fileUploadService
                .UploadAsync(
                    new AccFileUploadRequest(inboxProjectId, manifestPath, AccInboxLayout.ManifestFileName)
                    {
                        TargetFolderId = msgFolderId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[NativeAccIngest] Manifest upload skipped (non-fatal): {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrEmpty(manifestPath))
            {
                try { File.Delete(manifestPath); }
                catch { /* best-effort */ }
            }
        }
    }

    private static async Task<EmailAccUploadResult> FailAndReleaseAsync(
        SiNetSQLDbContext db,
        EmailInboxMessage message,
        string messageUniqueId,
        int totalAttachments,
        string error,
        long durationMs,
        CancellationToken cancellationToken)
    {
        message.Status = EmailInboxStatus.Error;
        message.Error = Truncate(error, 2000);
        message.ProcessingByLogin = null;
        message.ProcessingStartedAtUtc = null;
        message.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Still return Failed to the caller.
        }

        return Failed(messageUniqueId, message.Id, error, durationMs, totalAttachments);
    }

    private static EmailAccUploadResult Failed(
        string? messageUniqueId,
        int? inboxMessageId,
        string error,
        long durationMs,
        int totalAttachments = 0) =>
        new(EmailAccUploadOutcome.Failed, messageUniqueId, inboxMessageId, 0, totalAttachments, error, durationMs);

    private static async Task<int> ResolveDefaultOfficeProjectIdAsync(
        SiNetSQLDbContext db,
        CancellationToken cancellationToken)
    {
        var defaultTitle = await db.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.SettingKey == SystemSettingKeys.DefaultProjectTitle)
            .Select(setting => setting.SettingValue)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(defaultTitle))
        {
            defaultTitle = SystemSettingsDefaults.DefaultProjectTitle;
        }

        return await db.Projects
            .AsNoTracking()
            .Where(project => project.Title == defaultTitle)
            .Select(project => project.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || message.Contains("2601", StringComparison.Ordinal)
               || message.Contains("2627", StringComparison.Ordinal);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
