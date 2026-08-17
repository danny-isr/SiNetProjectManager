namespace SiNet.Application.Abstractions.Email;

/// <summary>Gmail category tab filter for mailbox list queries (independent of Scope).</summary>
public enum EmailMailboxCategory
{
    /// <summary>No category clause — full Scope contents.</summary>
    All = 0,

    Primary,
    Updates,
    Promotions,
    Social,
    Forums,
}
