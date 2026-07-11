namespace SiNet.Application.Email.Acc;

/// <summary>
/// Host-provided UI bridge for opening JumboMail / WeTransfer links and receiving completed downloads.
/// </summary>
public interface IEmailExternalDownloadBrowserHost
{
    event Action<EmailExternalDownloadCompletedEventArgs>? DownloadCompleted;

    void OpenDownloadUrl(string url, EmailExternalDownloadContext context);

    void ReportProgress(EmailExternalDownloadProgress progress);
}

public sealed record EmailExternalDownloadContext(
    string GmailMessageId,
    string? InternetMessageId,
    string Subject,
    string From,
    DateTime? ReceivedOn);

public sealed record EmailExternalDownloadCompletedEventArgs(
    string LocalFilePath,
    string FileName,
    EmailExternalDownloadContext Context);
