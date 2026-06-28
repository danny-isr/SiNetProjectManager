namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// A single outbound attachment. UI-agnostic: holds the raw bytes plus the metadata Gmail needs to
/// build the MIME part. No WPF / file-dialog types.
/// </summary>
/// <param name="FileName">Display file name (e.g. <c>quote.pdf</c>).</param>
/// <param name="ContentType">MIME content type (e.g. <c>application/pdf</c>). When null/empty the
/// sender falls back to <c>application/octet-stream</c>.</param>
/// <param name="Content">The attachment payload.</param>
public sealed record EmailAttachment(
    string FileName,
    string? ContentType,
    ReadOnlyMemory<byte> Content);
