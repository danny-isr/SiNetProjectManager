using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Standalone WebView2 window for Jumbo/WeTransfer downloads. Intercepts downloads to a temp
/// folder and raises <see cref="DownloadCompleted"/> for ACC upload (no V2 association dialog).
/// </summary>
public partial class ExternalDownloadBrowserWindow : Window
{
    private readonly string _initialUrl;
    private bool _hasActiveWork;

    public event Action<string, string>? DownloadCompleted;

    public ExternalDownloadBrowserWindow(string url, EmailExternalDownloadContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(context);

        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        _initialUrl = url;
        UrlText.Text = url;
        Title = $"הורדה — {context.Subject}";
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public void ApplyProgress(EmailExternalDownloadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

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
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "ExternalDownloadWebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(null, userData)
                .ConfigureAwait(true);
            await BrowserWebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

            var core = BrowserWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (!string.IsNullOrEmpty(args.Uri))
                {
                    core.Navigate(args.Uri);
                }
            };
            core.SourceChanged += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UrlText.Text = core.Source;
                    Title = $"הורדה — {core.DocumentTitle}";
                });
            };
            core.DownloadStarting += OnDownloadStarting;
            core.Navigate(_initialUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"שגיאה בפתיחת הדפדפן:\n{ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var rawName = Path.GetFileName(e.ResultFilePath);
            var safeName = EmailMessageIdentity.SanitizeFileName(rawName);
            var tempDir = Path.Combine(Path.GetTempPath(), "SiNetExternalDownload");
            Directory.CreateDirectory(tempDir);
            var targetPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{safeName}");

            e.ResultFilePath = targetPath;
            e.Handled = true;
            _hasActiveWork = true;

            ApplyProgress(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Downloading,
                $"מוריד למחשב: {safeName}",
                FileName: safeName));

            var operation = e.DownloadOperation;
            void OnStateChanged(object? s, object? args)
            {
                if (operation.State == CoreWebView2DownloadState.Completed)
                {
                    operation.StateChanged -= OnStateChanged;
                    Dispatcher.Invoke(() =>
                    {
                        ApplyProgress(new EmailExternalDownloadProgress(
                            EmailExternalDownloadStage.Uploading,
                            $"ההורדה הסתיימה — מעלה ל-ACC: {safeName}",
                            FileName: safeName));
                        DownloadCompleted?.Invoke(targetPath, safeName);
                    });
                }
                else if (operation.State == CoreWebView2DownloadState.Interrupted)
                {
                    operation.StateChanged -= OnStateChanged;
                    Dispatcher.Invoke(() =>
                    {
                        ApplyProgress(new EmailExternalDownloadProgress(
                            EmailExternalDownloadStage.Failed,
                            $"ההורדה נקטעה: {safeName}",
                            FileName: safeName));
                    });
                }
            }

            operation.StateChanged += OnStateChanged;
        }
        catch (Exception ex)
        {
            ApplyProgress(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Failed,
                $"שגיאת הורדה: {ex.Message}"));
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_hasActiveWork)
        {
            return;
        }

        var answer = MessageBox.Show(
            "יש הורדה/העלאה פעילה. לסגור בכל זאת?",
            "הורדה חיצונית",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }
}
