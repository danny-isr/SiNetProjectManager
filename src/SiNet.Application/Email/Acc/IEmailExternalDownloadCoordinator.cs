namespace SiNet.Application.Email.Acc;

public interface IEmailExternalDownloadCoordinator
{
    Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailExternalDownloadItem>> ListExternalDownloadsAsync(
        string? internetMessageId,
        string gmailMessageId,
        CancellationToken cancellationToken = default);
}
