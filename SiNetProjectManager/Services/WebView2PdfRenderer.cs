using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;

namespace SiNetProjectManager.Services;

/// <summary>
/// WebView2-based implementation of IEmailPdfRenderer.
/// 
/// Provides two PDF generation paths:
/// 1. <b>Live-view WYSIWYG</b> — captures the active email WebView2 control
///    (with CSS/JS clean-view injections) via <c>PrintToPdfAsync</c>.
///    Registered with <see cref="RegisterLiveView"/>/<see cref="UnregisterLiveView"/>.
/// 2. <b>Hidden-renderer fallback</b> — navigates raw HTML into an off-screen
///    WebView2 and prints. Used when no live view is available.
/// 
/// Usage:
/// - Register as singleton in DI container
/// - Call InitializeAsync once during app startup (hidden renderer)
/// - Call RegisterLiveView when the email viewer loads
/// - Pass to EmailIngestionServiceFactory for email body PDF generation
/// </summary>
public sealed class WebView2PdfRenderer : IEmailPdfRenderer, IDisposable
{
    // ══════════════════════════════════════════════════════════════════
    // Hidden renderer (fallback)
    // ══════════════════════════════════════════════════════════════════
    private WebView2? _webView;
    private Window? _hiddenWindow;
    private bool _isInitialized;
    private bool _isDisposed;
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    // ══════════════════════════════════════════════════════════════════
    // Live-view WYSIWYG capture
    // ══════════════════════════════════════════════════════════════════
    private WebView2? _liveWebView;
    private readonly SemaphoreSlim _livePrintLock = new(1, 1);

    /// <summary>A4 paper width in inches.</summary>
    private const double A4WidthInches = 8.27;
    /// <summary>A4 paper height in inches.</summary>
    private const double A4HeightInches = 11.69;
    /// <summary>Delay (ms) after readyState=complete to let remote images finish rendering.</summary>
    private const int LiveViewSettlingDelayMs = 500;

    /// <inheritdoc />
    public bool IsAvailable => _isInitialized && !_isDisposed;

    /// <inheritdoc />
    public bool IsLiveViewAvailable => _liveWebView?.CoreWebView2 != null && !_isDisposed;

    /// <summary>
    /// Registers the live email-viewer WebView2 for WYSIWYG PDF capture.
    /// Call from the email view's <c>Loaded</c> / <c>DataContextChanged</c> handler.
    /// </summary>
    public void RegisterLiveView(WebView2 webView)
    {
        _liveWebView = webView;
        System.Diagnostics.Debug.WriteLine("[WebView2Pdf] Live email view registered for WYSIWYG capture");
    }

    /// <summary>
    /// Unregisters the live view (e.g. when the email tab is unloaded).
    /// </summary>
    public void UnregisterLiveView()
    {
        _liveWebView = null;
        System.Diagnostics.Debug.WriteLine("[WebView2Pdf] Live email view unregistered");
    }

    /// <summary>
    /// Initializes the WebView2 control. Must be called from UI thread during app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed)
            return;

        AppLogger.Info("[WebView2Pdf] Initializing WebView2 PDF renderer...");

        try
        {
            // Create a hidden window to host the WebView2 control
            _hiddenWindow = new Window
            {
                Title = "PDF Renderer (Hidden)",
                Width = 1024,
                Height = 768,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Visibility = Visibility.Hidden,
                AllowsTransparency = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000
            };

            _webView = new WebView2
            {
                Width = 1024,
                Height = 768
            };

            _hiddenWindow.Content = _webView;
            _hiddenWindow.Show();
            _hiddenWindow.Hide();

            // Initialize WebView2 core
            await _webView.EnsureCoreWebView2Async();

            _isInitialized = true;
            AppLogger.Info("[WebView2Pdf] ✓ WebView2 PDF renderer initialized successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[WebView2Pdf] Failed to initialize WebView2 PDF renderer");
            _isInitialized = false;
            Cleanup();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RenderToPdfAsync(string htmlContent, string outputPdfPath, CancellationToken ct)
    {
        if (!_isInitialized || _isDisposed || _webView == null)
        {
            AppLogger.Error("[EmailBodyPdf] WebView2 not available for PDF rendering");
            return false;
        }

        if (string.IsNullOrEmpty(htmlContent))
        {
            AppLogger.Warn("[EmailBodyPdf] Cannot render empty HTML to PDF");
            return false;
        }

        // Ensure we're on the UI thread
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            AppLogger.Error("[EmailBodyPdf] No UI dispatcher available");
            return false;
        }

        if (!dispatcher.CheckAccess())
        {
            // Dispatch to UI thread
            return await dispatcher.InvokeAsync(async () =>
                await RenderToPdfCoreAsync(htmlContent, outputPdfPath, ct),
                DispatcherPriority.Normal, ct).Task.Unwrap();
        }

        return await RenderToPdfCoreAsync(htmlContent, outputPdfPath, ct);
    }

