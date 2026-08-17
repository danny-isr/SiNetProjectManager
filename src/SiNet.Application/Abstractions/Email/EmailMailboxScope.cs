namespace SiNet.Application.Abstractions.Email;

/// <summary>Gmail mailbox list scope for <see cref="EmailMailboxQuery"/>.</summary>
public enum EmailMailboxScope
{
    /// <summary>Inbox label — <c>label:INBOX</c> (Category is applied separately).</summary>
    Inbox,

    /// <summary>All mail excluding spam, trash, sent, drafts.</summary>
    AllMail,

    /// <summary>
    /// Deprecated compatibility: maps to <see cref="Inbox"/> + <c>UnreadOnly=true</c>.
    /// Removed from UI; do not use in new code.
    /// </summary>
    [Obsolete("Use EmailMailboxScope.Inbox with EmailMailboxQuery.UnreadOnly=true instead.")]
    Unread,

    /// <summary>Explicit Gmail label selected via <see cref="EmailMailboxQuery.LabelName"/>.</summary>
    Label,

    /// <summary>Sent mail — <c>in:sent</c>.</summary>
    Sent,
}
