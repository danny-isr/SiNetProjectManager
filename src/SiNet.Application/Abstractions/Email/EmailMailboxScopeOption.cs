namespace SiNet.Application.Abstractions.Email;

/// <summary>Display option for mailbox scope filter dropdowns.</summary>
public sealed record EmailMailboxScopeOption(EmailMailboxScope Scope, string DisplayName);
