namespace SiNet.Application.Abstractions.Email;

/// <summary>One paged slice of mailbox summaries from Gmail.</summary>
public sealed record EmailMailboxPage(
    IReadOnlyList<EmailSummary> Items,
    int PageSize,
    string? NextPageToken,
    bool HasNextPage);
