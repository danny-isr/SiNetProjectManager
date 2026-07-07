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

    /// <summary>
    /// Gets the full read-only details for one email message: summary headers, best-effort plain-text
    /// body, and attachment metadata. Returns <c>null</c> when the message cannot be loaded.
    /// </summary>
    Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one page of mailbox messages (default INBOX scope, legacy <c>label:INBOX</c>).
    /// Returns a single Gmail page — does not drain all results.
    /// </summary>
    Task<EmailMailboxPage> GetMailboxPageAsync(
        EmailMailboxQuery query,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists SiNet-relevant Gmail labels for filter dropdowns (read-only).</summary>
    Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unread count for the mailbox scope in <paramref name="query"/>.
    /// Uses a separate Gmail query from paged list fetch — not derived from the current page.
    /// </summary>
    Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
        EmailMailboxQuery query,
        CancellationToken cancellationToken = default);
}
