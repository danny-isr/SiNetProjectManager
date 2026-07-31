namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// SendQuote PDF attach: resolve ניהול_כספי initial directory and file into catalog slot
/// <c>QuoteSendDocument</c> («הצעת_מחיר_לשליחה») only when that slot has no physical file yet.
/// </summary>
public interface IQuoteSendAttachmentService
{
    /// <summary>Stable catalog code for the send-ready quote PDF.</summary>
    public const string CatalogCode = "QuoteSendDocument";

    /// <summary>
    /// Absolute FileServer path of the folder that holds <see cref="CatalogCode"/>
    /// (typically ניהול_כספי), or <see langword="null"/> when unresolved.
    /// </summary>
    Task<string?> ResolveAttachInitialDirectoryAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places <paramref name="sourcePdfPath"/> into the <see cref="CatalogCode"/> slot when empty.
    /// When a physical match already exists for that slot, skips placement.
    /// </summary>
    Task<QuoteSendEnsureFiledResult> EnsureFiledIfNeededAsync(
        int projectId,
        string sourcePdfPath,
        CancellationToken cancellationToken = default);
}

/// <param name="Success">Overall operation succeeded (skip or place).</param>
/// <param name="AlreadyFiled">Slot already had a physical FileServer match.</param>
/// <param name="FiledNow">A new canonical file was placed in this call.</param>
/// <param name="SourcePath">The PDF the user selected.</param>
/// <param name="FiledCanonicalPath">Canonical path when placed or when source already was the filed file.</param>
/// <param name="Error">Failure detail when <see cref="Success"/> is false.</param>
public sealed record QuoteSendEnsureFiledResult(
    bool Success,
    bool AlreadyFiled,
    bool FiledNow,
    string SourcePath,
    string? FiledCanonicalPath,
    string? Error);