    /// <summary>
    /// Core PDF rendering logic. Must run on UI thread.
    /// Includes proper waiting for DOM and images to load.
    /// </summary>
    private async Task<bool> RenderToPdfCoreAsync(string htmlContent, string outputPdfPath, CancellationToken ct)
    {
        const int ImageLoadTimeoutSeconds = 15;
        const int SettlingDelayMs = 500;
        const int PollIntervalMs = 200;

        await _renderLock.WaitAsync(ct);
        try
        {
            if (_webView?.CoreWebView2 == null)
            {
                AppLogger.Error("[EmailBodyPdf] CoreWebView2 is null");
                return false;
            }

            AppLogger.Info($"[EmailBodyPdf] WebView2 init ok");
            AppLogger.Info($"[EmailBodyPdf] Navigating HTML length={htmlContent.Length}");

            // Set up navigation completed handler
            var navigationTcs = new TaskCompletionSource<bool>();

            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                navigationTcs.TrySetResult(args.IsSuccess);
            }

            _webView.NavigationCompleted += OnNavigationCompleted;

            try
            {
                // Navigate to the HTML content
                _webView.NavigateToString(htmlContent);

                // Wait for navigation with timeout
                using var navTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var navLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, navTimeoutCts.Token);

                var completedTask = await Task.WhenAny(
                    navigationTcs.Task,
                    Task.Delay(Timeout.Infinite, navLinkedCts.Token));

                if (completedTask != navigationTcs.Task)
                {
                    AppLogger.Error("[EmailBodyPdf] Navigation timed out");
                    return false;
                }

                var navigationSuccess = await navigationTcs.Task;
                if (!navigationSuccess)
                {
                    AppLogger.Error("[EmailBodyPdf] Navigation failed");
                    return false;
                }

                AppLogger.Info("[EmailBodyPdf] NavigationCompleted ok");

                // ═══════════════════════════════════════════════════════════════════════════
                // STEP 1: Wait for document.readyState === 'complete'
                // ═══════════════════════════════════════════════════════════════════════════
                AppLogger.Info("[EmailBodyPdf] Waiting for readyState=complete...");
                var readyStateReached = await WaitForConditionAsync(
                    "document.readyState === 'complete'",
                    TimeSpan.FromSeconds(10),
                    PollIntervalMs,
                    ct);

                if (!readyStateReached)
                {
                    AppLogger.Warn("[EmailBodyPdf] readyState did not reach 'complete' in time, continuing anyway");
                }
                else
                {
                    AppLogger.Info("[EmailBodyPdf] readyState=complete ✓");
                }

                // ═══════════════════════════════════════════════════════════════════════════
                // STEP 2: Wait for all images to load
                // ═══════════════════════════════════════════════════════════════════════════
                var imageCountResult = await ExecuteScriptSafeAsync("document.images.length");
                var totalImages = int.TryParse(imageCountResult?.Trim('"'), out var imgCount) ? imgCount : 0;

                if (totalImages > 0)
                {
                    AppLogger.Info($"[EmailBodyPdf] Waiting for images: total={totalImages}");

                    var imageLoadStart = DateTime.UtcNow;
                    var imageLoadTimeout = TimeSpan.FromSeconds(ImageLoadTimeoutSeconds);
                    var allImagesLoaded = false;

                    while (DateTime.UtcNow - imageLoadStart < imageLoadTimeout && !ct.IsCancellationRequested)
                    {
                        // Check how many images are complete
                        var completeCountResult = await ExecuteScriptSafeAsync(
                            "Array.from(document.images).filter(img => img.complete && img.naturalWidth > 0).length");
                        var completeCount = int.TryParse(completeCountResult?.Trim('"'), out var cc) ? cc : 0;

                        AppLogger.Info($"[EmailBodyPdf] Waiting for images: complete={completeCount}/{totalImages}");

                        if (completeCount >= totalImages)
                        {
                            allImagesLoaded = true;
                            break;
                        }

                        await Task.Delay(PollIntervalMs, ct);
                    }

                    if (allImagesLoaded)
                    {
                        AppLogger.Info("[EmailBodyPdf] Images loaded ✓");
                    }
                    else
                    {
                        AppLogger.Warn("[EmailBodyPdf] Image load timeout - some images may be missing, continuing anyway");
                    }
                }
                else
                {
                    AppLogger.Info("[EmailBodyPdf] No images in document");
                }

                // ═══════════════════════════════════════════════════════════════════════════
                // STEP 3: Settling delay for final rendering
                // ═══════════════════════════════════════════════════════════════════════════
                AppLogger.Info($"[EmailBodyPdf] Settling delay: {SettlingDelayMs} ms");
                await Task.Delay(SettlingDelayMs, ct);

                // ═══════════════════════════════════════════════════════════════════════════
                // STEP 4: Print to PDF
                // ═══════════════════════════════════════════════════════════════════════════
                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPdfPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Configure print settings
                var printSettings = _webView.CoreWebView2.Environment.CreatePrintSettings();
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;
                printSettings.MarginTop = 0.4;
                printSettings.MarginBottom = 0.4;
                printSettings.MarginLeft = 0.4;
                printSettings.MarginRight = 0.4;

                AppLogger.Info($"[EmailBodyPdf] Printing to PDF path={outputPdfPath}");

                // Print to PDF
                var printResult = await _webView.CoreWebView2.PrintToPdfAsync(outputPdfPath, printSettings);

                if (!printResult)
                {
                    AppLogger.Error("[EmailBodyPdf] PrintToPdfAsync returned false");
                    return false;
                }

                // Verify file was created
                if (!File.Exists(outputPdfPath))
                {
                    AppLogger.Error("[EmailBodyPdf] PDF file was not created");
                    return false;
                }

                var fileInfo = new FileInfo(outputPdfPath);
                AppLogger.Info($"[EmailBodyPdf] ✓ PDF generated size={fileInfo.Length} bytes");

                return true;
            }
            finally
            {
                _webView.NavigationCompleted -= OnNavigationCompleted;
            }
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn("[EmailBodyPdf] PDF rendering cancelled");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[EmailBodyPdf] Exception during PDF rendering");
            return false;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    /// Waits for a JavaScript condition to become true.
    /// </summary>
    private async Task<bool> WaitForConditionAsync(string jsCondition, TimeSpan timeout, int pollIntervalMs, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout && !ct.IsCancellationRequested)
        {
            var result = await ExecuteScriptSafeAsync(jsCondition);
            if (result?.Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
            await Task.Delay(pollIntervalMs, ct);
        }
        return false;
    }

