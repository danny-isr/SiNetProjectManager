using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class AccServiceAdminIdentityTests
{
    [Fact]
    public void Match_when_expected_equals_connected()
    {
        var check = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "SIAD@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.Match, check.Status);
        Assert.Null(check.WarningMessage);
        Assert.False(AccServiceAdminIdentity.IsKnownWrongAdmin(check));
    }

    [Fact]
    public void Mismatch_warning_lists_expected_and_connected()
    {
        var check = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "danny@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.Mismatch, check.Status);
        Assert.True(AccServiceAdminIdentity.IsKnownWrongAdmin(check));
        Assert.Contains("Expected: siad@si-eng.co.il", check.WarningMessage);
        Assert.Contains("Connected: danny@si-eng.co.il", check.WarningMessage);
    }

    [Fact]
    public void Empty_expected_falls_back_to_default_siad()
    {
        var check = AccServiceAdminIdentity.Evaluate(null, "siad@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.Match, check.Status);
        Assert.Equal("siad@si-eng.co.il", check.ExpectedAdminEmail);
    }
}
