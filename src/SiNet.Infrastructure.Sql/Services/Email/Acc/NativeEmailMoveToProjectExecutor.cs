using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Autodesk.Metadata;
using SiNet.Application.Email.Acc;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Native MoveToProject backend. Files every <b>required</b> tagged business attachment
/// (ProjectFile tag; AccItemId may still be missing — counted in TotalCount) through
/// <see cref="IProjectFileFilingService"/>, stamps ACC Move/Lock metadata, and flips the
/// message to <see cref="EmailInboxStatus.Moved"/> only when every required item is filed
/// or verified at the current target with Move/Lock metadata complete.
/// <para>
/// Backend-only: no WPF/UI. When <c>TaskId</c> is present, task close is owned by the UI
/// (<c>EmailDetailViewModel</c> → <see cref="ITaskCompletionService"/>); executor reporting
/// is inactive for that path (FileMaterial six decisions 2026-08).
/// </para>
/// </summary>
public sealed class NativeEmailMoveToProjectExecutor(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IProjectFileFilingService filingService,
    IAccFileDownloadService accDownloadService,
    IAccFileUploadService accUploadService,
    IAccFolderBrowserService folderBrowserService,
    IAccItemMetadataService metadataService,
    ITaskCompletionService taskCompletionService,
    IEmailAccStatusService? accStatusService = null,
    IEmailAccRecoveryExecutor? recoveryExecutor = null) : IEmailMoveToProjectExecutor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;
    private readonly IProjectFileFilingService _filingService = filingService;
    private readonly IAccFileDownloadService _accDownloadService = accDownloadService;
    private readonly IAccFileUploadService _accUploadService = accUploadService;
    private readonly IAccFolderBrowserService _folderBrowserService = folderBrowserService;
    private readonly IAccItemMetadataService _metadataService = metadataService;
    private readonly ITaskCompletionService _taskCompletionService = taskCompletionService;
    private readonly IEmailAccStatusService? _accStatusService = accStatusService;
    private readonly IEmailAccRecoveryExecutor? _recoveryExecutor = recoveryExecutor;

    private sealed record InboxItem(string ItemId, string DisplayName);

    public async Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emailMessageId = command.InboxMessageId;
        if (emailMessageId <= 0)
        {
            return Failed("EmailMessageId is required.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var message = await db.EmailInboxMessages
            .Include(m => m.Attachments)
            .FirstOrDefaultAsync(m => m.Id == emailMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return Failed("ההודעה לא נמצאה.");
        }

        var projectId = command.ProjectId > 0 ? command.ProjectId : message.ProjectId;
        if (projectId <= 0)
        {
            return Deferred("ההודעה אינה משויכת לפרויקט. יש לשייך תחילה לפרויקט.");
        }

        // INACTIVE (FileMaterial six decisions 2026-08): empty-email auto-Moved shortcut.
        // Empty / no-business-attachments must go through UI (include 00_Email.pdf or confirm no material).
        // Previously: message.Status = Moved + Succeeded "ההודעה תויקה (ללא קבצים)."
        if (message.Attachments.Count == 0)
        {
            return Deferred(
                "אין צרופות להעברה.\n" +
                "ניתן לבחור «תוכן המייל (PDF)» לתיוק, או לאשר שאין חומר ממסך התיוק.");
        }

        // Reconcile + Gmail recovery for missing AccItemId before counting/filing.
        await TryReconcileAndRecoverAsync(db, message, command, cancellationToken).ConfigureAwait(false);

        // Reload attachments after recovery may have filled AccItemId.
        message = await db.EmailInboxMessages
            .Include(m => m.Attachments)
            .FirstOrDefaultAsync(m => m.Id == emailMessageId, cancellationToken)
            .ConfigureAwait(false);
        if (message is null)
        {
            return Failed("ההודעה לא נמצאה.");
        }

        // Required = tagged business items (incl. missing AccItemId). Body PDF only if tagged.
        var taggedNeedingFiling = message.Attachments
            .Where(IsRequiredBusinessAttachment)
            .ToList();

        // TEMP WF-DEBUG
        var withProjectFileId = message.Attachments.Count(a => a.ProjectFileId.HasValue);
        var withAccItemId = message.Attachments.Count(a => !string.IsNullOrEmpty(a.AccItemId));
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move",
            $"executor state inbox={message.Id} project={projectId} attachments={message.Attachments.Count} tagged={taggedNeedingFiling.Count} withProjectFileId={withProjectFileId} withAccItemId={withAccItemId} accProjectId={(string.IsNullOrEmpty(message.InboxAccProjectId) ? "(missing)" : "present")} accFolderId={(string.IsNullOrEmpty(message.InboxAccFolderId) ? "(missing)" : "present")} status={message.Status} task={command.TaskId?.ToString() ?? "(none)"}");

        // Never treat "nothing to file" as success — including when a TaskId is present.
        if (taggedNeedingFiling.Count == 0)
        {
            var deferredMessage =
                "אין קבצים מתויגים ליעד בפרויקט.\n" +
                "ליד כל צרופה לחץ «בחר קובץ», בחר יעד ואלטרנטיבה, ואז העבר לפרויקט.\n" +
                "אם אין צרופות עסקיות — בחר תוכן המייל (PDF) או אשר שאין חומר.";

            SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move",
                $"executor DEFERRED empty-tagged: {deferredMessage.Replace('\n', ' ')}");
            return Deferred(deferredMessage);
        }

        // Duplicate-target validation — backend-only, no MessageBox.
        var dupItems = taggedNeedingFiling
            .Select(a => (
                Target: new FilingTargetDuplicateValidator.TargetKey(
                    a.ProjectFileId!.Value,
                    a.ProjectAlternativeId is > 0 ? a.ProjectAlternativeId : null),
                SourceLabel: a.OriginalFileName ?? a.SavedFileName ?? $"AttId={a.Id}"));
        var duplicateGroups = FilingTargetDuplicateValidator.FindDuplicates(dupItems);
        if (duplicateGroups.Count > 0)
        {
            var details = FilingTargetDuplicateValidator.FormatDetails(duplicateGroups);
            Trace.TraceWarning($"[MoveToProject] Blocked: duplicate target tags in same email. {details}");
            return Deferred(FilingTargetDuplicateValidator.UserMessageHebrew);
        }

        if (string.IsNullOrEmpty(message.InboxAccProjectId))
        {
            return Failed("לא נמצא פרויקט ACC קולט עבור ההודעה.");
        }

        int movedCount = 0;
        int failedCount = 0;
        int alreadySameSourceCount = 0;
        var filedInboxAttachmentIdsThisRun = new List<int>();
        var attachmentFailures = new List<EmailMoveToProjectAttachmentFailure>();
        IReadOnlyList<InboxItem>? messageFolderItems = null;
        IReadOnlyList<InboxItem>? attachmentsFolderItems = null;
        string? attachmentsFolderId = null;
        var attachmentsFolderResolved = false;

        foreach (var att in taggedNeedingFiling)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? tempLocalPath = null;
            try
            {
                bool isZipFolder = string.IsNullOrEmpty(att.AccItemId)
                    && !string.IsNullOrEmpty(att.AccVersionId)
                    && att.OriginalFileName != null
                    && att.OriginalFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(att.AccItemId) && !isZipFolder)
                {
                    if (string.IsNullOrEmpty(message.InboxAccFolderId))
                    {
                        Trace.TraceWarning(
                            $"[MoveToProject] Missing InboxAccFolderId for email Id={message.Id} and attachment Id={att.Id} has no AccItemId; cannot verify in ACC Inbox.");
                        // TEMP WF-DEBUG
                        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"att={att.Id} '{att.OriginalFileName}' FAILED: missing InboxAccFolderId and no AccItemId");
                        RecordFailure(attachmentFailures, ref failedCount, att, "MissingInAcc", "חסר InboxAccFolderId ואין AccItemId");
                        continue;
                    }

                    var role = AccInboxLayout.GetRole(
                        att.AttachmentIndex,
                        att.SavedFileName ?? att.OriginalFileName,
                        att.IsExternalDownload);
                    var useAttachmentsFolder = AccInboxLayout.UsesAttachmentsFolder(role);

                    messageFolderItems ??= await GetFolderItemsAsync(
                        message.InboxAccProjectId!, message.InboxAccFolderId!, cancellationToken).ConfigureAwait(false);

                    if (useAttachmentsFolder && !attachmentsFolderResolved)
                    {
                        attachmentsFolderResolved = true;
                        attachmentsFolderId = await GetFolderByNameAsync(
                            message.InboxAccProjectId!,
                            message.InboxAccFolderId!,
                            AccInboxLayout.AttachmentsFolderName,
                            cancellationToken).ConfigureAwait(false);

                        attachmentsFolderItems = string.IsNullOrWhiteSpace(attachmentsFolderId)
                            ? Array.Empty<InboxItem>()
                            : await GetFolderItemsAsync(
                                message.InboxAccProjectId!, attachmentsFolderId!, cancellationToken).ConfigureAwait(false);
                    }

                    var lookupItems = useAttachmentsFolder
                        ? attachmentsFolderItems ?? Array.Empty<InboxItem>()
                        : messageFolderItems;

                    var inboxItem = FindInboxItem(lookupItems, att);
                    if (inboxItem is null)
                    {
                        Trace.TraceWarning(
                            $"[MoveToProject] MissingInAcc attachment Id={att.Id}, File='{att.SavedFileName ?? att.OriginalFileName}'.");
                        // TEMP WF-DEBUG
                        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"att={att.Id} '{att.SavedFileName ?? att.OriginalFileName}' FAILED: not found in ACC inbox folder (lookupItems={lookupItems.Count} useAttachmentsFolder={useAttachmentsFolder})");
                        RecordFailure(attachmentFailures, ref failedCount, att, "MissingInAcc");
                        continue;
                    }
                }

                var metadataRead = await ReadInboxMetadataAsync(message, att, cancellationToken).ConfigureAwait(false);
                if (!metadataRead.Success)
                {
                    Trace.TraceWarning(
                        $"[MoveToProject] Metadata read failed for attachment Id={att.Id}: {metadataRead.ErrorMessage}. Continuing with empty attributes (DB tag cache fallback applies).");
                    metadataRead = AccItemMetadataReadResult.Ok(
                        new Dictionary<string, string?>(StringComparer.Ordinal));
                }

                if (IsTruthy(metadataRead.Attributes, SidecarMetadata.InboxAccAttributeNames.MoveMovedToProject))
                {
                    var (movedTagFileId, movedTagAltId) = ResolveFilingTag(metadataRead.Attributes, att);
                    if (movedTagFileId is null)
                    {
                        RecordFailure(attachmentFailures, ref failedCount, att, "AlreadyMovedConflict",
                            "הקובץ מסומן כהועבר ב-ACC אך אין תיוג יעד נוכחי להשוואה.");
                        continue;
                    }

                    if (MatchesCurrentMoveTarget(
                            metadataRead.Attributes, projectId, movedTagFileId.Value, movedTagAltId))
                    {
                        alreadySameSourceCount++;
                        filedInboxAttachmentIdsThisRun.Add(att.Id);
                        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move",
                            $"att={att.Id} '{att.OriginalFileName}' VERIFIED already at current target");
                        continue;
                    }

                    RecordFailure(attachmentFailures, ref failedCount, att, "AlreadyMovedConflict",
                        "הקובץ כבר תויק ליעד אחר לפי מטא-דאטת ACC.");
                    continue;
                }

                // Locked without MoveMovedToProject (or after conflict path above).
                if (IsTruthy(metadataRead.Attributes, SidecarMetadata.InboxAccAttributeNames.LockLockedForEditing))
                {
                    Trace.TraceWarning($"[MoveToProject] Attachment Id={att.Id} is locked by ACC metadata; skipping filing.");
                    SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"att={att.Id} '{att.OriginalFileName}' FAILED: locked by ACC metadata");
                    RecordFailure(attachmentFailures, ref failedCount, att, "Locked");
                    continue;
                }

                var (projectFileId, projectAlternativeId) = ResolveFilingTag(metadataRead.Attributes, att);
                if (projectFileId is null)
                {
                    // TEMP WF-DEBUG
                    SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"att={att.Id} '{att.OriginalFileName}' FAILED: no filing tag resolved (metadataReadOk={metadataRead.Success} dbTag={att.ProjectFileId?.ToString() ?? "null"})");
                    RecordFailure(attachmentFailures, ref failedCount, att, "NoFilingTag");
                    continue;
                }

                if (isZipFolder)
                {
                    var (zipMoved, zipFailed, zipSameSource, zipMetadataIncomplete) = await FileZipFolderAsync(
                        db, message, att, projectId, projectFileId.Value, projectAlternativeId, command, cancellationToken)
                        .ConfigureAwait(false);

                    if (zipMetadataIncomplete)
                    {
                        RecordFailure(attachmentFailures, ref failedCount, att, "FiledButMoveMetadataFailed",
                            "תיוק תיקיית ZIP הצליח פיזית אך מטא-דאטת Move/Lock נכשלה.");
                    }
                    else if (zipFailed && !zipMoved && !zipSameSource)
                    {
                        RecordFailure(attachmentFailures, ref failedCount, att, "ZipFilingFailed");
                    }
                    else
                    {
                        movedCount += zipMoved ? 1 : 0;
                        alreadySameSourceCount += zipSameSource ? 1 : 0;
                        filedInboxAttachmentIdsThisRun.Add(att.Id);
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(att.AccItemId))
                {
                    RecordFailure(attachmentFailures, ref failedCount, att, "MissingAccItemId");
                    continue;
                }

                var dl = await _accDownloadService.DownloadToTempAsync(
                    message.InboxAccProjectId!, att.AccItemId!, cancellationToken).ConfigureAwait(false);
                if (dl is null)
                {
                    Trace.TraceWarning($"[MoveToProject] Failed to download attachment Id={att.Id} from inbox.");
                    // TEMP WF-DEBUG
                    SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"att={att.Id} '{att.OriginalFileName}' FAILED: DownloadToTemp returned null (accItemId={att.AccItemId})");
                    RecordFailure(attachmentFailures, ref failedCount, att, "DownloadFailed");
                    continue;
                }
                tempLocalPath = dl.TempFilePath;

                long? sourceFileSize = TryGetFileSize(tempLocalPath);

                var request = new FileProjectFileRequest(
                    ProjectId: projectId,
                    ProjectFileId: projectFileId.Value,
                    ProjectAlternativeId: projectAlternativeId,
                    SourceLocalPath: tempLocalPath,
                    OriginalFileName: att.OriginalFileName ?? att.SavedFileName ?? "attachment",
                    SourceType: FileInstanceSourceType.EmailAttachment,
                    SourceEmailAttachmentId: att.Id,
                    EmailSubject: message.Subject,
                    EmailFrom: message.FromAddress,
                    EmailDate: message.ReceivedUtc.ToString("o"))
                {
                    SourceGmailMessageId = message.MessageUniqueId,
                    SourceMessageDateUtc = message.ReceivedUtc,
                    SourceOriginalFileName = att.OriginalFileName ?? att.SavedFileName,
                    SourceFileSizeBytes = sourceFileSize,
                    SourceContentSha256 = string.IsNullOrWhiteSpace(att.ContentSha256) ? null : att.ContentSha256,
                    SourceAttachmentId = att.Id,
                };

                var result = await _filingService.FileAsync(request, cancellationToken).ConfigureAwait(false);

                var movedAtUtc = DateTime.UtcNow;
                var moveMetadataResult = await WriteMoveLockMetadataAsync(
                    message, att, projectId, projectFileId.Value, projectAlternativeId, result, movedAtUtc, command, cancellationToken)
                    .ConfigureAwait(false);
                if (!moveMetadataResult.Success)
                {
                    // INACTIVE (FileMaterial six decisions 2026-08): previous behavior only raised
                    // warningCount and still counted the attachment as fully moved.
                    // Target: physical file may exist, but Move/Lock incomplete → process failure.
                    RecordFailure(attachmentFailures, ref failedCount, att, "FiledButMoveMetadataFailed",
                        moveMetadataResult.ErrorMessage);
                    Trace.TraceWarning(
                        $"[MoveToProject] Attachment Id={att.Id} filed but Move/Lock metadata write failed: {moveMetadataResult.ErrorMessage}");
                    continue;
                }

                filedInboxAttachmentIdsThisRun.Add(att.Id);
                if (result.AlreadySameSource)
                    alreadySameSourceCount++;
                else
                    movedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[MoveToProject] Failed to file attachment Id={att.Id}. {ex}");
                // TEMP WF-DEBUG
                SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"att={att.Id} '{att.OriginalFileName}' FAILED: {ex.GetType().Name}: {ex.Message}");
                RecordFailure(attachmentFailures, ref failedCount, att, "FilingFailed", $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                DeleteTemp(tempLocalPath);
            }
        }

        if (failedCount == 0 && (movedCount + alreadySameSourceCount) >= taggedNeedingFiling.Count
            && taggedNeedingFiling.Count > 0)
        {
            var refreshed = await db.EmailInboxMessages
                .FirstOrDefaultAsync(m => m.Id == emailMessageId, cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is not null)
            {
                refreshed.Status = EmailInboxStatus.Moved;
                refreshed.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var allTransferred = failedCount == 0
                             && taggedNeedingFiling.Count > 0
                             && (movedCount + alreadySameSourceCount) >= taggedNeedingFiling.Count;

        // INACTIVE (FileMaterial six decisions 2026-08): executor ReportTaskCompletionAsync when TaskId set.
        // UI EmailDetailViewModel owns CompleteAsync + dismiss gating. Kept method for reference / non-UI tooling.
        if (allTransferred && command.TaskId is int taskId && taskId > 0)
        {
            Trace.TraceInformation(
                $"[MoveToProject] Skipping executor task completion for TaskId={taskId}; UI owns CompleteAsync.");
            // Previously: await ReportTaskCompletionAsync(...)
        }

        // TEMP WF-DEBUG
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move",
            $"executor done inbox={message.Id} moved={movedCount} failed={failedCount} sameSource={alreadySameSourceCount} of {taggedNeedingFiling.Count} allTransferred={allTransferred}");

        var messageText = EmailMoveToProjectOutcomeDisplay.Build(
            movedCount, taggedNeedingFiling.Count, attachmentFailures, alreadySameSourceCount);
        // TEMP WF-DEBUG
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move",
            $"userMessage={messageText.Replace("\r\n", " | ").Replace('\n', '|').Replace('\r', '|')}");
        return allTransferred
            ? new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.Succeeded,
                messageText,
                movedCount,
                failedCount,
                attachmentFailures,
                taggedNeedingFiling.Count,
                alreadySameSourceCount)
            : new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.Failed,
                messageText,
                movedCount,
                failedCount,
                attachmentFailures,
                taggedNeedingFiling.Count,
                alreadySameSourceCount);
    }

    private async Task TryReconcileAndRecoverAsync(
        SiNetSQLDbContext db,
        EmailInboxMessage message,
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_accStatusService is not null)
            {
                await _accStatusService
                    .GetStatusByInboxMessageIdAsync(message.Id, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_recoveryExecutor is null || string.IsNullOrWhiteSpace(message.MessageUniqueId))
            {
                return;
            }

            // Gmail-only recover: never pass external Jumbo/WeTransfer rows to Gmail re-ingest.
            var missingGmailIds = await db.EmailInboxAttachments
                .AsNoTracking()
                .Where(a => a.MessageId == message.Id
                            && a.ProjectFileId != null
                            && string.IsNullOrEmpty(a.AccItemId)
                            && !a.IsExternalDownload)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // ZIP folders use AccVersionId without AccItemId — exclude those from "missing".
            var zipFolderIds = await db.EmailInboxAttachments
                .AsNoTracking()
                .Where(a => a.MessageId == message.Id
                            && a.ProjectFileId != null
                            && string.IsNullOrEmpty(a.AccItemId)
                            && !string.IsNullOrEmpty(a.AccVersionId)
                            && a.OriginalFileName != null
                            && a.OriginalFileName.EndsWith(".zip"))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var recoverIds = missingGmailIds.Except(zipFolderIds).ToList();
            if (recoverIds.Count == 0)
            {
                return;
            }

            await _recoveryExecutor
                .RecoverMissingAttachmentsAsync(
                    message.Id,
                    message.MessageUniqueId,
                    recoverIds,
                    command.UserId?.ToString(CultureInfo.InvariantCulture) ?? Environment.UserName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[MoveToProject] Reconcile/recovery before move failed (non-fatal): {ex.Message}");
        }
    }

    private static bool IsRequiredBusinessAttachment(EmailInboxAttachment a)
    {
        if (!a.ProjectFileId.HasValue)
            return false;

        var fileName = a.SavedFileName ?? a.OriginalFileName;
        if (string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(fileName, AccInboxLayout.EmailBodyFileName, StringComparison.OrdinalIgnoreCase)
            || a.AttachmentIndex == AccInboxLayout.EmailBodyAttachmentIndex)
        {
            // Body PDF is required only when explicitly tagged (ProjectFileId already required above).
            return true;
        }

        // Other system/inline rows (negative index) are not business material.
        if (a.AttachmentIndex < 0)
            return false;

        return true;
    }

    private async Task<(bool Moved, bool Failed, bool SameSource, bool MetadataIncomplete)> FileZipFolderAsync(
        SiNetSQLDbContext db,
        EmailInboxMessage message,
        EmailInboxAttachment att,
        int projectId,
        int projectFileId,
        int? projectAlternativeId,
        EmailMoveToProjectCommand command,
        CancellationToken ct)
    {
        var projectFile = await db.ProjectFiles.AsNoTracking()
            .FirstOrDefaultAsync(pf => pf.Id == projectFileId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ProjectFile #{projectFileId} not found.");
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Project #{projectId} not found.");

        string? altName = null;
        if (projectAlternativeId is > 0)
        {
            altName = await db.ProjectAlternatives.AsNoTracking()
                .Where(a => a.Id == projectAlternativeId.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        altName = string.IsNullOrWhiteSpace(altName) ? "1" : altName!;

        var conventionFolderName = ProjectFileNameBuilder.BuildFolderName(
            (int)(project.Number ?? 0),
            projectFile.TypeProjId ?? 0,
            (int)(projectFile.Number ?? 0),
            altName,
            projectFile.Title ?? string.Empty,
            Path.GetFileNameWithoutExtension(att.OriginalFileName ?? att.SavedFileName ?? "Folder"));

        var folderItems = await GetFolderItemsAsync(message.InboxAccProjectId!, att.AccVersionId!, ct).ConfigureAwait(false);
        if (folderItems.Count == 0)
        {
            Trace.TraceWarning($"[MoveToProject] ZIP folder URN={att.AccVersionId} is empty or not found in ACC.");
            return (false, true, false, false);
        }

        int folderMovedCount = 0;
        int folderFailedCount = 0;
        int folderSameSourceCount = 0;
        FileProjectFileResult? lastFilingResult = null;

        foreach (var item in folderItems)
        {
            ct.ThrowIfCancellationRequested();
            string? itemTempPath = null;
            try
            {
                var itemDl = await _accDownloadService.DownloadToTempAsync(message.InboxAccProjectId!, item.ItemId, ct).ConfigureAwait(false);
                if (itemDl is null)
                {
                    Trace.TraceWarning($"[MoveToProject] Failed to download item '{item.DisplayName}' (Id={item.ItemId}) from ZIP folder.");
                    folderFailedCount++;
                    continue;
                }
                itemTempPath = itemDl.TempFilePath;

                var itemFilingRequest = new FileProjectFileRequest(
                    ProjectId: projectId,
                    ProjectFileId: projectFileId,
                    ProjectAlternativeId: projectAlternativeId,
                    SourceLocalPath: itemTempPath,
                    OriginalFileName: item.DisplayName,
                    SourceType: FileInstanceSourceType.EmailAttachment,
                    SourceEmailAttachmentId: att.Id,
                    EmailSubject: message.Subject,
                    EmailFrom: message.FromAddress,
                    EmailDate: message.ReceivedUtc.ToString("o"))
                {
                    SourceGmailMessageId = message.MessageUniqueId,
                    SourceMessageDateUtc = message.ReceivedUtc,
                    SourceOriginalFileName = item.DisplayName,
                    SourceFileSizeBytes = TryGetFileSize(itemTempPath),
                    SourceContentSha256 = null,
                    SourceAttachmentId = att.Id,
                    FolderNameOverride = conventionFolderName
                };

                var itemFilingResult = await _filingService.FileAsync(itemFilingRequest, ct).ConfigureAwait(false);
                lastFilingResult = itemFilingResult;
                if (itemFilingResult.AlreadySameSource)
                    folderSameSourceCount++;
                else
                    folderMovedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                folderFailedCount++;
                Trace.TraceError($"[MoveToProject] Failed to file item '{item.DisplayName}' from ZIP folder. {ex}");
            }
            finally
            {
                DeleteTemp(itemTempPath);
            }
        }

        if (folderFailedCount > 0 && folderMovedCount == 0)
        {
            return (false, true, false, false);
        }

        var itemMovedAtUtc = DateTime.UtcNow;
        var folderMoveMetadataResult = lastFilingResult is null
            ? AccItemMetadataResult.Fail(null, "ZIP folder filing completed without a final filing result for metadata write.")
            : await WriteMoveLockMetadataAsync(
                message, att, projectId, projectFileId, projectAlternativeId, lastFilingResult, itemMovedAtUtc, command, ct)
                .ConfigureAwait(false);

        if (!folderMoveMetadataResult.Success)
        {
            // INACTIVE: previously warningCount++ and still treated ZIP as fully moved.
            Trace.TraceWarning(
                $"[MoveToProject] ZIP attachment Id={att.Id} filed but Move/Lock metadata write failed: {folderMoveMetadataResult.ErrorMessage}");
            return (folderMovedCount > 0, false, folderSameSourceCount > 0 && folderMovedCount == 0, true);
        }

        return (folderMovedCount > 0, false, folderSameSourceCount > 0 && folderMovedCount == 0, false);
    }

    // INACTIVE for TaskId path (FileMaterial six decisions 2026-08): UI owns CompleteAsync.
    // Retained for potential non-UI tooling; do not call from MoveAsync when TaskId is set.
    private async Task ReportTaskCompletionAsync(
        SiNetSQLDbContext db,
        int taskId,
        EmailInboxMessage? inboxMessage,
        IReadOnlyList<int> filedInboxAttachmentIds,
        int userId,
        CancellationToken ct)
    {
        if (userId == 0)
        {
            Trace.TraceWarning("[MoveToProject] UserId is 0; skipping task completion.");
            return;
        }

        // Reconstruct the task's EmailInboxMessage work-target ids from TaskLinks
        // (the coordinator command only carries TaskId; legacy carried a full context).
        var taskWorkTargetIds = (await db.TaskLinks.AsNoTracking()
            .Where(l => l.TaskId == taskId
                        && l.IsWorkTarget
                        && l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage)
            .Select(l => (int)l.LinkedEntityId)
            .ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();

        var filedEmailIds = await db.EmailInboxAttachments.AsNoTracking()
            .Where(a => filedInboxAttachmentIds.Contains(a.Id))
            .Select(a => a.MessageId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        if (inboxMessage != null && !filedEmailIds.Contains(inboxMessage.Id))
            filedEmailIds.Add(inboxMessage.Id);

        var completedEmailIds = taskWorkTargetIds.Count == 0
            ? filedEmailIds
            : filedEmailIds.Where(id => taskWorkTargetIds.Contains(id)).ToList();

        if (completedEmailIds.Count == 0)
        {
            Trace.TraceInformation("[MoveToProject] No filed email overlaps the task's work-target ids; nothing to complete.");
            return;
        }

        var filedThisRunSet = filedInboxAttachmentIds as HashSet<int> ?? new HashSet<int>(filedInboxAttachmentIds);
        var emailsFullyFiled = new List<int>();
        foreach (var emailId in completedEmailIds)
        {
            var messageForCompletion = inboxMessage?.Id == emailId
                ? inboxMessage
                : await db.EmailInboxMessages.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == emailId, ct).ConfigureAwait(false);
            if (messageForCompletion is null)
                continue;

            var relevantAttachments = await db.EmailInboxAttachments.AsNoTracking()
                .Where(a => a.MessageId == emailId && a.ProjectFileId != null && a.AccItemId != null)
                .ToListAsync(ct).ConfigureAwait(false);
            if (relevantAttachments.Count == 0)
                continue;

            var unfiledCount = 0;
            foreach (var attachment in relevantAttachments)
            {
                if (filedThisRunSet.Contains(attachment.Id)) continue;
                if (!await IsAttachmentFiledAsync(messageForCompletion, attachment, ct).ConfigureAwait(false))
                    unfiledCount++;
            }
            if (unfiledCount == 0)
                emailsFullyFiled.Add(emailId);
        }

        if (emailsFullyFiled.Count == 0)
        {
            Trace.TraceInformation("[MoveToProject] No email reached the all-attachments-filed threshold; skipping completion.");
            return;
        }

        var completedEmailIdsLong = emailsFullyFiled.Select(i => (long)i).ToList();
        var resolvedTaskLinkIds = await db.TaskLinks.AsNoTracking()
            .Where(l => l.TaskId == taskId
                        && l.IsWorkTarget
                        && l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage
                        && completedEmailIdsLong.Contains(l.LinkedEntityId))
            .Select(l => l.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        if (resolvedTaskLinkIds.Count == 0)
        {
            Trace.TraceInformation(
                $"[MoveToProject] Task {taskId} has no matching EmailInboxMessage work-target TaskLinks for filed emails; skipping completion.");
            return;
        }

        var completionResult = await _taskCompletionService.CompleteAsync(
            new CompleteTaskCommand(
                TaskId: taskId,
                CompletionEventCode: ReviewCompletionEvents.ReviewMaterialFiled,
                TaskResultCode: null,
                CompletedTaskLinkIds: resolvedTaskLinkIds,
                UserId: userId),
            ct).ConfigureAwait(false);

        if (!completionResult.Success)
        {
            Trace.TraceWarning(
                $"[MoveToProject] Task completion returned failure for task {taskId}: {completionResult.ErrorMessage}");
            return;
        }

        Trace.TraceInformation(
            $"[MoveToProject] Task {taskId} completion reported: links=[{string.Join(',', resolvedTaskLinkIds)}], " +
            $"closed={completionResult.TaskClosed}, workflowAdvanced={completionResult.WorkflowAdvanced}.");
    }

    private static InboxItem? FindInboxItem(IReadOnlyList<InboxItem> inboxItems, EmailInboxAttachment attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.AccItemId))
        {
            var byItemId = inboxItems.FirstOrDefault(i =>
                string.Equals(i.ItemId, attachment.AccItemId, StringComparison.OrdinalIgnoreCase));
            if (byItemId is not null)
                return byItemId;
        }

        var names = new[] { attachment.SavedFileName, attachment.OriginalFileName }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0
            ? null
            : inboxItems.FirstOrDefault(i => names.Contains(i.DisplayName, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<AccItemMetadataReadResult> ReadInboxMetadataAsync(
        EmailInboxMessage message, EmailInboxAttachment attachment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.InboxAccProjectId) || string.IsNullOrWhiteSpace(attachment.AccItemId))
        {
            return AccItemMetadataReadResult.Ok(new Dictionary<string, string?>(StringComparer.Ordinal));
        }

        return await _metadataService
            .ReadAttributesAsync(
                message.InboxAccProjectId,
                attachment.AccItemId,
                attachment.SavedFileName ?? attachment.OriginalFileName,
                ct)
            .ConfigureAwait(false);
    }

    private static bool MatchesCurrentMoveTarget(
        IReadOnlyDictionary<string, string?> attributes,
        int projectId,
        int projectFileId,
        int? projectAlternativeId)
    {
        var targetProjectId = TryGetInt(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectId);
        var targetProjectFileId = TryGetInt(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectFileId);
        if (targetProjectId != projectId || targetProjectFileId != projectFileId)
            return false;

        var targetAlt = TryGetInt(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectAlternativeId);
        var expectedAlt = projectAlternativeId is > 0 ? projectAlternativeId : null;
        var actualAlt = targetAlt is > 0 ? targetAlt : null;
        return expectedAlt == actualAlt;
    }

    private static bool IsTruthy(IReadOnlyDictionary<string, string?> attributes, string key)
    {
        return attributes.TryGetValue(key, out var value) &&
               (value == "1" ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    private static (int? ProjectFileId, int? ProjectAlternativeId) ResolveFilingTag(
        IReadOnlyDictionary<string, string?> attributes, EmailInboxAttachment attachment)
    {
        var projectFileId = TryGetInt(attributes, SidecarMetadata.InboxAccAttributeNames.TagProjectFileId);
        var projectAlternativeId = TryGetInt(attributes, SidecarMetadata.InboxAccAttributeNames.TagProjectAlternativeId);

        if (projectFileId.HasValue)
            return (projectFileId, projectAlternativeId);

        // Tag-cache fallback (not file-existence proof); physical ACC existence is verified earlier.
        return (attachment.ProjectFileId, attachment.ProjectAlternativeId);
    }

    private async Task<bool> IsAttachmentFiledAsync(
        EmailInboxMessage message, EmailInboxAttachment attachment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.InboxAccProjectId) || string.IsNullOrWhiteSpace(attachment.AccItemId))
            return false;

        AccItemMetadataReadResult metadataRead;
        try
        {
            metadataRead = await ReadInboxMetadataAsync(message, attachment, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }

        if (!metadataRead.Success)
            return false;

        // ACC Move/Lock metadata is the sole source of truth (no location-resolver in the native path).
        return HasFiledMoveTarget(metadataRead.Attributes);
    }

    private static bool HasFiledMoveTarget(IReadOnlyDictionary<string, string?> attributes)
    {
        if (!IsTruthy(attributes, SidecarMetadata.InboxAccAttributeNames.MoveMovedToProject))
            return false;

        if (!TryGetAttribute(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetDestination, out var destination) ||
            !TryGetAttribute(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetFileName, out _))
            return false;

        if (string.Equals(destination, FileStorageDestination.Acc.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return TryGetAttribute(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetAccItemId, out _) &&
                   TryGetAttribute(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetAccFolderId, out _);
        }

        if (string.Equals(destination, FileStorageDestination.FileServer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return TryGetAttribute(attributes, SidecarMetadata.InboxAccAttributeNames.MoveTargetFilePath, out _);
        }

        return false;
    }

    private static bool TryGetAttribute(IReadOnlyDictionary<string, string?> attributes, string key, out string value)
    {
        if (attributes.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, string?> attributes, string key)
    {
        return attributes.TryGetValue(key, out var value)
               && int.TryParse(value, out var parsed)
               && parsed > 0
            ? parsed
            : null;
    }

    private static bool IsZipContainerAttachment(EmailInboxAttachment attachment)
    {
        var fileName = attachment.SavedFileName ?? attachment.OriginalFileName;
        return !attachment.IsExternalDownload
               && !string.IsNullOrWhiteSpace(attachment.AccVersionId)
               && fileName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task<AccItemMetadataResult> WriteMoveLockMetadataAsync(
        EmailInboxMessage message,
        EmailInboxAttachment attachment,
        int targetProjectId,
        int targetProjectFileId,
        int? targetProjectAlternativeId,
        FileProjectFileResult filingResult,
        DateTime movedAtUtc,
        EmailMoveToProjectCommand command,
        CancellationToken ct)
    {
        if (IsZipContainerAttachment(attachment))
        {
            if (string.IsNullOrWhiteSpace(message.InboxAccProjectId) || string.IsNullOrWhiteSpace(attachment.AccVersionId))
                return AccItemMetadataResult.Fail(null, "Inbox ACC project/folder identifiers are required for Move/Lock metadata write on folder.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(message.InboxAccProjectId) ||
                string.IsNullOrWhiteSpace(message.InboxAccFolderId) ||
                string.IsNullOrWhiteSpace(attachment.AccItemId))
                return AccItemMetadataResult.Fail(null, "Inbox ACC project/folder/item identifiers are required for Move/Lock metadata write.");
        }

        if (string.IsNullOrWhiteSpace(attachment.AccVersionId))
            return AccItemMetadataResult.Fail(null, "AccVersionId is required for Move/Lock metadata write.");

        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SidecarMetadata.InboxAccAttributeNames.MoveMovedToProject] = "true",
            [SidecarMetadata.InboxAccAttributeNames.MoveMovedAtUtc] = movedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            [SidecarMetadata.InboxAccAttributeNames.MoveMovedBy] = command.UserId?.ToString(CultureInfo.InvariantCulture) ?? Environment.UserName,
            [SidecarMetadata.InboxAccAttributeNames.MoveTargetDestination] = filingResult.TargetDestination.ToString(),
            [SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectId] = (filingResult.TargetProjectId > 0 ? filingResult.TargetProjectId : targetProjectId).ToString(CultureInfo.InvariantCulture),
            [SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectFileId] = (filingResult.TargetProjectFileId > 0 ? filingResult.TargetProjectFileId : targetProjectFileId).ToString(CultureInfo.InvariantCulture),
            [SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectAlternativeId] = (filingResult.TargetProjectAlternativeId ?? targetProjectAlternativeId)?.ToString(CultureInfo.InvariantCulture),
            [SidecarMetadata.InboxAccAttributeNames.MoveTargetFileName] = filingResult.TargetFileName,
            [SidecarMetadata.InboxAccAttributeNames.MoveTargetFilePath] = filingResult.TargetDestination == FileStorageDestination.FileServer
                ? filingResult.TargetFilePath
                : null,
            [SidecarMetadata.InboxAccAttributeNames.LockLockedForEditing] = "true",
        };

        var targetAccItemId = filingResult.TargetAccItemId;
        var targetAccFolderId = filingResult.TargetAccFolderId;
        if (filingResult.TargetDestination == FileStorageDestination.Acc &&
            (string.IsNullOrWhiteSpace(targetAccItemId) || string.IsNullOrWhiteSpace(targetAccFolderId)))
        {
            Trace.TraceWarning(
                $"[MoveToProject] Filing result missing ACC target metadata " +
                $"(ItemId={targetAccItemId ?? "(null)"}, FolderId={targetAccFolderId ?? "(null)"}).");
        }

        if (!string.IsNullOrWhiteSpace(targetAccItemId))
            attributes[SidecarMetadata.InboxAccAttributeNames.MoveTargetAccItemId] = targetAccItemId;
        if (!string.IsNullOrWhiteSpace(targetAccFolderId))
            attributes[SidecarMetadata.InboxAccAttributeNames.MoveTargetAccFolderId] = targetAccFolderId;

        if (IsZipContainerAttachment(attachment))
            return await WriteAccFolderMoveLockMetadataJsonAsync(message, attachment, attributes, ct).ConfigureAwait(false);

        return await _metadataService
            .WriteAttributesAsync(
                message.InboxAccProjectId!,
                message.InboxAccFolderId!,
                attachment.AccVersionId!,
                attachment.AccItemId!,
                attributes,
                attachment.SavedFileName ?? attachment.OriginalFileName,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<AccItemMetadataResult> WriteAccFolderMoveLockMetadataJsonAsync(
        EmailInboxMessage message,
        EmailInboxAttachment attachment,
        Dictionary<string, string?> moveAttributes,
        CancellationToken ct)
    {
        var accProjectId = message.InboxAccProjectId!;
        var zipSubfolderId = attachment.AccVersionId!;
        var zipFileName = attachment.SavedFileName ?? attachment.OriginalFileName!;
        var zipFolderName = Path.GetFileNameWithoutExtension(zipFileName);
        var folderMetaFileName = zipFolderName + ".json";

        try
        {
            var folderItems = await GetFolderItemsAsync(accProjectId, zipSubfolderId, ct).ConfigureAwait(false);
            var existingMetaDup = folderItems.FirstOrDefault(
                i => string.Equals(i.DisplayName, folderMetaFileName, StringComparison.OrdinalIgnoreCase));

            FilePlacementMetadata metadata = new();
            if (existingMetaDup != null && !string.IsNullOrWhiteSpace(existingMetaDup.ItemId))
            {
                var downloadResult = await _accDownloadService.DownloadToTempAsync(accProjectId, existingMetaDup.ItemId, ct).ConfigureAwait(false);
                if (downloadResult != null)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(downloadResult.TempFilePath, System.Text.Encoding.UTF8, ct).ConfigureAwait(false);
                        metadata = System.Text.Json.JsonSerializer.Deserialize<FilePlacementMetadata>(json) ?? new FilePlacementMetadata();
                    }
                    catch
                    {
                        metadata = new FilePlacementMetadata();
                    }
                    finally
                    {
                        DeleteTemp(downloadResult.TempFilePath);
                    }
                }
            }

            if (string.IsNullOrEmpty(metadata.OriginalFileName))
                metadata.OriginalFileName = attachment.OriginalFileName ?? attachment.SavedFileName ?? "";
            if (string.IsNullOrEmpty(metadata.ConventionFileName))
                metadata.ConventionFileName = zipFolderName;
            metadata.EmailSubject ??= message.Subject;
            metadata.EmailFrom ??= message.FromAddress;
            metadata.EmailDate ??= message.ReceivedUtc.ToString("o");
            if (string.IsNullOrEmpty(metadata.PlacedAtUtc))
                metadata.PlacedAtUtc = DateTime.UtcNow.ToString("o");

            foreach (var kv in moveAttributes)
                metadata.Attributes[kv.Key] = kv.Value;

            var updatedJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var tempMetaPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{folderMetaFileName}");
            await File.WriteAllTextAsync(tempMetaPath, updatedJson, System.Text.Encoding.UTF8, ct).ConfigureAwait(false);

            try
            {
                await _accUploadService.UploadAsync(
                    new AccFileUploadRequest(accProjectId, tempMetaPath, folderMetaFileName)
                    {
                        TargetFolderId = zipSubfolderId,
                        ExistingItemId = existingMetaDup?.ItemId,
                    },
                    ct).ConfigureAwait(false);
            }
            finally
            {
                DeleteTemp(tempMetaPath);
            }

            return AccItemMetadataResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[MoveToProject][ZIP] Failed to write move metadata JSON inside ZIP folder '{zipFolderName}': {ex.Message}");
            return AccItemMetadataResult.Fail(null, $"Failed to write ACC folder move metadata: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<InboxItem>> GetFolderItemsAsync(
        string accProjectId, string folderId, CancellationToken cancellationToken)
    {
        var browseResult = await _folderBrowserService
            .BrowseAsync(accProjectId, folderId, cancellationToken)
            .ConfigureAwait(false);

        return browseResult?.Entries
            .Where(static entry => entry.Kind == AccFolderEntryKind.Item)
            .Select(static entry => new InboxItem(entry.Id, entry.DisplayName))
            .ToArray()
            ?? Array.Empty<InboxItem>();
    }

    private async Task<string?> GetFolderByNameAsync(
        string accProjectId, string parentFolderId, string folderName, CancellationToken cancellationToken)
    {
        var browseResult = await _folderBrowserService
            .BrowseAsync(accProjectId, parentFolderId, cancellationToken)
            .ConfigureAwait(false);

        return browseResult?.Entries
            .FirstOrDefault(entry =>
                entry.Kind == AccFolderEntryKind.Folder &&
                string.Equals(entry.DisplayName, folderName, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static long? TryGetFileSize(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return new FileInfo(path).Length;
        }
        catch { /* best-effort */ }
        return null;
    }

    private static void RecordFailure(
        List<EmailMoveToProjectAttachmentFailure> failures,
        ref int failedCount,
        EmailInboxAttachment att,
        string kind,
        string? detail = null)
    {
        failedCount++;
        failures.Add(new EmailMoveToProjectAttachmentFailure(
            att.Id,
            att.OriginalFileName ?? att.SavedFileName ?? $"AttId={att.Id}",
            kind,
            detail));
    }

    private static void DeleteTemp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore cleanup errors */ }
    }

    private static EmailMoveToProjectCoordinatorResult Failed(string message)
    {
        // TEMP WF-DEBUG
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"executor FAILED: {message}");
        return new(EmailMoveToProjectOutcome.Failed, message);
    }

    private static EmailMoveToProjectCoordinatorResult Deferred(string message)
    {
        // TEMP WF-DEBUG
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step("Email.Move", $"executor DEFERRED: {message}");
        return new(EmailMoveToProjectOutcome.DeferredRequiresUi, message);
    }
}
