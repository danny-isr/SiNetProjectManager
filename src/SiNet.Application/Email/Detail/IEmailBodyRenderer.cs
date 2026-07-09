namespace SiNet.Application.Email.Detail;

/// <summary>
/// Host-provided email body renderer (WebView2 in production V2 host).
/// When unavailable, the viewer pane falls back to plain text.
/// </summary>
public interface IEmailBodyRenderer
{
    bool IsAvailable { get; }

    /// <summary>Attach renderer to a WPF host element (e.g. <c>ContentControl</c> or panel).</summary>
    void AttachHost(object hostElement);

    Task LoadAsync(EmailBodyRenderRequest request, CancellationToken cancellationToken = default);

    void Clear();
}

public sealed record EmailBodyRenderRequest(
    string BodyText,
    string? HtmlBody,
    string? GmailMessageId);
