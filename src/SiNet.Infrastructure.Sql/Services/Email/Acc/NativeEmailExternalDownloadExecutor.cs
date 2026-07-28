using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Native Jumbo/WeTransfer → ACC Inbox upload (no SiNetSQL EmailIngestionService).
/// See <c>docs/NATIVE_EMAIL_ACC_INGEST.md</c> N2.
/// </summary>
public sealed class NativeEmailExternalDownloadExecutor(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IAccInboxBootstrapService inboxBootstrap,
    IAccFolderPathService folderPathService,
    IAccFileUploadService fileUploadService,
    IAppLogger logger) : IEmailExternalDownloadExecutor
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IAccInboxBootstrapService _inboxBootstrap =
        inboxBootstrap ?? throw new ArgumentNullException(nameof(inboxBootstrap));
    private readonly IAccFolderPathService _folderPathService =
        folderPathService ?? throw new ArgumentNullException(nameof(folderPathService));
    private readonly IAccFileUploadService _fileUploadService =
        fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
    private readonly IAppLogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        IProgress<EmailExternalDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.GmailMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.LocalFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FileName);

        if (!File.Exists(command.LocalFilePath))
        {
            return Failed(command.FileName, "קובץ ההורדה לא נמצא בדיסק.");
        }

        if (command.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return await UploadZipContentsAsync(command, progress, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new EmailExternalDownloadProgress(
            EmailExternalDownloadStage.Uploading,
            $"מעלה ל-ACC: {command.FileName}",
            Percent: null,
            CurrentFile: 1,
            TotalFiles: 1,
            FileName: command.FileName));

        var result = await UploadSingleFileAsync(
                command,
                command.LocalFilePath,
                command.FileName,
                zipSubfolder: null,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(result.Succeeded
            ? new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Completed,
                $"הועלה בהצלחה: {command.FileName}",
                Percent: 100,
                FileName: command.FileName)
            : new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Failed,
                result.ErrorMessage ?? $"העלאה נכשלה: {command.FileName}",
                FileName: command.FileName));

        return result;
    }

    private async Task<EmailExternalDownloadResult> UploadZipContentsAsync(
        EmailExternalDownloadCommand command,
        IProgress<EmailExternalDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var extractFolder = Path.Combine(
            Path.GetTempPath(),
            "SiNetExternalZip",
            Guid.NewGuid().ToString("N"));

        try
        {
            progress?.Report(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Extracting,
                $"מחלץ ZIP: {command.FileName}",
                FileName: command.FileName));

            Directory.CreateDirectory(extractFolder);
            ZipFile.ExtractToDirectory(command.LocalFilePath, extractFolder, overwriteFiles: true);

            var files = Directory
                .GetFiles(extractFolder, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                progress?.Report(new EmailExternalDownloadProgress(
                    EmailExternalDownloadStage.Failed,
                    "קובץ ZIP ריק — לא הועלה דבר ל-ACC",
                    FileName: command.FileName));
                return Failed(command.FileName, "קובץ ZIP ריק — לא הועלה דבר ל-ACC");
            }

            var zipSubfolder = Path.GetFileNameWithoutExtension(command.FileName);
            string? accFolderId = null;
            var uploaded = 0;
            string? lastError = null;

            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = files[i];
                var entryName = Path.GetFileName(filePath);
                var percent = (int)Math.Round((i / (double)files.Count) * 100);
                progress?.Report(new EmailExternalDownloadProgress(
                    EmailExternalDownloadStage.Uploading,
                    $"מעלה ל-ACC: {entryName} ({i + 1}/{files.Count})",
                    Percent: percent,
                    CurrentFile: i + 1,
                    TotalFiles: files.Count,
                    FileName: entryName));

                var one = await UploadSingleFileAsync(
                        command,
                        filePath,
                        entryName,
                        zipSubfolder,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (one.Succeeded)
                {
                    uploaded++;
                    accFolderId ??= one.AccFolderId;
                }
                else
                {
                    lastError = one.ErrorMessage ?? entryName;
                }
            }

            if (uploaded > 0)
            {
                var message = uploaded == files.Count
                    ? $"הועלו {uploaded}/{files.Count} קבצים ל-ACC"
                    : $"הועלו {uploaded}/{files.Count} קבצים ל-ACC (חלק נכשלו)";
                progress?.Report(new EmailExternalDownloadProgress(
                    uploaded == files.Count
                        ? EmailExternalDownloadStage.Completed
                        : EmailExternalDownloadStage.Failed,
                    message,
                    Percent: 100,
                    CurrentFile: uploaded,
                    TotalFiles: files.Count,
                    FileName: command.FileName));
                return new EmailExternalDownloadResult(
                    EmailExternalDownloadOutcome.Succeeded,
                    null,
                    accFolderId,
                    command.FileName,
                    uploaded == files.Count ? null : lastError);
            }

            progress?.Report(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Failed,
                lastError ?? "העלאת תוכן ה-ZIP ל-ACC נכשלה",
                FileName: command.FileName));
            return Failed(command.FileName, lastError ?? "העלאת תוכן ה-ZIP ל-ACC נכשלה");
        }
        catch (InvalidDataException)
        {
            progress?.Report(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Uploading,
                $"מעלה ל-ACC (לא ZIP תקין): {command.FileName}",
                FileName: command.FileName));

            return await UploadSingleFileAsync(
                    command,
                    command.LocalFilePath,
                    command.FileName,
                    zipSubfolder: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractFolder))
                {
                    Directory.Delete(extractFolder, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private async Task<EmailExternalDownloadResult> UploadSingleFileAsync(
        EmailExternalDownloadCommand command,
        string localFilePath,
        string fileName,
        string? zipSubfolder,
        CancellationToken cancellationToken)
    {
        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(
            command.InternetMessageId,
            command.GmailMessageId);
        var actingLogin = string.IsNullOrWhiteSpace(command.ActingUserLogin)
            ? Environment.UserName
            : command.ActingUserLogin.Trim();
        var safeFileName = EmailMessageIdentity.SanitizeFileName(fileName);

        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(localFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failed(safeFileName, $"קריאת הקובץ נכשלה: {ex.Message}");
        }

        if (data.Length == 0)
        {
            return Failed(safeFileName, "קובץ ריק — לא הועלה ל-ACC.");
        }

        var sha256 = EmailMessageIdentity.ComputeSha256Hex(data);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var message = await EnsureMessageRowAsync(db, command, messageUniqueId, actingLogin, cancellationToken)
            .ConfigureAwait(false);
        if (message is null)
        {
            return Failed(safeFileName, "לא ניתן ליצור/למצוא רשומת מייל ב-DB.");
        }

        var bootstrap = await _inboxBootstrap.EnsureAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(bootstrap.AccProjectId)
            || string.IsNullOrWhiteSpace(bootstrap.AccInboxFolderId))
        {
            return Failed(safeFileName, "ACC Inbox bootstrap נכשל. האם AccService רץ?");
        }

        var inboxProjectId = bootstrap.AccProjectId;
        var messageKey = EmailMessageIdentity.GetMessageKey(messageUniqueId);
        var pathSegments = AccInboxLayout.BuildMessageFolderPath(message.ThreadKey, messageKey);
        var msgFolderId = await _folderPathService
            .EnsurePathAsync(inboxProjectId, bootstrap.AccInboxFolderId, pathSegments, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string> attachmentSegments = string.IsNullOrWhiteSpace(zipSubfolder)
            ? [AccInboxLayout.AttachmentsFolderName]
            : [AccInboxLayout.AttachmentsFolderName, EmailMessageIdentity.SanitizeFileName(zipSubfolder)];

        var attachmentsFolderId = await _folderPathService
            .EnsurePathAsync(inboxProjectId, msgFolderId, attachmentSegments, cancellationToken)
            .ConfigureAwait(false);

        message.InboxAccProjectId = inboxProjectId;
        message.InboxAccFolderId = msgFolderId;
        message.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var existingAttachment = await db.EmailInboxAttachments
            .FirstOrDefaultAsync(
                a => a.MessageId == message.Id && a.ContentSha256 == sha256,
                cancellationToken)
            .ConfigureAwait(false);

        if (existingAttachment is not null && !string.IsNullOrEmpty(existingAttachment.AccItemId))
        {
            existingAttachment.IsExternalDownload = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new EmailExternalDownloadResult(
                EmailExternalDownloadOutcome.Succeeded,
                existingAttachment.AccItemId,
                msgFolderId,
                safeFileName,
                null);
        }

        EmailInboxAttachment dbAttachment;
        if (existingAttachment is not null)
        {
            dbAttachment = existingAttachment;
            dbAttachment.IsExternalDownload = true;
            dbAttachment.OriginalFileName = Truncate(fileName, 260);
            dbAttachment.SavedFileName = Truncate(safeFileName, 260);
        }
        else
        {
            var nextIndex = await db.EmailInboxAttachments
                .Where(a => a.MessageId == message.Id)
                .Select(a => (int?)a.AttachmentIndex)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? -1;

            dbAttachment = new EmailInboxAttachment
            {
                MessageId = message.Id,
                AttachmentIndex = nextIndex + 1,
                OriginalFileName = Truncate(fileName, 260),
                SavedFileName = Truncate(safeFileName, 260),
                ContentSha256 = sha256,
                IsExternalDownload = true,
            };
            db.EmailInboxAttachments.Add(dbAttachment);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var upload = await _fileUploadService
                .UploadAsync(
                    new AccFileUploadRequest(inboxProjectId, localFilePath, safeFileName)
                    {
                        TargetFolderId = attachmentsFolderId,
                        ExistingItemId = dbAttachment.AccItemId,
                        SourceIdentity = new AccFileSourceIdentity(
                            command.GmailMessageId,
                            message.ReceivedUtc,
                            fileName,
                            data.LongLength,
                            sha256,
                            dbAttachment.Id),
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            dbAttachment.AccItemId = upload.ItemId;
            dbAttachment.AccVersionId = upload.VersionId;
            dbAttachment.SavedFileName = Truncate(safeFileName, 260);
            dbAttachment.IsExternalDownload = true;
            if (message.Status is EmailInboxStatus.Pending or EmailInboxStatus.Error)
            {
                message.Status = EmailInboxStatus.Uploaded;
                message.Error = null;
            }

            message.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"[NativeExternalDownload] Uploaded '{safeFileName}' AccItemId={upload.ItemId} MessageUniqueId={messageUniqueId}");

            return new EmailExternalDownloadResult(
                EmailExternalDownloadOutcome.Succeeded,
                upload.ItemId,
                msgFolderId,
                safeFileName,
                null);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[NativeExternalDownload] Upload failed '{safeFileName}': {ex.Message}");
            return Failed(safeFileName, ex.Message);
        }
    }

    private static async Task<EmailInboxMessage?> EnsureMessageRowAsync(
        SiNetSQLDbContext db,
        EmailExternalDownloadCommand command,
        string messageUniqueId,
        string actingLogin,
        CancellationToken cancellationToken)
    {
        var existing = await db.EmailInboxMessages
            .FirstOrDefaultAsync(m => m.MessageUniqueId == messageUniqueId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var defaultProjectId = await ResolveDefaultOfficeProjectIdAsync(db, cancellationToken)
            .ConfigureAwait(false);
        if (defaultProjectId <= 0)
        {
            return null;
        }

        var internetForThread = string.IsNullOrWhiteSpace(command.InternetMessageId)
            ? messageUniqueId
            : command.InternetMessageId.Trim().Trim('<', '>').Trim();
        var threadUniqueId = EmailMessageIdentity.GetThreadUniqueId(null, null, internetForThread);
        var threadKey = EmailMessageIdentity.GetThreadKey(threadUniqueId);

        var row = new EmailInboxMessage
        {
            MessageUniqueId = messageUniqueId,
            InternetMessageId = Truncate(internetForThread, 255),
            ThreadUniqueId = threadUniqueId,
            ThreadKey = threadKey,
            ProjectId = defaultProjectId,
            Subject = Truncate(command.EmailSubject, 500),
            FromAddress = Truncate(command.EmailFrom, 320),
            ReceivedUtc = command.EmailDate?.ToUniversalTime() ?? DateTime.UtcNow,
            Status = EmailInboxStatus.Pending,
            CreatedByLogin = actingLogin,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        db.EmailInboxMessages.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return row;
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            return await db.EmailInboxMessages
                .FirstOrDefaultAsync(m => m.MessageUniqueId == messageUniqueId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

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

    private static EmailExternalDownloadResult Failed(string? fileName, string error) =>
        new(EmailExternalDownloadOutcome.Failed, null, null, fileName, error);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
