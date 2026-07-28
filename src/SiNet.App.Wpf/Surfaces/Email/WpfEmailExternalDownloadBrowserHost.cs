using SiNet.Application.Email.Acc;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Standalone host bridge: opens <see cref="ExternalDownloadBrowserWindow"/> and forwards
/// completed downloads to the email workbench for ACC upload.
/// </summary>
public sealed class WpfEmailExternalDownloadBrowserHost : IEmailExternalDownloadBrowserHost
{
    private EmailExternalDownloadContext? _activeContext;
    private ExternalDownloadBrowserWindow? _activeWindow;

    public event Action<EmailExternalDownloadCompletedEventArgs>? DownloadCompleted;

    public void OpenDownloadUrl(string url, EmailExternalDownloadContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(context);

        _activeContext = context;
        var window = new ExternalDownloadBrowserWindow(url, context);
        _activeWindow = window;

        window.DownloadCompleted += OnWindowDownloadCompleted;
        window.Closed += (_, _) =>
        {
            window.DownloadCompleted -= OnWindowDownloadCompleted;
            if (ReferenceEquals(_activeContext, context))
            {
                _activeContext = null;
            }

            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
            }
        };

        window.Show();
    }

    public void ReportProgress(EmailExternalDownloadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _activeWindow?.ApplyProgress(progress);
    }

    private void OnWindowDownloadCompleted(string localPath, string fileName)
    {
        if (_activeContext is null)
        {
            return;
        }

        ReportProgress(new EmailExternalDownloadProgress(
            EmailExternalDownloadStage.Uploading,
            $"ההורדה הסתיימה — מתחיל העלאה ל-ACC: {fileName}",
            FileName: fileName));

        DownloadCompleted?.Invoke(new EmailExternalDownloadCompletedEventArgs(
            localPath,
            fileName,
            _activeContext));
    }
}
