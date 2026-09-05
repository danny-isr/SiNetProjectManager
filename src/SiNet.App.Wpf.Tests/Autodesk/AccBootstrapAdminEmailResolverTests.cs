using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AccBootstrapAdminEmailResolverTests
{
    [Fact]
    public void ResolveForInboxProjectAdmin_WhenSettingsPresent_UsesSettings()
    {
        var email = AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(
            "siad@si-eng.co.il",
            requestOverride: null);

        Assert.Equal("siad@si-eng.co.il", email);
    }

    [Fact]
    public void ResolveForInboxProjectAdmin_WhenRequestOverridePresent_PrefersOverride()
    {
        var email = AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(
            "siad@si-eng.co.il",
            requestOverride: "other@si-eng.co.il");

        Assert.Equal("other@si-eng.co.il", email);
    }

    [Fact]
    public void ResolveForInboxProjectAdmin_WhenNeitherPresent_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(null, null));
    }
}
