namespace SiNet.Application.Email;

/// <summary>
/// Read-only enrichment for mailbox rows: project link state from SQL inbox/thread tables.
/// </summary>
public interface IEmailThreadLinkQueryService
{
    /// <summary>
    /// Returns link state keyed by RFC Message-ID / <see cref="EmailInboxMessageDto.InternetMessageId"/>
    /// (case-insensitive, angle brackets ignored).
    /// </summary>
    Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
        IReadOnlyList<string> internetMessageIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns thread-level project mapping keyed by Gmail thread id (adapter mirror).
    /// Works even when the current message has no <c>EmailInboxMessage</c> row yet.
    /// </summary>
    Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByGmailThreadIdsAsync(
        IReadOnlyList<string> gmailThreadIds,
        CancellationToken cancellationToken = default);
}
