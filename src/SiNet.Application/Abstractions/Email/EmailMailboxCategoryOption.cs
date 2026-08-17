namespace SiNet.Application.Abstractions.Email;

/// <summary>Display option for mailbox category filter dropdowns.</summary>
public sealed record EmailMailboxCategoryOption(EmailMailboxCategory Category, string DisplayName);
