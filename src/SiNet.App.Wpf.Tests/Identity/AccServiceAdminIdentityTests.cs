using SiNet.Application.Identity;
using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class AccServiceAdminIdentityTests
{
    [Fact]
    public void Match_with_admin_api_200_is_Healthy()
    {
        var check = AccServiceAdminIdentity.Evaluate(
            "SIAD@si-eng.co.il",
            "siad@si-eng.co.il",
            adminApiStatus: "200");
        Assert.Equal(AccServiceAdminIdentityStatus.Healthy, check.Status);
        Assert.True(check.EmailMatch);
        Assert.Equal("200", check.AdminApiStatus);
        Assert.Null(check.WarningMessage);
        Assert.False(AccServiceAdminIdentity.IsKnownWrongAdmin(check));
        Assert.False(AccServiceAdminIdentity.ShouldBlockAdminMutation(check));
    }

    [Fact]
    public void Match_without_admin_api_probe_is_ServiceUnavailable_not_Healthy()
    {
        var check = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "siad@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.ServiceUnavailable, check.Status);
        Assert.True(check.EmailMatch);
    }

    [Fact]
    public void AdminEmailMismatch_lists_expected_and_connected()
    {
        var check = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "danny@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.AdminEmailMismatch, check.Status);
        Assert.False(check.EmailMatch);
        Assert.True(AccServiceAdminIdentity.IsKnownWrongAdmin(check));
        Assert.True(AccServiceAdminIdentity.ShouldBlockAdminMutation(check));
        Assert.Contains("siad@si-eng.co.il", check.OperatorMessageHe);
        Assert.Contains("danny@si-eng.co.il", check.OperatorMessageHe);
    }

    [Fact]
    public void Empty_expected_falls_back_to_default_siad()
    {
        var check = AccServiceAdminIdentity.Evaluate(null, "siad@si-eng.co.il", adminApiStatus: "200");
        Assert.Equal(AccServiceAdminIdentityStatus.Healthy, check.Status);
        Assert.Equal(SystemSettingsDefaults.AccBootstrapAdminEmail, check.ExpectedAdminEmail);
    }

    [Fact]
    public void TokenMissing_blocks_mutation()
    {
        var check = AccServiceAdminIdentity.Evaluate(
            "siad@si-eng.co.il",
            actualAdminEmail: null,
            tokenAvailable: false,
            profileResolved: false);
        Assert.Equal(AccServiceAdminIdentityStatus.TokenMissing, check.Status);
        Assert.True(AccServiceAdminIdentity.ShouldBlockAdminMutation(check));
    }

    [Fact]
    public void ProfileUnavailable_blocks_mutation()
    {
        var check = AccServiceAdminIdentity.Evaluate(
            "siad@si-eng.co.il",
            actualAdminEmail: null,
            tokenAvailable: true,
            profileResolved: false);
        Assert.Equal(AccServiceAdminIdentityStatus.ProfileUnavailable, check.Status);
        Assert.True(AccServiceAdminIdentity.ShouldBlockAdminMutation(check));
    }

    [Fact]
    public void Identity_match_with_admin_api_403_is_AdminApiUnauthorized()
    {
        var identity = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "siad@si-eng.co.il");
        var withApi = AccServiceAdminIdentity.WithAdminApiStatus(identity, "403");
        Assert.Equal(AccServiceAdminIdentityStatus.AdminApiUnauthorized, withApi.Status);
        Assert.True(withApi.EmailMatch);
        Assert.False(AccServiceAdminIdentity.ShouldBlockAdminMutation(withApi));
    }

    [Fact]
    public void AccService_admin_may_differ_from_operator_SIUser_email()
    {
        var check = AccServiceAdminIdentity.Evaluate(
            "siad@si-eng.co.il",
            "siad@si-eng.co.il",
            adminApiStatus: "200");
        Assert.Equal(AccServiceAdminIdentityStatus.Healthy, check.Status);
        Assert.NotEqual("shirly@si-eng.co.il", check.ExpectedAdminEmail);
    }
}
