using Microsoft.Web.WebView2.Core;
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
/// <para>
/// The user can close this window manually after browsing/downloading.
/// </para>
/// </summary>
public partial class ExternalBrowserWindow : Window
{
    private readonly EmailInfo? _emailInfo;
    private readonly string _initialUrl;

    public ExternalBrowserWindow(string url, EmailInfo? emailInfo)
    {
        InitializeComponent();
        _initialUrl = url;
        _emailInfo = emailInfo;

        UrlText.Text = url;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Gap 18F: use the unified shared WebView2 profile so this floating
            // browser shares cookies / login state (Gmail, Autodesk, ACC, etc.)
            // with every other WebView2 window in the app.
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
        // Enable scroll wheel by auto-focusing on mouse enter
        WebView2Helper.EnableScrollWheelFocus(BrowserWebView);

        // Spoof UA to look like standalone Chrome (same as main WebView2)
        coreWebView.Settings.UserAgent = WebView2Helper.ChromeUserAgent;

        // Track current URL in the address bar
        coreWebView.SourceChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                UrlText.Text = coreWebView.Source;
                Title = $"דפדפן חיצוני — {coreWebView.DocumentTitle}";
            });
        };

        // Open new-window requests internally (stay in this floating window)
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

        // Intercept downloads — show association dialog
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
                            // Save to ACC-mirrored path + trigger ACC Inbox upload pipeline
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
                                WebView2Helper.TrackDownloadCompletion(
                                    e.DownloadOperation, _emailInfo);
                            }
                            else
                            {
                                // Fallback: save to Downloads if no email context
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
                            // Local save only — no ACC upload
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

                        default: // Cancel
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

    /// <summary>
    /// Navigates the existing WebView2 instance to a new URL without creating a new window.
    /// If CoreWebView2 is not yet initialized, updates the initial URL for the next OnLoaded call.
    /// </summary>
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
