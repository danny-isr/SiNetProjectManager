namespace SiNet.Application.Email.Detail;

public interface IEmailExternalDownloadService
{
    Task<EmailExternalDownloadUploadResult> UploadDownloadedFileAsync(
        EmailExternalDownloadUploadCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EmailExternalDownloadUploadCommand(
    int? InboxMessageId,
    string GmailMessageId,
    string? InternetMessageId,
    string LocalFilePath,
    string DisplayFileName,
    string ActingUserLogin);

public sealed record EmailExternalDownloadUploadResult(
    bool Succeeded,
    string? ErrorMessage,
    int? InboxAttachmentId);
