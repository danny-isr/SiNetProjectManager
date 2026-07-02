using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class CurrentUserProfileDisplayTests
{
    [Fact]
    public void Format_prefers_display_name()
    {
        var profile = new CurrentUserProfileDto(
            UserId: 7,
            DisplayName: "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC",
            LoginName: "DOMAIN\\danny",
            Role: AppRole.Employee,
            IsActive: true);

        Assert.Equal("\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", CurrentUserProfileDisplay.Format(profile));
    }

    [Fact]
    public void Format_falls_back_to_login_name()
    {
        var profile = new CurrentUserProfileDto(
            UserId: 7,
            DisplayName: "   ",
            LoginName: "DOMAIN\\danny",
            Role: AppRole.Management,
            IsActive: true);

        Assert.Equal("DOMAIN\\danny", CurrentUserProfileDisplay.Format(profile));
    }

    [Fact]
    public void Format_falls_back_to_user_id()
    {
        var profile = new CurrentUserProfileDto(
            UserId: 42,
            DisplayName: "",
            LoginName: null,
            Role: AppRole.Administrator,
            IsActive: true);

        Assert.Equal("\u05DE\u05E9\u05EA\u05DE\u05E9 #42", CurrentUserProfileDisplay.Format(profile));
    }

    [Fact]
    public void Format_returns_null_for_missing_profile()
    {
        Assert.Null(CurrentUserProfileDisplay.Format(null));
    }
}
