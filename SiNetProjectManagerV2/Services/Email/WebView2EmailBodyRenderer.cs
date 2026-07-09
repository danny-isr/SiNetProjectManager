using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using SiNet.Application.Email.Detail;

namespace SiNetProjectManagerV2.Services.Email;

/// <summary>
/// V2 host adapter: renders email body HTML in WebView2 inside the Detail viewer host element.
/// </summary>
internal sealed class WebView2EmailBodyRenderer : IEmailBodyRenderer
{
    private WebView2? _webView;
    private ContentControl? _host;

    public bool IsAvailable => true;

    public void AttachHost(object hostElement)
    {
        if (hostElement is not ContentControl host)
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
    }

    public async Task LoadAsync(EmailBodyRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_webView is null || _host is null)
        {
            return;
        }

        await EnsureInitializedAsync().ConfigureAwait(true);

        var html = BuildHtmlDocument(request.HtmlBody, request.BodyText);
        _webView.NavigateToString(html);
    }

    public void Clear()
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webView.NavigateToString("<html><body></body></html>");
        }

        if (_host is not null)
        {
            _host.Content = null;
            _host.Visibility = Visibility.Collapsed;
        }
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

        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset=\"utf-8\"/>");
        sb.Append("<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;padding:12px;}</style>");
        sb.Append("</head><body dir=\"auto\">");
        sb.Append(encoded);
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
