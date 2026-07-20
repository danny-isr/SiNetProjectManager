using SiNet.Application.Abstractions.Email;

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

    /// <returns>True when HTML was rendered into the host; false when host is not ready.</returns>
    Task<bool> LoadAsync(EmailBodyRenderRequest request, CancellationToken cancellationToken = default);

    void Clear();
}

public sealed record EmailBodyRenderRequest(
    string BodyText,
    string? HtmlBody,
    string? GmailMessageId,
    IReadOnlyList<EmailInlineImage>? InlineImages = null)
{
    /// <summary>Embedded images referenced from <see cref="HtmlBody"/> via <c>cid:</c>. Never null.</summary>
    public IReadOnlyList<EmailInlineImage> InlineImages { get; init; } = InlineImages ?? [];
}
