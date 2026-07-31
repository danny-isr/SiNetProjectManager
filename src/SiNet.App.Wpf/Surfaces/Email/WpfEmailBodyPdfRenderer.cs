using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.Acc;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Hidden WebView2 print-to-PDF for ACC Inbox <c>00_Email.pdf</c> (Native N4).
/// Singleton — initialize once on the UI thread (lazy or via <see cref="InitializeAsync"/>).
/// CID images use the same virtual-host pattern as <see cref="WebView2EmailBodyRenderer"/>.
/// </summary>
public sealed class WpfEmailBodyPdfRenderer : IEmailBodyPdfRenderer, IDisposable
{
    private const string InlineImageHost = "sinet-mail-images.local";
    private const int ImageLoadTimeoutSeconds = 15;
    private const int SettlingDelayMs = 500;
    private const int PollIntervalMs = 200;

    private static readonly Regex CidReferenceRegex = new(
        "(?<prefix>src\\s*=\\s*[\"'])cid:(?<cid>[^\"']+)(?<suffix>[\"'])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly Dictionary<string, InlineImageEntry> _inlineImages = new(StringComparer.OrdinalIgnoreCase);
    private WebView2? _webView;
    private Window? _hiddenWindow;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _webResourceHandlerAttached;

    /// <summary>
    /// Thread-safe flag only — never touch <see cref="WebView2"/> here (ingest runs off UI thread).
    /// </summary>
    public bool IsAvailable => _isInitialized && !_isDisposed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(
                    () => InitializeAsync(cancellationToken),
                    DispatcherPriority.Normal,
                    cancellationToken)
                .Task
                .Unwrap()
                .ConfigureAwait(true);
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_isInitialized || _isDisposed)
            {
                return;
            }

            _hiddenWindow = new Window
            {
                Title = "SiNet Email Body PDF (Hidden)",
                Width = 1024,
                Height = 768,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Visibility = Visibility.Hidden,
                AllowsTransparency = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };

