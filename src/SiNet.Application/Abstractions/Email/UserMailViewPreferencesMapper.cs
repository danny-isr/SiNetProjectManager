namespace SiNet.Application.Abstractions.Email;

/// <summary>Maps persisted <c>UserSetting</c> Gmail view columns ↔ <see cref="UserMailViewPreferences"/>.</summary>
public static class UserMailViewPreferencesMapper
{
    public static UserMailViewPreferences FromStored(string? scope, string? category, bool unreadOnly)
    {
        var parsedScope = ParseScope(scope);
        var parsedCategory = ParseCategory(category);
        var unread = unreadOnly;

#pragma warning disable CS0618
        if (parsedScope == EmailMailboxScope.Unread)
        {
            parsedScope = EmailMailboxScope.Inbox;
            unread = true;
        }
#pragma warning restore CS0618

        if (parsedScope is not (EmailMailboxScope.Inbox or EmailMailboxScope.AllMail))
            parsedScope = EmailMailboxScope.Inbox;

        return new UserMailViewPreferences(parsedScope, parsedCategory, unread);
    }

    public static (string Scope, string Category, bool UnreadOnly) ToStored(UserMailViewPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = EmailMailboxQueryComposer.Normalize(new EmailMailboxQuery
        {
            MailboxScope = preferences.Scope,
            Category = preferences.Category,
            UnreadOnly = preferences.UnreadOnly,
        });

        var scope = normalized.MailboxScope == EmailMailboxScope.AllMail
            ? nameof(EmailMailboxScope.AllMail)
            : nameof(EmailMailboxScope.Inbox);

        return (scope, normalized.Category.ToString(), normalized.UnreadOnly);
    }

    private static EmailMailboxScope ParseScope(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return EmailMailboxScope.Inbox;

        return Enum.TryParse<EmailMailboxScope>(value.Trim(), ignoreCase: true, out var scope)
            ? scope
            : EmailMailboxScope.Inbox;
    }

    private static EmailMailboxCategory ParseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return EmailMailboxCategory.All;

        return Enum.TryParse<EmailMailboxCategory>(value.Trim(), ignoreCase: true, out var category)
            ? category
            : EmailMailboxCategory.All;
    }
}
