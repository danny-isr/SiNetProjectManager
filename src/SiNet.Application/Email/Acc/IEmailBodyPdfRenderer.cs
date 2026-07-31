using SiNet.Application.Abstractions.Email;

namespace SiNet.Application.Email.Acc;

/// <summary>
/// Renders email HTML to a local PDF file for ACC Inbox <c>00_Email.pdf</c> upload.
/// Distinct from <c>IEmailBodyRenderer</c> (UI display). Best-effort — callers must tolerate failure.
/// </summary>
public interface IEmailBodyPdfRenderer
{
    /// <summary>True when the hidden WebView2 (or equivalent) engine is ready.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Renders <paramref name="htmlDocument"/> to <paramref name="outputPdfPath"/>.
    /// Must be safe to call off the UI thread (implementation marshals as needed).
    /// <paramref name="inlineImages"/> are served via virtual host (CID → https) — not Base64 data-URIs.
    /// </summary>
    Task<bool> RenderHtmlToPdfAsync(
        string htmlDocument,
        string outputPdfPath,
        IReadOnlyList<EmailInlineImage>? inlineImages = null,
        CancellationToken cancellationToken = default);
}
