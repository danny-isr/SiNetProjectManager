using System.IO;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiOffice.GoogleConnector;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host bridge: uploads externally downloaded files via legacy <see cref="EmailIngestionService"/>.
/// </summary>
internal sealed class LegacyEmailExternalDownloadExecutor(
    GoogleService googleService,
    IEmailIngestionServiceFactory ingestionFactory) : IEmailExternalDownloadExecutor
{
    private readonly GoogleService _googleService =
        googleService ?? throw new ArgumentNullException(nameof(googleService));
    private readonly IEmailIngestionServiceFactory _ingestionFactory =
        ingestionFactory ?? throw new ArgumentNullException(nameof(ingestionFactory));

    public async Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        IProgress<EmailExternalDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(
            command.InternetMessageId,
            command.GmailMessageId);

        var ingestion = await _ingestionFactory.CreateAsync(_googleService).ConfigureAwait(false);
        if (ingestion is null)
        {
            return EmailExternalDownloadResult.BackendNotAvailable();
        }

        using (ingestion)
        {
            if (command.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return await UploadZipContentsAsync(
                        ingestion,
                        command,
                        messageUniqueId,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Uploading,
                $"מעלה ל-ACC: {command.FileName}",
                Percent: null,
                CurrentFile: 1,
                TotalFiles: 1,
                FileName: command.FileName));

            var legacy = await ingestion
                .UploadExternalFileToInboxAsync(
                    command.LocalFilePath,
                    messageUniqueId,
                    command.FileName,
                    subfolderName: null,
                    command.EmailSubject,
                    command.EmailFrom,
                    command.EmailDate,
                    internetMessageId: command.InternetMessageId,
                    ct: cancellationToken)
                .ConfigureAwait(false);

            var mapped = MapResult(legacy);
            progress?.Report(mapped.Succeeded
                ? new EmailExternalDownloadProgress(
                    EmailExternalDownloadStage.Completed,
                    $"הועלה בהצלחה: {command.FileName}",
                    Percent: 100,
                    FileName: command.FileName)
                : new EmailExternalDownloadProgress(
                    EmailExternalDownloadStage.Failed,
                    mapped.ErrorMessage ?? $"העלאה נכשלה: {command.FileName}",
                    FileName: command.FileName));
            return mapped;
        }
    }

    private static async Task<EmailExternalDownloadResult> UploadZipContentsAsync(
        IEmailIngestionService ingestion,
        EmailExternalDownloadCommand command,
        string messageUniqueId,
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
            System.IO.Compression.ZipFile.ExtractToDirectory(command.LocalFilePath, extractFolder, overwriteFiles: true);

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
                return new EmailExternalDownloadResult(
                    EmailExternalDownloadOutcome.Failed,
                    null,
                    null,
                    command.FileName,
                    "קובץ ZIP ריק — לא הועלה דבר ל-ACC");
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

                var legacy = await ingestion
                    .UploadExternalFileToInboxAsync(
                        filePath,
                        messageUniqueId,
                        entryName,
                        zipSubfolder,
                        command.EmailSubject,
                        command.EmailFrom,
                        command.EmailDate,
                        internetMessageId: command.InternetMessageId,
                        ct: cancellationToken)
                    .ConfigureAwait(false);

                if (legacy.Success)
                {
                    uploaded++;
                    accFolderId ??= legacy.AccFolderId;
                }
                else
                {
                    lastError = legacy.ErrorMessage ?? entryName;
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
            return new EmailExternalDownloadResult(
                EmailExternalDownloadOutcome.Failed,
                null,
                null,
                command.FileName,
                lastError ?? "העלאת תוכן ה-ZIP ל-ACC נכשלה");
        }
        catch (InvalidDataException)
        {
            progress?.Report(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Uploading,
                $"מעלה ל-ACC (לא ZIP תקין): {command.FileName}",
                FileName: command.FileName));

            var legacy = await ingestion
                .UploadExternalFileToInboxAsync(
                    command.LocalFilePath,
                    messageUniqueId,
                    command.FileName,
                    subfolderName: null,
                    command.EmailSubject,
                    command.EmailFrom,
                    command.EmailDate,
                    internetMessageId: command.InternetMessageId,
                    ct: cancellationToken)
                .ConfigureAwait(false);

            return MapResult(legacy);
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

    private static EmailExternalDownloadResult MapResult(ExternalUploadResult legacy) =>
        legacy.Success
            ? new EmailExternalDownloadResult(
                EmailExternalDownloadOutcome.Succeeded,
                legacy.AccItemId,
                legacy.AccFolderId,
                legacy.FileName,
                null)
            : new EmailExternalDownloadResult(
                EmailExternalDownloadOutcome.Failed,
                null,
                null,
                legacy.FileName,
                legacy.ErrorMessage ?? "העלאה ל-ACC נכשלה");
}
