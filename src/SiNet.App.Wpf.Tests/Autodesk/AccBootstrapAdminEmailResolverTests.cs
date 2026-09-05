using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AccBootstrapAdminEmailResolverTests
{
    [Fact]
    public void ResolveForInboxProjectAdmin_WhenSettingsPresent_AndNoRequest_UsesSettings()
    {
        var email = AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(
            "siad@si-eng.co.il",
            requestAdminEmail: null);

        Assert.Equal("siad@si-eng.co.il", email);
    }

    [Fact]
    public void ResolveForInboxProjectAdmin_WhenRequestMatchesConfigured_Accepts()
    {
        var email = AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(
            "siad@si-eng.co.il",
            requestAdminEmail: "siad@si-eng.co.il");

        Assert.Equal("siad@si-eng.co.il", email);
    }

    [Fact]
    public void ResolveForInboxProjectAdmin_WhenRequestDiffersFromConfigured_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(
                "siad@si-eng.co.il",
                requestAdminEmail: "other@si-eng.co.il"));
    }

    [Fact]
    public void ResolveForInboxProjectAdmin_WhenConfiguredMissing_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccBootstrapAdminEmailResolver.ResolveForInboxProjectAdmin(null, null));
    }
}
