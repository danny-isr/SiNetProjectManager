namespace SiNet.Application.Abstractions.Email;

/// <summary>One Gmail <c>messages.list</c> page of ids only. Failures are not empty successes.</summary>
public sealed record GmailMessageIdPage(
    IReadOnlyList<string> MessageIds,
    string? NextPageToken,
    bool HasNextPage,
    bool Succeeded,
    string? ErrorMessage,
    string? FailedPageToken = null,
    int Retries = 0,
    int RateLimitHits = 0)
{
    public static GmailMessageIdPage Failed(string errorMessage, string? failedPageToken = null) =>
        new([], null, false, false, errorMessage, failedPageToken);

    public static GmailMessageIdPage Ok(
        IReadOnlyList<string> messageIds,
        string? nextPageToken) =>
        new(
            messageIds,
            nextPageToken,
            !string.IsNullOrEmpty(nextPageToken),
            true,
            null);
}
