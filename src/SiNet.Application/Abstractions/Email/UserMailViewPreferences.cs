namespace SiNet.Application.Abstractions.Email;

/// <summary>Per-user Gmail mailbox list view preferences (persisted in <c>UserSetting</c>).</summary>
public sealed record UserMailViewPreferences(
    EmailMailboxScope Scope,
    EmailMailboxCategory Category,
    bool UnreadOnly)
{
    public static UserMailViewPreferences Default { get; } =
        new(EmailMailboxScope.Inbox, EmailMailboxCategory.All, UnreadOnly: false);
}
