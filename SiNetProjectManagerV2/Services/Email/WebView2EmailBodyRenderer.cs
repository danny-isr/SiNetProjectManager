using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email.Detail;

namespace SiNetProjectManagerV2.Services.Email;

/// <summary>
/// V2 host adapter: renders email body HTML in WebView2 inside the Detail viewer host element.
/// Registered as Transient so each email surface gets its own WebView2 instance (no reparent across hosts).
/// </summary>
internal sealed class WebView2EmailBodyRenderer : IEmailBodyRenderer
{
    private WebView2? _webView;
    private ContentControl? _host;
    private EmailBodyRenderRequest? _pendingRequest;

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
            return false;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);

            var html = BuildHtmlDocument(request.HtmlBody, request.BodyText);
            _webView.NavigateToString(html);
            // Clear may have set a local Collapsed; restore so the XAML DataTrigger can show the host.
            _host.Visibility = Visibility.Visible;

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Email.BodyRender",
                $"NavigateToString ok gmailId={request.GmailMessageId ?? "(none)"} htmlLen={html.Length} coreReady={_webView.CoreWebView2 is not null}");
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

    private static string BuildHtmlDocument(string? htmlBody, string bodyText)
    {
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            return htmlBody.Contains("<html", StringComparison.OrdinalIgnoreCase)
                ? htmlBody
                : $"<html><head><meta charset=\"utf-8\"/></head><body dir=\"auto\">{htmlBody}</body></html>";
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
}
