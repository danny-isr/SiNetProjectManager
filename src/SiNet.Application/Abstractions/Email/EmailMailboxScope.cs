namespace SiNet.Application.Abstractions.Email;

/// <summary>Gmail mailbox list scope for <see cref="EmailMailboxQuery"/>.</summary>
public enum EmailMailboxScope
{
    /// <summary>Primary Inbox tab — <c>label:INBOX category:primary</c>.</summary>
    Inbox,

    /// <summary>All mail excluding spam, trash, sent, drafts.</summary>
    AllMail,

    /// <summary>Unread messages in Primary Inbox.</summary>
    Unread,

    /// <summary>Explicit Gmail label selected via <see cref="EmailMailboxQuery.LabelName"/>.</summary>
    Label,

    /// <summary>Sent mail — <c>in:sent</c>.</summary>
    Sent,
}
