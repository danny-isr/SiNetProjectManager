namespace SiNet.Application.Abstractions.Email;

/// <summary>One page of Gmail <c>users.history.list</c> (caller must walk <see cref="NextPageToken"/>).</summary>
public sealed record GmailHistoryListPage(
    ulong HistoryId,
    bool HasMessagesAdded,
    string? NextPageToken);

/// <summary>Low-level Gmail History/Profile access for mailbox change detection.</summary>
public interface IGmailHistoryApi
{
    Task<ulong?> GetProfileHistoryIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one history page. Throws <see cref="GmailHistoryExpiredException"/> on HTTP 404.
    /// Other failures throw; caller treats as transient.
    /// </summary>
    Task<GmailHistoryListPage> ListHistoryPageAsync(
        ulong startHistoryId,
        string? labelId,
        IReadOnlyList<string> historyTypes,
        string? pageToken,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when Gmail reports the startHistoryId is no longer valid (HTTP 404).</summary>
public sealed class GmailHistoryExpiredException : InvalidOperationException
{
    public GmailHistoryExpiredException(string message) : base(message)
    {
    }

    public GmailHistoryExpiredException(string message, Exception inner) : base(message, inner)
    {
    }
}
