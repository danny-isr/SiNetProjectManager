namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// One embedded (inline) image referenced from the HTML body via <c>&lt;img src="cid:CONTENT-ID"&gt;</c>.
/// Carries the raw bytes so the host renderer can serve it (virtual-host / WebResourceRequested)
/// instead of inlining Base64 data-URIs, which can crash WebView2 with large images.
/// </summary>
public sealed record EmailInlineImage(
    string ContentId,
    string ContentType,
    byte[] Data);
