using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

internal sealed class SqlEmailExternalDownloadService(IEmailExternalDownloadExecutor? downloadExecutor = null)
    : IEmailExternalDownloadService
{
    private readonly IEmailExternalDownloadExecutor? _downloadExecutor = downloadExecutor;

    public async Task<EmailExternalDownloadUploadResult> UploadDownloadedFileAsync(
        EmailExternalDownloadUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_downloadExecutor is null)
        {
            return new EmailExternalDownloadUploadResult(
                false,
                "External download backend is not configured.",
                InboxAttachmentId: null);
        }

        var result = await _downloadExecutor
            .UploadExternalFileAsync(
                new EmailExternalDownloadCommand(
                    command.GmailMessageId,
                    command.InternetMessageId,
                    command.LocalFilePath,
                    command.DisplayFileName,
                    EmailSubject: null,
                    EmailFrom: null,
                    EmailDate: null,
                    command.ActingUserLogin),
                cancellationToken)
            .ConfigureAwait(false);

        return new EmailExternalDownloadUploadResult(
            result.Outcome == EmailExternalDownloadOutcome.Succeeded,
            result.ErrorMessage,
            InboxAttachmentId: null);
    }
}
