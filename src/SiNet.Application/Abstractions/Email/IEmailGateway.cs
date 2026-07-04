namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Read access to the mailbox. Implemented by <c>SiNet.Infrastructure.Google</c>,
/// or temporarily by <c>SiNet.LegacyBridge</c> over the existing <c>GoogleService</c>.
/// <para>
/// The legacy mailbox is organized per project (Gmail labels under a root), so reads are
/// scoped by location and project name rather than a single global inbox.
/// </para>
/// </summary>
public interface IEmailGateway
{
    /// <summary>
    /// Gets the email summaries filed under a specific project (Gmail label path
    /// <c>{root}/{location}/{projectName}</c>). Returns an empty list when the mailbox is not
    /// available (e.g. not signed in) rather than throwing.
    /// </summary>
    Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
        string location,
        string projectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets email summaries for a project by matching the canonical project-label leaf
    /// (legacy <c>NameAndNumber</c>) under the configured Gmail root across any location bucket.
    /// This supports the first real Email work surface without forcing WPF to understand Gmail
    /// label-scanning rules.
    /// </summary>
    Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
        string projectLabelName,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a single email summary by its message id, or <c>null</c> if not found.</summary>
    Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default);
}
