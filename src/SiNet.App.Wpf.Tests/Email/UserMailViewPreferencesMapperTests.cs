using SiNet.Application.Abstractions.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class UserMailViewPreferencesMapperTests
{
    [Fact]
    public void FromStored_nulls_yield_defaults()
    {
        var prefs = UserMailViewPreferencesMapper.FromStored(null, null, unreadOnly: false);

        Assert.Equal(EmailMailboxScope.Inbox, prefs.Scope);
        Assert.Equal(EmailMailboxCategory.All, prefs.Category);
        Assert.False(prefs.UnreadOnly);
    }

    [Fact]
    public void FromStored_deprecated_unread_scope_maps_to_inbox_unread()
    {
        var prefs = UserMailViewPreferencesMapper.FromStored("Unread", "Primary", unreadOnly: false);

        Assert.Equal(EmailMailboxScope.Inbox, prefs.Scope);
        Assert.True(prefs.UnreadOnly);
        Assert.Equal(EmailMailboxCategory.Primary, prefs.Category);
    }

    [Fact]
    public void ToStored_normalizes_unread_scope()
    {
#pragma warning disable CS0618
        var stored = UserMailViewPreferencesMapper.ToStored(
            new UserMailViewPreferences(EmailMailboxScope.Unread, EmailMailboxCategory.All, UnreadOnly: false));
#pragma warning restore CS0618

        Assert.Equal(nameof(EmailMailboxScope.Inbox), stored.Scope);
        Assert.True(stored.UnreadOnly);
        Assert.Equal(nameof(EmailMailboxCategory.All), stored.Category);
    }
}
