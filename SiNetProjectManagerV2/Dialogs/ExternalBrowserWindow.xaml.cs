using Microsoft.Web.WebView2.Core;
using SiNet.Application.Email.Acc;
using SiNetProjectManagerV2.WPFUserControl;
using SiNetSQL.Services.EmailIngestion;
using SiOffice.GoogleConnector;
using System.IO;
using System.Windows;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Floating browser window for viewing external links clicked from emails.
/// Uses a clean WebView2 instance (no Gmail/Calendar clean-view scripts) that shares
/// the app-wide unified WebView2 profile (see <see cref="WebView2Helper.CreateSharedEnvironmentAsync"/>),
/// so cookies / login state (Gmail, Autodesk, ACC, etc.) are reused across windows.
/// Downloads are intercepted and routed via the
/// <see cref="DownloadAssociationDialog"/> for project association.
/// </summary>
public partial class ExternalBrowserWindow : Window
{
    private readonly EmailInfo? _emailInfo;
    private readonly string _initialUrl;
    private bool _hasActiveWork;

    public ExternalBrowserWindow(string url, EmailInfo? emailInfo)
    {
        InitializeComponent();
        _initialUrl = url;
        _emailInfo = emailInfo;

        UrlText.Text = url;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
    }

    public void ApplyProgress(EmailExternalDownloadProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyProgress(progress));
            return;
        }

        ProgressPanel.Visibility = Visibility.Visible;
        ProgressStageText.Text = progress.Stage switch
        {
            EmailExternalDownloadStage.Downloading => "מוריד למחשב…",
            EmailExternalDownloadStage.Extracting => "מחלץ ZIP…",
            EmailExternalDownloadStage.Uploading => "מעלה ל-ACC…",
            EmailExternalDownloadStage.Completed => "הושלם",
            EmailExternalDownloadStage.Failed => "שגיאה",
            _ => progress.Stage.ToString(),
        };
        ProgressDetailText.Text = progress.Message;

        if (progress.Percent is int percent and >= 0 and <= 100)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = percent;
        }
        else if (progress.Stage is EmailExternalDownloadStage.Completed or EmailExternalDownloadStage.Failed)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = progress.Stage == EmailExternalDownloadStage.Completed ? 100 : 0;
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
        }

        _hasActiveWork = progress.Stage is not (
            EmailExternalDownloadStage.Completed or EmailExternalDownloadStage.Failed);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await WebView2Helper.CreateSharedEnvironmentAsync(
                source: "ExternalBrowserWindow");
            await BrowserWebView.EnsureCoreWebView2Async(environment);

            ConfigureBrowser(BrowserWebView.CoreWebView2);
            BrowserWebView.CoreWebView2.Navigate(_initialUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"ExternalBrowser: Initialization failed - {ex.Message}");
            MessageBox.Show(
                $"שגיאה בפתיחת הדפדפן:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void ConfigureBrowser(CoreWebView2 coreWebView)
    {
        WebView2Helper.EnableScrollWheelFocus(BrowserWebView);
        coreWebView.Settings.UserAgent = WebView2Helper.ChromeUserAgent;

        coreWebView.SourceChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                UrlText.Text = coreWebView.Source;
                Title = $"דפדפן חיצוני — {coreWebView.DocumentTitle}";
            });
        };

        coreWebView.NewWindowRequested += (sender, args) =>
        {
            args.Handled = true;
            if (sender is CoreWebView2 core && !string.IsNullOrEmpty(args.Uri))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ExternalBrowser: New-window → navigating internally: {args.Uri}");
                core.Navigate(args.Uri);
            }
        };

        coreWebView.DownloadStarting += (_, args) => HandleDownload(args);

        System.Diagnostics.Debug.WriteLine("ExternalBrowser: Browser configured (UA + navigation + download interception)");
    }

    private void HandleDownload(CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var rawFileName = Path.GetFileName(e.ResultFilePath);
            var sanitizedFileName = MessageKeyGenerator.SanitizeFileName(rawFileName);

            var projectName = WebView2Helper.ResolveProjectNameFromEmail(_emailInfo);
            var hasProject = _emailInfo != null
                           && !string.IsNullOrEmpty(_emailInfo.MessageId)
                           && !string.IsNullOrEmpty(projectName);

            var deferral = e.GetDeferral();

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var dialog = new DownloadAssociationDialog(
                        sanitizedFileName, hasProject ? projectName : null)
                    {
                        Owner = this
                    };

                    dialog.ShowDialog();

                    switch (dialog.ChosenAction)
                    {
                        case DownloadAction.UploadToAcc:
                        case DownloadAction.AssociateToProject:
                            if (_emailInfo != null && !string.IsNullOrEmpty(_emailInfo.MessageId))
                            {
                                var accPath = WebView2Helper.BuildAccMirroredPath(
                                    _emailInfo, sanitizedFileName);
                                if (!WebView2Helper.ResolveDuplicateFilePath(this, sanitizedFileName, accPath, out var resolvedAccPath))
                                {
                                    e.Cancel = true;
                                    e.Handled = true;
                                    return;
                                }

                                e.ResultFilePath = resolvedAccPath;
                                e.Handled = true;
                                System.Diagnostics.Debug.WriteLine(
                                    $"ExternalBrowser: Download → ACC path: {resolvedAccPath}");
                                ApplyProgress(new EmailExternalDownloadProgress(
                                    EmailExternalDownloadStage.Downloading,
                                    $"מוריד למחשב: {sanitizedFileName}",
                                    FileName: sanitizedFileName));
                                WebView2Helper.TrackDownloadCompletion(
                                    e.DownloadOperation,
                                    _emailInfo,
                                    OnDownloadBytesProgress);
                            }
                            else
                            {
                                var fallbackPath = Path.Combine(
                                    Environment.GetFolderPath(
                                        Environment.SpecialFolder.UserProfile),
                                    "Downloads", sanitizedFileName);
                                if (!WebView2Helper.ResolveDuplicateFilePath(this, sanitizedFileName, fallbackPath, out var resolvedFallbackPath))
                                {
                                    e.Cancel = true;
                                    e.Handled = true;
                                    return;
                                }

                                e.ResultFilePath = resolvedFallbackPath;
                                e.Handled = true;
                                System.Diagnostics.Debug.WriteLine(
                                    $"ExternalBrowser: Download → fallback Downloads: {resolvedFallbackPath}");
                            }
                            break;

                        case DownloadAction.SaveToDownloads:
                            var downloadsPath = Path.Combine(
                                Environment.GetFolderPath(
                                    Environment.SpecialFolder.UserProfile),
                                "Downloads", sanitizedFileName);
                            if (!WebView2Helper.ResolveDuplicateFilePath(this, sanitizedFileName, downloadsPath, out var resolvedDownloadsPath))
                            {
                                e.Cancel = true;
                                e.Handled = true;
                                return;
                            }

                            e.ResultFilePath = resolvedDownloadsPath;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine(
                                $"ExternalBrowser: Download → Downloads (local only): {resolvedDownloadsPath}");
                            break;

                        default:
                            e.Cancel = true;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine(
                                "ExternalBrowser: Download cancelled by user");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ExternalBrowser: Error in download dialog - {ex.Message}");
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"ExternalBrowser: Error handling download - {ex.Message}");
        }
    }

    private void OnDownloadBytesProgress(long bytesReceived, long? totalBytes, string fileName)
    {
        int? percent = null;
        string message;
        if (totalBytes is > 0)
        {
            percent = (int)Math.Clamp(Math.Round(bytesReceived * 100.0 / totalBytes.Value), 0, 100);
            message = $"מוריד למחשב: {fileName} ({percent}%)";
        }
        else
        {
            var mb = bytesReceived / (1024.0 * 1024.0);
            message = $"מוריד למחשב: {fileName} ({mb:0.0} MB)";
        }

        ApplyProgress(new EmailExternalDownloadProgress(
            EmailExternalDownloadStage.Downloading,
            message,
            Percent: percent,
            FileName: fileName));
    }

    public void NavigateTo(string url, string title)
    {
        Title = title;
        Dispatcher.Invoke(() => UrlText.Text = url);

        if (BrowserWebView.CoreWebView2 is { } core)
        {
            core.Navigate(url);
            System.Diagnostics.Debug.WriteLine(
                $"ExternalBrowser: Navigated to {url}");
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_hasActiveWork)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "הורדה או העלאה ל-ACC עדיין בתהליך.\nלסגור בכל זאת?",
            "פעולה פעילה",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            BrowserWebView.Dispose();
            System.Diagnostics.Debug.WriteLine("ExternalBrowser: Window closed, WebView2 disposed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"ExternalBrowser: Error disposing WebView2 - {ex.Message}");
        }
    }
}