    /// <summary>
    /// Executes JavaScript safely, returning null on error.
    /// </summary>
    private async Task<string?> ExecuteScriptSafeAsync(string script)
    {
        try
        {
            if (_webView?.CoreWebView2 == null) return null;
            return await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            return null;
        }
    }

    private void Cleanup()
    {
        try
        {
            _webView?.Dispose();
            _webView = null;

            _hiddenWindow?.Close();
            _hiddenWindow = null;
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Live-view WYSIWYG PDF capture
    // ══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<bool> PrintLiveViewToPdfAsync(string outputPdfPath, CancellationToken ct)
    {
        if (_isDisposed || _liveWebView?.CoreWebView2 == null)
        {
            AppLogger.Error("[LiveViewPdf] Live WebView2 not available for WYSIWYG capture");
            return false;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            AppLogger.Error("[LiveViewPdf] No UI dispatcher available");
            return false;
        }

        if (!dispatcher.CheckAccess())
        {
            return await dispatcher.InvokeAsync(async () =>
                await PrintLiveViewCoreAsync(outputPdfPath, ct),
                DispatcherPriority.Normal, ct).Task.Unwrap();
        }

        return await PrintLiveViewCoreAsync(outputPdfPath, ct);
    }

    /// <summary>
    /// Core live-view print logic. Must run on UI thread.
    /// Waits for <c>document.readyState === 'complete'</c> and the clean-view
    /// isolation script to finish, then prints to A4 PDF with no header/footer.
    /// </summary>
    private async Task<bool> PrintLiveViewCoreAsync(string outputPdfPath, CancellationToken ct)
    {
        const int PollIntervalMs = 200;

        await _livePrintLock.WaitAsync(ct);
        try
        {
            var coreWv = _liveWebView?.CoreWebView2;
            if (coreWv == null)
            {
                AppLogger.Error("[LiveViewPdf] CoreWebView2 became null before print");
                return false;
            }

            var currentUri = coreWv.Source ?? "(unavailable)";
            AppLogger.Info($"[LiveViewPdf] Starting WYSIWYG capture of: {currentUri}");

            // ═══════════════════════════════════════════════════════════
            // STEP 1: Wait for document.readyState === 'complete'
            // ═══════════════════════════════════════════════════════════
            var readyStateReached = await WaitForLiveConditionAsync(
                coreWv, "document.readyState === 'complete'",
                TimeSpan.FromSeconds(10), PollIntervalMs, ct);

            if (!readyStateReached)
            {
                AppLogger.Warn("[LiveViewPdf] readyState did not reach 'complete', continuing anyway");
            }
            else
            {
                AppLogger.Info("[LiveViewPdf] readyState=complete \u2713");
            }

            // ═══════════════════════════════════════════════════════════
            // STEP 2: Wait for clean-view isolation to be active
            // ═══════════════════════════════════════════════════════════
            var isolationReady = await WaitForLiveConditionAsync(
                coreWv,
                "!!(document.querySelector('.ii.gt') || document.querySelector('.a3s'))",
                TimeSpan.FromSeconds(5), PollIntervalMs, ct);

            if (!isolationReady)
            {
                AppLogger.Warn("[LiveViewPdf] Email body selector not found, PDF may include Gmail chrome");
            }
            else
            {
                AppLogger.Info("[LiveViewPdf] Email body element detected \u2713");
            }

            // ═══════════════════════════════════════════════════════════
            // STEP 3: Settling delay for remote images
            // ═══════════════════════════════════════════════════════════
            AppLogger.Info($"[LiveViewPdf] Settling delay: {LiveViewSettlingDelayMs} ms");
            await Task.Delay(LiveViewSettlingDelayMs, ct);

            // ═══════════════════════════════════════════════════════════
            // STEP 4: Ensure output directory exists
            // ═══════════════════════════════════════════════════════════
            var outputDir = Path.GetDirectoryName(outputPdfPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // ═══════════════════════════════════════════════════════════
            // STEP 5: Print to PDF with professional A4 settings
            // ═══════════════════════════════════════════════════════════
            var printSettings = coreWv.Environment.CreatePrintSettings();
            printSettings.ShouldPrintHeaderAndFooter = false;
            printSettings.ShouldPrintBackgrounds = true;
            printSettings.PageWidth = A4WidthInches;
            printSettings.PageHeight = A4HeightInches;
            printSettings.MarginTop = 0.4;
            printSettings.MarginBottom = 0.4;
            printSettings.MarginLeft = 0.4;
            printSettings.MarginRight = 0.4;

            AppLogger.Info($"[LiveViewPdf] Printing to PDF path={outputPdfPath}");

            var printResult = await coreWv.PrintToPdfAsync(outputPdfPath, printSettings);

            if (!printResult)
            {
                AppLogger.Error("[LiveViewPdf] PrintToPdfAsync returned false");
                return false;
            }

            if (!File.Exists(outputPdfPath))
            {
                AppLogger.Error("[LiveViewPdf] PDF file was not created");
                return false;
            }

            var fileInfo = new FileInfo(outputPdfPath);
            AppLogger.Info($"[LiveViewPdf] \u2713 WYSIWYG PDF generated size={fileInfo.Length} bytes");
            return true;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn("[LiveViewPdf] WYSIWYG capture cancelled");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[LiveViewPdf] Exception during WYSIWYG capture");
            return false;
        }
        finally
        {
            _livePrintLock.Release();
        }
    }

    /// <summary>
    /// Polls a JS condition on a specific <see cref="CoreWebView2"/> instance.
    /// </summary>
    private static async Task<bool> WaitForLiveConditionAsync(
        CoreWebView2 coreWv, string jsCondition, TimeSpan timeout, int pollIntervalMs, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await coreWv.ExecuteScriptAsync(jsCondition);
                if (result?.Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
            catch
            {
                // Ignore transient script errors
            }
            await Task.Delay(pollIntervalMs, ct);
        }
        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _liveWebView = null;
        Cleanup();
        _renderLock.Dispose();
        _livePrintLock.Dispose();
    }
}
