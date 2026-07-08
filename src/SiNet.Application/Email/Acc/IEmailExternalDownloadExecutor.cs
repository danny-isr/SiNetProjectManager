namespace SiNet.Application.Email.Acc;

/// <summary>
/// Host-provided backend that uploads externally downloaded files to ACC Inbox (legacy pipeline).
/// </summary>
public interface IEmailExternalDownloadExecutor
{
    Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        CancellationToken cancellationToken = default);
}
