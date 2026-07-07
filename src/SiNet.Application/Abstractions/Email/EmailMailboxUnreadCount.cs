namespace SiNet.Application.Abstractions.Email;

/// <summary>Unread message count for a mailbox scope (separate from paged list results).</summary>
public sealed record EmailMailboxUnreadCount(
    int Count,
    bool IsExact,
    string? ScopeDescription = null);