            _webView = new WebView2 { Width = 1024, Height = 768 };
            _hiddenWindow.Content = _webView;
            _hiddenWindow.Show();
            _hiddenWindow.Hide();

            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            EnsureWebResourceHandler();
            _isInitialized = _webView.CoreWebView2 is not null;
            Trace.TraceInformation($"[EmailBodyPdf] Initialize complete available={_isInitialized}");
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "WpfEmailBodyPdfRenderer.InitializeAsync",
                "init complete",
                new { available = _isInitialized },
                runId: "post-fix");
            // #endregion
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[EmailBodyPdf] Initialize failed: {ex.GetType().Name}: {ex.Message}");
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "WpfEmailBodyPdfRenderer.InitializeAsync",
                "init failed",
                new { errorType = ex.GetType().Name, error = ex.Message },
                runId: "post-fix");
            // #endregion
            _isInitialized = false;
            Cleanup();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> RenderHtmlToPdfAsync(
        string htmlDocument,
        string outputPdfPath,
        IReadOnlyList<EmailInlineImage>? inlineImages = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(htmlDocument) || string.IsNullOrWhiteSpace(outputPdfPath))
        {
            return false;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Trace.TraceWarning("[EmailBodyPdf] Render aborted — no WPF Application.Current dispatcher.");
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                "no dispatcher",
                null,
                runId: "post-fix");
            // #endregion
            return false;
        }

        // Marshal BEFORE any WebView2 touch — ingest calls this from a thread-pool thread.
        if (!dispatcher.CheckAccess())
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                "marshal to UI thread",
                new { htmlLen = htmlDocument.Length, inlineCount = inlineImages?.Count ?? 0 },
                runId: "post-fix");
            // #endregion
            return await dispatcher.InvokeAsync(
                    () => RenderHtmlToPdfAsync(htmlDocument, outputPdfPath, inlineImages, cancellationToken),
                    DispatcherPriority.Normal,
                    cancellationToken)
                .Task
                .Unwrap()
                .ConfigureAwait(true);
        }

        if (!IsAvailable)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(true);
        }

        if (!IsAvailable || _webView?.CoreWebView2 is null)
        {
            Trace.TraceWarning("[EmailBodyPdf] Render aborted — renderer not available after init.");
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                "not available after init",
                null,
                runId: "post-fix");
            // #endregion
            return false;
        }

        await _renderLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_webView?.CoreWebView2 is null)
            {
                return false;
            }

            EnsureWebResourceHandler();
            RegisterInlineImages(inlineImages);
            var htmlForNav = RewriteInlineCidSources(htmlDocument, InlineImageHost);

            // #region agent log
            AgentDebugNdjson.Write(
                "H6",
                "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                "before navigate",
                new
                {
                    htmlLen = htmlForNav.Length,
                    inlineRegistered = _inlineImages.Count,
                    hasCidLeft = htmlForNav.Contains("cid:", StringComparison.OrdinalIgnoreCase),
                    hasVirtualHost = htmlForNav.Contains(InlineImageHost, StringComparison.OrdinalIgnoreCase),
                },
                runId: "post-fix");
            // #endregion

            var navigationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
                navigationTcs.TrySetResult(args.IsSuccess);

            _webView.NavigationCompleted += OnNavigationCompleted;
            try
            {
                _webView.NavigateToString(htmlForNav);

                using var navTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, navTimeout.Token);
                var winner = await Task.WhenAny(navigationTcs.Task, Task.Delay(Timeout.Infinite, linked.Token))
                    .ConfigureAwait(true);
                if (winner != navigationTcs.Task || !await navigationTcs.Task.ConfigureAwait(true))
                {
                    // #region agent log
                    AgentDebugNdjson.Write(
                        "H4",
                        "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                        "navigation failed",
                        null,
                        runId: "post-fix");
                    // #endregion
                    return false;
                }

                var readyOk = await WaitForConditionAsync(
                        "document.readyState === 'complete'",
                        TimeSpan.FromSeconds(10),
                        cancellationToken)
                    .ConfigureAwait(true);

                var imageStats = await WaitForImagesAsync(cancellationToken).ConfigureAwait(true);

                await Task.Delay(SettlingDelayMs, cancellationToken).ConfigureAwait(true);

                // #region agent log
                AgentDebugNdjson.Write(
                    "H7",
                    "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                    "pre-print image wait",
                    new
                    {
                        readyOk,
                        imageStats.Total,
                        imageStats.Complete,
                        imageStats.TimedOut,
                    },
                    runId: "post-fix");
                // #endregion

                var outputDir = Path.GetDirectoryName(outputPdfPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var printSettings = _webView.CoreWebView2.Environment.CreatePrintSettings();
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;
                printSettings.MarginTop = 0.4;
                printSettings.MarginBottom = 0.4;
                printSettings.MarginLeft = 0.4;
                printSettings.MarginRight = 0.4;

                var printed = await _webView.CoreWebView2
                    .PrintToPdfAsync(outputPdfPath, printSettings)
                    .ConfigureAwait(true);

                var ok = printed && File.Exists(outputPdfPath);
                // #region agent log
                AgentDebugNdjson.Write(
                    "H4",
                    "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                    ok ? "print ok" : "print failed",
                    new
                    {
                        printed,
                        pathExists = File.Exists(outputPdfPath),
                        htmlLen = htmlForNav.Length,
                        pdfBytes = ok ? new FileInfo(outputPdfPath).Length : 0L,
                        imageStats.Total,
                        imageStats.Complete,
                    },
                    runId: "post-fix");
                // #endregion
                return ok;
            }
            finally
            {
                _webView.NavigationCompleted -= OnNavigationCompleted;
                _inlineImages.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[EmailBodyPdf] Render failed: {ex.GetType().Name}: {ex.Message}");
            // #region agent log
            AgentDebugNdjson.Write(
                "H4",
                "WpfEmailBodyPdfRenderer.RenderHtmlToPdfAsync",
                "exception",
                new { errorType = ex.GetType().Name, error = ex.Message },
                runId: "post-fix");
            // #endregion
            return false;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Cleanup();
        _renderLock.Dispose();
        _initLock.Dispose();
    }

    private void EnsureWebResourceHandler()
    {
        if (_webResourceHandlerAttached || _webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        core.AddWebResourceRequestedFilter($"https://{InlineImageHost}/*", CoreWebView2WebResourceContext.Image);
        core.WebResourceRequested += OnWebResourceRequested;
        _webResourceHandlerAttached = true;
    }

    private void RegisterInlineImages(IReadOnlyList<EmailInlineImage>? images)
    {
        _inlineImages.Clear();
        if (images is null)
        {
            return;
        }

        foreach (var image in images)
        {
            var key = NormalizeContentId(image.ContentId);
            if (!string.IsNullOrWhiteSpace(key) && image.Data is { Length: > 0 })
            {
                _inlineImages[key] = new InlineImageEntry(image.Data, image.ContentType);
            }
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            if (sender is not CoreWebView2 core || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
            {
                return;
            }

            if (!uri.Host.Equals(InlineImageHost, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var key = NormalizeContentId(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
            if (!_inlineImages.TryGetValue(key, out var entry))
            {
                // #region agent log
                AgentDebugNdjson.Write(
                    "H6",
                    "WpfEmailBodyPdfRenderer.OnWebResourceRequested",
                    "cid miss",
                    new { path = uri.AbsolutePath, registered = _inlineImages.Count },
                    runId: "post-fix");
                // #endregion
                return;
            }

            var mime = string.IsNullOrWhiteSpace(entry.ContentType) ? "application/octet-stream" : entry.ContentType;
            var stream = new MemoryStream(entry.Data, writable: false);
            e.Response = core.Environment.CreateWebResourceResponse(
                stream, 200, "OK", $"Content-Type: {mime}\r\nAccess-Control-Allow-Origin: *");
        }
        catch
        {
            // Serving an inline image must never break PDF rendering.
        }
    }

    private static string RewriteInlineCidSources(string html, string host)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        return CidReferenceRegex.Replace(html, match =>
        {
            var cid = NormalizeContentId(match.Groups["cid"].Value);
            var url = $"https://{host}/{Uri.EscapeDataString(cid)}";
            return $"{match.Groups["prefix"].Value}{url}{match.Groups["suffix"].Value}";
        });
    }

    private async Task<bool> WaitForConditionAsync(
        string jsCondition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout && !cancellationToken.IsCancellationRequested)
        {
            var result = await ExecuteScriptSafeAsync(jsCondition).ConfigureAwait(true);
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(true);
        }

        return false;
    }

    private async Task<(int Total, int Complete, bool TimedOut)> WaitForImagesAsync(CancellationToken cancellationToken)
    {
        var totalRaw = await ExecuteScriptSafeAsync("document.images.length").ConfigureAwait(true);
        var total = int.TryParse(totalRaw?.Trim('"'), out var t) ? t : 0;
        if (total <= 0)
        {
            return (0, 0, false);
        }

        var start = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(ImageLoadTimeoutSeconds);
        while (DateTime.UtcNow - start < timeout && !cancellationToken.IsCancellationRequested)
        {
            var completeRaw = await ExecuteScriptSafeAsync(
                    "Array.from(document.images).filter(img => img.complete && img.naturalWidth > 0).length")
                .ConfigureAwait(true);
            var complete = int.TryParse(completeRaw?.Trim('"'), out var c) ? c : 0;
            if (complete >= total)
            {
                return (total, complete, false);
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(true);
        }

        var finalRaw = await ExecuteScriptSafeAsync(
                "Array.from(document.images).filter(img => img.complete && img.naturalWidth > 0).length")
            .ConfigureAwait(true);
        var finalComplete = int.TryParse(finalRaw?.Trim('"'), out var fc) ? fc : 0;
        return (total, finalComplete, finalComplete < total);
    }

    private async Task<string?> ExecuteScriptSafeAsync(string script)
    {
        try
        {
            if (_webView?.CoreWebView2 is null)
            {
                return null;
            }

            return await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeContentId(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('<', '>').Trim();

    private void Cleanup()
    {
        try
        {
            if (_webView?.CoreWebView2 is not null && _webResourceHandlerAttached)
            {
                _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
            }
        }
        catch
        {
            // ignore
        }

        _webResourceHandlerAttached = false;
        _inlineImages.Clear();

        try
        {
            _webView?.Dispose();
        }
        catch
        {
            // ignore
        }

        _webView = null;

        try
        {
            _hiddenWindow?.Close();
        }
        catch
        {
            // ignore
        }

        _hiddenWindow = null;
        _isInitialized = false;
    }

    private readonly record struct InlineImageEntry(byte[] Data, string ContentType);
}
