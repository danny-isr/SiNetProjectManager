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
                        cancellationToken)
                    .ConfigureAwait(false);
            }

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
    }

    private static async Task<EmailExternalDownloadResult> UploadZipContentsAsync(
        IEmailIngestionService ingestion,
        EmailExternalDownloadCommand command,
        string messageUniqueId,
        CancellationToken cancellationToken)
    {
        var extractFolder = Path.Combine(
            Path.GetTempPath(),
            "SiNetExternalZip",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(extractFolder);
            System.IO.Compression.ZipFile.ExtractToDirectory(command.LocalFilePath, extractFolder, overwriteFiles: true);

            var files = Directory
                .GetFiles(extractFolder, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
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

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = Path.GetFileName(filePath);
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
            }

            return uploaded > 0
                ? new EmailExternalDownloadResult(
                    EmailExternalDownloadOutcome.Succeeded,
                    null,
                    accFolderId,
                    command.FileName,
                    null)
                : new EmailExternalDownloadResult(
                    EmailExternalDownloadOutcome.Failed,
                    null,
                    null,
                    command.FileName,
                    "העלאת תוכן ה-ZIP ל-ACC נכשלה");
        }
        catch (InvalidDataException)
        {
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
