using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG + agent debug
using SiNet.Application.Email.Detail;

namespace SiNetProjectManagerV2.Services.Email;

/// <summary>
/// V2 host adapter: renders email body HTML in WebView2 inside the Detail viewer host element.
/// Registered as Transient so each email surface gets its own WebView2 instance (no reparent across hosts).
/// <para>
/// Embedded images (<c>&lt;img src="cid:..."&gt;</c>) are served via a virtual host + WebResourceRequested,
/// NOT inlined as Base64 data-URIs (which can crash WebView2 with large images and hit the
/// <c>NavigateToString</c> size limit).
/// </para>
/// </summary>
internal sealed class WebView2EmailBodyRenderer : IEmailBodyRenderer
{
    private const string InlineImageHost = "sinet-mail-images.local";

    // Matches src="cid:CONTENT-ID" (single/double quotes) in the HTML body.
    private static readonly Regex CidReferenceRegex = new(
        "(?<prefix>src\\s*=\\s*[\"'])cid:(?<cid>[^\"']+)(?<suffix>[\"'])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, InlineImageEntry> _inlineImages = new(StringComparer.OrdinalIgnoreCase);

    private WebView2? _webView;
    private ContentControl? _host;
    private EmailBodyRenderRequest? _pendingRequest;
    private bool _webResourceHandlerAttached;

    public bool IsAvailable => true;

    public void AttachHost(object hostElement)
    {
        if (hostElement is not ContentControl host)
        {
            return;
        }

        // Already attached to this host — keep the existing WebView2.
        if (ReferenceEquals(_host, host) && _webView is not null && ReferenceEquals(host.Content, _webView))
        {
            return;
        }

        _host = host;
        if (_webView is null)
        {
            _webView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.White,
            };
        }

        host.Content = _webView;
        host.Visibility = Visibility.Visible;

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step(
            "Email.BodyRender",
            $"AttachHost hostHash={host.GetHashCode()} webViewHash={_webView.GetHashCode()} pending={_pendingRequest is not null}");

        if (_pendingRequest is not null)
        {
            var pending = _pendingRequest;
            _pendingRequest = null;
            _ = LoadAsync(pending, CancellationToken.None);
        }
    }

    public async Task<bool> LoadAsync(EmailBodyRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_webView is null || _host is null)
        {
            _pendingRequest = request;
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.BodyRender",
                $"LoadAsync deferred (no host) gmailId={request.GmailMessageId ?? "(none)"} htmlLen={request.HtmlBody?.Length ?? 0}");
            // #region agent log
            AgentDebugNdjson.Write("D", "WebView2EmailBodyRenderer.LoadAsync", "deferred-no-host",
                new
                {
                    gmailId = request.GmailMessageId,
                    requestInlineCount = request.InlineImages.Count,
                    hasWebView = _webView is not null,
                    hasHost = _host is not null,
                });
            // #endregion
            return false;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            EnsureWebResourceHandler();

            RegisterInlineImages(request.InlineImages);
            var html = BuildHtmlDocument(request.HtmlBody, request.BodyText);
            var stillHasCid = html.Contains("cid:", StringComparison.OrdinalIgnoreCase);
            var rewrittenCount = System.Text.RegularExpressions.Regex.Matches(
                html, InlineImageHost, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

            // #region agent log
            AgentDebugNdjson.Write("D", "WebView2EmailBodyRenderer.LoadAsync", "before-navigate",
                new
                {
                    gmailId = request.GmailMessageId,
                    requestInlineCount = request.InlineImages.Count,
                    registeredCount = _inlineImages.Count,
                    htmlLen = html.Length,
                    stillHasCid,
                    rewrittenHostHits = rewrittenCount,
                    handlerAttached = _webResourceHandlerAttached,
                    coreReady = _webView.CoreWebView2 is not null,
                });
            // #endregion

            _webView.NavigateToString(html);
            // Clear may have set a local Collapsed; restore so the XAML DataTrigger can show the host.
            _host.Visibility = Visibility.Visible;

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.BodyRender",
                $"NavigateToString ok gmailId={request.GmailMessageId ?? "(none)"} htmlLen={html.Length} inlineImages={request.InlineImages.Count} coreReady={_webView.CoreWebView2 is not null}");
            return true;
        }
        catch (Exception ex)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.BodyRender",
                $"LoadAsync FAILED: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void Clear()
    {
        _pendingRequest = null;
        _inlineImages.Clear();

        if (_webView?.CoreWebView2 is not null)
        {
            try
            {
                _webView.NavigateToString("<html><body></body></html>");
            }
            catch
            {
                // ignore clear navigation failures
            }
        }

        // Keep WebView2 attached to this host (Transient: one renderer per surface).
        // Only hide — do not Dispose here; Dispose would force a cold CoreWebView2 init on next load.
        if (_host is not null)
        {
            _host.Visibility = Visibility.Collapsed;
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.BodyRender", "Clear content (WebView2 kept for surface)");
    }

    private async Task EnsureInitializedAsync()
    {
        if (_webView?.CoreWebView2 is not null)
        {
            return;
        }

        if (_webView is null)
        {
            return;
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(async () => await _webView.EnsureCoreWebView2Async().ConfigureAwait(true))
                .Task.ConfigureAwait(true);
            return;
        }

        await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
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

    private void RegisterInlineImages(IReadOnlyList<EmailInlineImage> images)
    {
        _inlineImages.Clear();
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
            var found = _inlineImages.TryGetValue(key, out var entry);

            // #region agent log
            AgentDebugNdjson.Write("E", "WebView2EmailBodyRenderer.OnWebResourceRequested", "request",
                new
                {
                    uri = e.Request.Uri,
                    key,
                    found,
                    cacheCount = _inlineImages.Count,
                    cacheKeys = _inlineImages.Keys.Take(5).ToArray(),
                });
            // #endregion

            if (!found)
            {
                return;
            }

            var mime = string.IsNullOrWhiteSpace(entry.ContentType) ? "application/octet-stream" : entry.ContentType;
            var stream = new MemoryStream(entry.Data, writable: false);
            e.Response = core.Environment.CreateWebResourceResponse(
                stream, 200, "OK", $"Content-Type: {mime}\r\nAccess-Control-Allow-Origin: *");
        }
        catch
        {
            // Serving an inline image must never break body rendering.
        }
    }

    private string BuildHtmlDocument(string? htmlBody, string bodyText)
    {
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            var rewritten = RewriteInlineCidSources(htmlBody, InlineImageHost);
            return rewritten.Contains("<html", StringComparison.OrdinalIgnoreCase)
                ? rewritten
                : $"<html><head><meta charset=\"utf-8\"/></head><body dir=\"auto\">{rewritten}</body></html>";
        }

        var encoded = System.Net.WebUtility.HtmlEncode(bodyText ?? string.Empty)
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);

        var sb = new System.Text.StringBuilder();
        sb.Append("<html><head><meta charset=\"utf-8\"/>");
        sb.Append("<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;padding:12px;}</style>");
        sb.Append("</head><body dir=\"auto\">");
        sb.Append(encoded);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Rewrites <c>src="cid:CONTENT-ID"</c> references to <c>https://{host}/{escaped content-id}</c>
    /// so the WebResourceRequested handler can serve the bytes. Non-cid sources are untouched.
    /// </summary>
    internal static string RewriteInlineCidSources(string html, string host)
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

    private static string NormalizeContentId(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('<', '>').Trim();

    private readonly record struct InlineImageEntry(byte[] Data, string ContentType);
}
