using SiNet.Application.Email.Acc;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.WPFUserControl;
using SiOffice.GoogleConnector;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// V2 host bridge: opens <see cref="ExternalBrowserWindow"/> and forwards completed downloads to the workbench.
/// </summary>
internal sealed class V2EmailExternalDownloadBrowserHost : IEmailExternalDownloadBrowserHost
{
    private EmailExternalDownloadContext? _activeContext;

    public event Action<EmailExternalDownloadCompletedEventArgs>? DownloadCompleted;

    public void OpenDownloadUrl(string url, EmailExternalDownloadContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(context);

        _activeContext = context;
        WebView2Helper.ProjectFileDownloaded += OnProjectFileDownloaded;

        var emailInfo = BuildEmailInfo(context);
        var window = new ExternalBrowserWindow(url, emailInfo)
        {
            Title = $"הורדה — {context.Subject}",
            Width = 1200,
            Height = 800,
        };

        window.Closed += (_, _) =>
        {
            WebView2Helper.ProjectFileDownloaded -= OnProjectFileDownloaded;
            if (ReferenceEquals(_activeContext, context))
            {
                _activeContext = null;
            }
        };

        window.Show();
    }

    private void OnProjectFileDownloaded(string localPath, string fileName, EmailInfo emailInfo)
    {
        if (_activeContext is null)
        {
            return;
        }

        if (!string.Equals(emailInfo.MessageId, _activeContext.GmailMessageId, StringComparison.Ordinal))
        {
            return;
        }

        DownloadCompleted?.Invoke(new EmailExternalDownloadCompletedEventArgs(
            localPath,
            fileName,
            _activeContext));
    }

    private static EmailInfo BuildEmailInfo(EmailExternalDownloadContext context) =>
        new()
        {
            MessageId = context.GmailMessageId,
            InternetMessageId = context.InternetMessageId,
            Subject = context.Subject,
            From = context.From,
            Date = context.ReceivedOn?.ToString("O") ?? string.Empty,
        };
}
