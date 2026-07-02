using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class AppFeatureAuthorizationTests
{
    [Fact]
    public void Employee_passes_employee_feature()
    {
        Assert.True(AppFeatureAuthorization.CanAccessFeature(
            AppRole.Employee,
            AppFeatureCodes.ShellOpenEmailSurface));
    }

    [Fact]
    public void Employee_denied_management_and_admin_features()
    {
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Employee, AppFeatureCodes.ProjectCreate));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Employee, AppFeatureCodes.ReportsManagement));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Employee, AppFeatureCodes.SystemSettingsWrite));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Employee, AppFeatureCodes.UsersManage));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Employee, AppFeatureCodes.ActionPermissionsManage));
    }

    [Fact]
    public void Management_passes_employee_and_management_features()
    {
        Assert.True(AppFeatureAuthorization.CanAccessFeature(AppRole.Management, AppFeatureCodes.ShellOpenInspectionSurface));
        Assert.True(AppFeatureAuthorization.CanAccessFeature(AppRole.Management, AppFeatureCodes.ProjectCreate));
        Assert.True(AppFeatureAuthorization.CanAccessFeature(AppRole.Management, AppFeatureCodes.ReportsManagement));
    }

    [Fact]
    public void Management_denied_administrator_features()
    {
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Management, AppFeatureCodes.SystemSettingsWrite));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Management, AppFeatureCodes.UsersManage));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Management, AppFeatureCodes.ActionPermissionsManage));
    }

    [Fact]
    public void Administrator_passes_all_registered_features()
    {
        foreach (var code in new[]
                 {
                     AppFeatureCodes.ShellOpenEmailSurface,
                     AppFeatureCodes.ShellOpenInspectionSurface,
                     AppFeatureCodes.ProjectCreate,
                     AppFeatureCodes.ReportsManagement,
                     AppFeatureCodes.SystemSettingsWrite,
                     AppFeatureCodes.UsersManage,
                     AppFeatureCodes.ActionPermissionsManage,
                 })
        {
            Assert.True(AppFeatureAuthorization.CanAccessFeature(AppRole.Administrator, code));
        }
    }

    [Fact]
    public void Unauthorized_denied_all_features()
    {
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Unauthorized, AppFeatureCodes.ShellOpenEmailSurface));
        Assert.False(AppFeatureAuthorization.CanAccessFeature(AppRole.Unauthorized, AppFeatureCodes.SystemSettingsWrite));
    }

    [Fact]
    public void Unknown_feature_code_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            AppFeatureAuthorization.GetRequiredRole("Not.A.Real.Feature"));

        Assert.Contains("Unknown feature code", ex.Message);
    }

    [Theory]
    [InlineData(AppRole.Employee, AppRole.Employee, true)]
    [InlineData(AppRole.Management, AppRole.Employee, true)]
    [InlineData(AppRole.Administrator, AppRole.Management, true)]
    [InlineData(AppRole.Employee, AppRole.Management, false)]
    [InlineData(AppRole.Unauthorized, AppRole.Employee, false)]
    public void SatisfiesRole_uses_hierarchical_comparison(AppRole current, AppRole required, bool expected)
    {
        Assert.Equal(expected, AppFeatureAuthorization.SatisfiesRole(current, required));
    }
}
