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
public sealed class NativeEmailAccIngestionExecutor : IEmailAccIngestionExecutor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IEmailGateway _emailGateway;
    private readonly IAccInboxBootstrapService _inboxBootstrap;
    private readonly IAccFolderPathService _folderPathService;
    private readonly IAccFileUploadService _fileUploadService;
    private readonly IAppLogger _logger;
    private readonly IEmailBodyPdfRenderer? _bodyPdfRenderer;

    public NativeEmailAccIngestionExecutor(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IEmailGateway emailGateway,
        IAccInboxBootstrapService inboxBootstrap,
        IAccFolderPathService folderPathService,
        IAccFileUploadService fileUploadService,
        IAppLogger logger,
        IEmailBodyPdfRenderer? bodyPdfRenderer = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _emailGateway = emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
        _inboxBootstrap = inboxBootstrap ?? throw new ArgumentNullException(nameof(inboxBootstrap));
        _folderPathService = folderPathService ?? throw new ArgumentNullException(nameof(folderPathService));
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bodyPdfRenderer = bodyPdfRenderer;

        // #region agent log
        AgentDebugNdjson.Write(
            "H1",
            "NativeEmailAccIngestionExecutor.ctor",
            "executor constructed",
            new { rendererRegistered = bodyPdfRenderer is not null });
        // #endregion
    }

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
            "H1",
            "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
            "ingest enter",
            new
            {
                gmailMessageId = command.GmailMessageId,
                rendererRegistered = _bodyPdfRenderer is not null,
                rendererAvailable = _bodyPdfRenderer?.IsAvailable,
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
            var authMessage = EmailAccIngestGates.MapAuthFailureMessage(ex.Message);
            var mid = EmailMessageIdentity.GetMessageUniqueId(command.InternetMessageId, command.GmailMessageId);
            return Failed(mid, null, authMessage ?? ex.Message, stopwatch.ElapsedMilliseconds);
        }

        if (details is null)
        {
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
            return Failed(
                null,
                null,
                "Email rejected: missing RFC 2822 Message-ID header.",
                stopwatch.ElapsedMilliseconds);
        }

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(internetMessageId, details.MessageId);
        var attachments = details.Attachments ?? [];

        // N4.3: zero-attachment ingest only when mailbox-filed / recovery / post-File (AllowZeroAttachmentIngest).
        if (attachments.Count == 0 && !command.AllowZeroAttachmentIngest)
        {
            _logger.Info(
                $"[NativeAccIngest] Skip — no attachments and AllowZeroAttachmentIngest=false MessageUniqueId={messageUniqueId}");
            // #region agent log
            AgentDebugNdjson.Write(
                "H10",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "skip not eligible N4.3",
                new { messageUniqueId, gmailMessageId = command.GmailMessageId },
                runId: "post-fix");
            // #endregion
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.SkippedNotRelevant,
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
                // #region agent log
                AgentDebugNdjson.Write(
                    "H2",
                    "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                    "short-circuit return",
                    new
                    {
                        messageUniqueId,
                        inboxId = existing.Id,
                        status = existing.Status.ToString(),
                        outcome = shortCircuit.Outcome.ToString(),
                        folderId = existing.InboxAccFolderId,
                    });
                // #endregion
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

        bool leaseAcquired = await TryAcquireLeaseAsync(db, existing, actingLogin, cancellationToken)
            .ConfigureAwait(false);

        if (!leaseAcquired)
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

        try
        {
            var bootstrap = await _inboxBootstrap.EnsureAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bootstrap.AccProjectId)
                || string.IsNullOrWhiteSpace(bootstrap.AccInboxFolderId))
            {
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

            var msgFolderId = await _folderPathService
                .EnsurePathAsync(inboxProjectId, inboxRootFolderId, pathSegments, cancellationToken)
                .ConfigureAwait(false);
            var attachmentsFolderId = await _folderPathService
                .EnsurePathAsync(inboxProjectId, msgFolderId, [AccInboxLayout.AttachmentsFolderName], cancellationToken)
                .ConfigureAwait(false);

            existing.InboxAccProjectId = inboxProjectId;
            existing.InboxAccFolderId = msgFolderId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // #region agent log
            AgentDebugNdjson.Write(
                "H5",
                "NativeEmailAccIngestionExecutor.IngestToInboxAsync",
                "before TryUploadBodyPdfAsync",
                new
                {
                    inboxId = existing.Id,
                    msgFolderId,
                    attachmentCount = attachments.Count,
                    htmlLen = details.HtmlBody?.Length ?? 0,
                    textLen = details.BodyText?.Length ?? 0,
                });
            // #endregion

            await TryUploadBodyPdfAsync(
                    db,
                    existing,
                    details,
                    inboxProjectId,
                    msgFolderId,
                    cancellationToken)
                .ConfigureAwait(false);

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

            // Succeed when the message folder exists on ACC. Attachments are optional (N4);
            // if Gmail reported attachments but none uploaded, fail.
            if (string.IsNullOrWhiteSpace(existing.InboxAccFolderId))
            {
                return await FailAndReleaseAsync(
                        db,
                        existing,
                        messageUniqueId,
                        attachments.Count,
                        "לא נוצרה תיקיית הודעה ב-ACC Inbox.",
                        stopwatch.ElapsedMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (attachments.Count > 0 && uploadedCount == 0)
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

        var bodyPdfReady = await db.EmailInboxAttachments
            .AsNoTracking()
            .AnyAsync(
                a => a.MessageId == existing.Id
                     && a.AttachmentIndex == AccInboxLayout.EmailBodyAttachmentIndex
                     && a.AccItemId != null,
                cancellationToken)
            .ConfigureAwait(false);

        // N4.1: never treat Uploaded+folder as final when 00_Email.pdf is still missing and a
        // renderer exists — first ingest often raced WebView2 init and skipped the body PDF.
        // Do not demote Moved (project filing already completed).
        if (!bodyPdfReady
            && _bodyPdfRenderer is not null
            && existing.Status == EmailInboxStatus.Uploaded)
        {
            _logger.Info(
                $"[NativeAccIngest] Body PDF missing for MessageUniqueId={messageUniqueId} — re-ingesting (not AlreadyProcessed).");
            // #region agent log
            AgentDebugNdjson.Write(
                "H2",
                "TryShortCircuitAlreadyProcessedAsync",
                "force re-ingest missing body PDF",
                new { messageUniqueId, inboxId = existing.Id, status = existing.Status.ToString() });
            // #endregion
            existing.Status = EmailInboxStatus.Error;
            existing.Error = "Missing 00_Email.pdf — retrying body PDF upload.";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!bodyPdfReady && existing.Status == EmailInboxStatus.Moved)
        {
            _logger.Warn(
                $"[NativeAccIngest] Body PDF missing on Moved message MessageUniqueId={messageUniqueId} — not demoting; open ACC MSG folder to verify.");
        }

        // Zero-attachment messages: folder + (body PDF or no renderer) = done.
        if (expectedAttachmentCount == 0
            && !string.IsNullOrWhiteSpace(existing.InboxAccFolderId))
        {
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.AlreadyProcessed,
                messageUniqueId,
                existing.Id,
                0,
                0,
                null,
                stopwatch.ElapsedMilliseconds);
        }

        var uploadedAttachmentCount = await db.EmailInboxAttachments
            .CountAsync(
                a => a.MessageId == existing.Id
                     && a.AccItemId != null
                     && a.AttachmentIndex != AccInboxLayout.EmailBodyAttachmentIndex,
                cancellationToken)
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

    private async Task TryUploadBodyPdfAsync(
        SiNetSQLDbContext db,
        EmailInboxMessage message,
        EmailMessageDetails details,
        string inboxProjectId,
        string msgFolderId,
        CancellationToken cancellationToken)
    {
        if (_bodyPdfRenderer is null)
        {
            _logger.Warn("[NativeAccIngest] Body PDF renderer not registered — skipping 00_Email.pdf.");
            // #region agent log
            AgentDebugNdjson.Write("H1", "TryUploadBodyPdfAsync", "skip renderer null", new { inboxId = message.Id });
            // #endregion
            return;
        }

        var htmlBody = details.HtmlBody;
        var textBody = details.BodyText;
        var hasHtml = !string.IsNullOrWhiteSpace(htmlBody);
        var hasText = !string.IsNullOrWhiteSpace(textBody);
        if (!hasHtml && !hasText)
        {
            _logger.Info("[NativeAccIngest] Email has no body content — skipping 00_Email.pdf.");
            // #region agent log
            AgentDebugNdjson.Write("H3", "TryUploadBodyPdfAsync", "skip empty body", new { inboxId = message.Id });
            // #endregion
            return;
        }

        string? pdfPath = null;
        try
        {
            var existingRow = await db.EmailInboxAttachments
                .FirstOrDefaultAsync(
                    a => a.MessageId == message.Id
                         && a.AttachmentIndex == AccInboxLayout.EmailBodyAttachmentIndex,
                    cancellationToken)
                .ConfigureAwait(false);

            // N4.3: do not re-render/re-upload when ACC already has 00_Email.pdf.
            if (existingRow is not null && !string.IsNullOrEmpty(existingRow.AccItemId))
            {
                // #region agent log
                AgentDebugNdjson.Write(
                    "H2",
                    "TryUploadBodyPdfAsync",
                    "skip already has AccItemId",
                    new { inboxId = message.Id, accItemId = existingRow.AccItemId },
                    runId: "post-fix");
                // #endregion
                return;
            }

            pdfPath = Path.Combine(Path.GetTempPath(), $"email_body_{Guid.NewGuid():N}.pdf");
            var inlineCount = details.InlineImages?.Count ?? 0;
            var htmlDocument = EmailBodyHtmlDocumentBuilder.Build(
                details.Subject,
                details.From.ToString(),
                details.ReceivedAt,
                details.InternetMessageId ?? message.InternetMessageId,
                hasHtml ? htmlBody! : textBody!,
                isPlainTextFallback: !hasHtml);

            // #region agent log
            AgentDebugNdjson.Write(
                "H6",
                "TryUploadBodyPdfAsync",
                "render with inline images",
                new
                {
                    inboxId = message.Id,
                    inlineCount,
                    htmlLen = htmlDocument.Length,
                    hasCidLeft = htmlDocument.Contains("cid:", StringComparison.OrdinalIgnoreCase),
                    existingAccItemId = existingRow?.AccItemId,
                },
                runId: "post-fix");
            // #endregion

            _logger.Info(
                $"[NativeAccIngest] Rendering body PDF HtmlLen={htmlDocument.Length} inline={inlineCount} rendererAvailable={_bodyPdfRenderer.IsAvailable}");
            var generated = await _bodyPdfRenderer
                .RenderHtmlToPdfAsync(htmlDocument, pdfPath, details.InlineImages, cancellationToken)
                .ConfigureAwait(false);

            if (!generated || !File.Exists(pdfPath))
            {
                _logger.Warn(
                    $"[NativeAccIngest] Body PDF render failed (non-fatal). available={_bodyPdfRenderer.IsAvailable} pathExists={File.Exists(pdfPath)}");
                // #region agent log
                AgentDebugNdjson.Write(
                    "H4",
                    "TryUploadBodyPdfAsync",
                    "render failed",
                    new
                    {
                        inboxId = message.Id,
                        available = _bodyPdfRenderer.IsAvailable,
                        pathExists = File.Exists(pdfPath),
                        htmlLen = htmlDocument.Length,
                        plainText = !hasHtml,
                    });
                // #endregion
                return;
            }

            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
            var sha256 = EmailMessageIdentity.ComputeSha256Hex(pdfBytes);
            var fileName = AccInboxLayout.EmailBodyFileName;

            EmailInboxAttachment dbAttachment;
            if (existingRow is not null)
            {
                dbAttachment = existingRow;
                dbAttachment.ContentSha256 = sha256;
                dbAttachment.SavedFileName = fileName;
            }
            else
            {
                dbAttachment = new EmailInboxAttachment
                {
                    MessageId = message.Id,
                    AttachmentIndex = AccInboxLayout.EmailBodyAttachmentIndex,
                    OriginalFileName = fileName,
                    SavedFileName = fileName,
                    ContentSha256 = sha256,
                };
                db.EmailInboxAttachments.Add(dbAttachment);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // H9 (runtime): AccService returns HTTP 500 when versioning body PDF via ExistingItemId
            // for many messages; first upload (null ExistingItemId) succeeds. Retry as a new file.
            var existingItemId = dbAttachment.AccItemId;
            AccFileUploadResult upload;
            try
            {
                upload = await _fileUploadService
                    .UploadAsync(
                        new AccFileUploadRequest(inboxProjectId, pdfPath, fileName)
                        {
                            TargetFolderId = msgFolderId,
                            ExistingItemId = existingItemId,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (!string.IsNullOrWhiteSpace(existingItemId))
            {
                // #region agent log
                AgentDebugNdjson.Write(
                    "H9",
                    "TryUploadBodyPdfAsync",
                    "version upload failed — retry without ExistingItemId",
                    new
                    {
                        inboxId = message.Id,
                        existingItemId,
                        errorType = ex.GetType().Name,
                        error = ex.Message,
                    },
                    runId: "post-fix");
                // #endregion
                _logger.Warn(
                    $"[NativeAccIngest] Body PDF version upload failed ({ex.Message}) — retrying as new file.");
                upload = await _fileUploadService
                    .UploadAsync(
                        new AccFileUploadRequest(inboxProjectId, pdfPath, fileName)
                        {
                            TargetFolderId = msgFolderId,
                            ExistingItemId = null,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            dbAttachment.AccItemId = upload.ItemId;
            dbAttachment.AccVersionId = upload.VersionId;
            message.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"[NativeAccIngest] Body PDF uploaded AccItemId={upload.ItemId}");
            // #region agent log
            AgentDebugNdjson.Write(
                "H5",
                "TryUploadBodyPdfAsync",
                "upload succeeded",
                new
                {
                    inboxId = message.Id,
                    accItemId = upload.ItemId,
                    msgFolderId,
                    bytes = pdfBytes.Length,
                    retriedWithoutExistingItemId = !string.IsNullOrWhiteSpace(existingItemId)
                        && !string.Equals(existingItemId, upload.ItemId, StringComparison.Ordinal),
                },
                runId: "post-fix");
            // #endregion
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[NativeAccIngest] Body PDF skipped (non-fatal): {ex.Message}");
            // #region agent log
            AgentDebugNdjson.Write(
                "H5",
                "TryUploadBodyPdfAsync",
                "upload/exception",
                new { inboxId = message.Id, errorType = ex.GetType().Name, error = ex.Message },
                runId: "post-fix");
            // #endregion
        }
        finally
        {
            if (!string.IsNullOrEmpty(pdfPath))
            {
                try { File.Delete(pdfPath); }
                catch { /* best-effort */ }
            }
        }
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
