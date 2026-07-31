namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// SendQuote PDF attach: resolve ניהול_כספי initial directory and ensure the email attachment
/// is a filed <c>QuoteSendDocument</c> («הצעה_לשליחה») copy.
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
    /// Ensures <paramref name="sourcePdfPath"/> becomes (or already is) a physical
    /// <see cref="CatalogCode"/> file. When a send document already exists and the selected file
    /// is not it, returns <see cref="QuoteSendEnsureFiledResult.RequiresNewAlternative"/> unless
    /// <paramref name="alternativeName"/> is supplied for a new unused alternative.
    /// Never renames the original source file.
    /// </summary>
    Task<QuoteSendEnsureFiledResult> EnsureFiledIfNeededAsync(
        int projectId,
        string sourcePdfPath,
        string? alternativeName = null,
        CancellationToken cancellationToken = default);
}

/// <param name="Success">Overall operation succeeded (skip or place).</param>
/// <param name="AlreadyFiled">Selected file was already a QuoteSendDocument physical match.</param>
/// <param name="FiledNow">A new canonical copy was placed in this call.</param>
/// <param name="RequiresNewAlternative">
/// Existing send document(s) found and selected file is different — caller must prompt for a new alternative.
/// </param>
/// <param name="ExistingAlternatives">Alternative labels already used for this catalog slot.</param>
/// <param name="SuggestedAlternative">Suggested unused alternative label (e.g. <c>"2"</c>).</param>
/// <param name="SourcePath">The PDF the user selected (unchanged on disk).</param>
/// <param name="FiledCanonicalPath">Path of the filed QuoteSendDocument to attach/open.</param>
/// <param name="Error">Failure detail when <see cref="Success"/> is false.</param>
public sealed record QuoteSendEnsureFiledResult(
    bool Success,
    bool AlreadyFiled,
    bool FiledNow,
    bool RequiresNewAlternative,
    IReadOnlyList<string> ExistingAlternatives,
    string? SuggestedAlternative,
    string SourcePath,
    string? FiledCanonicalPath,
    string? Error);
