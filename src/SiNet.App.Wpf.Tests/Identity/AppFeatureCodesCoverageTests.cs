using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

/// <summary>
/// Ensures every registered <see cref="AppFeatureCodes"/> constant is mapped in
/// <see cref="AppFeatureAuthorization"/> (fail-closed for unknown codes).
/// </summary>
public sealed class AppFeatureCodesCoverageTests
{
    public static IEnumerable<object[]> AllFeatureCodes =>
        new[]
        {
            AppFeatureCodes.ShellOpenEmailSurface,
            AppFeatureCodes.ShellOpenProjectWorkSurface,
            AppFeatureCodes.ShellOpenInspectionSurface,
            AppFeatureCodes.ShellOpenTaskPanelReadOnly,
            AppFeatureCodes.ShellOpenWorkflowClosedViewer,
            AppFeatureCodes.DevToolsReset,
            AppFeatureCodes.DevToolsSeed,
            AppFeatureCodes.ProjectCreate,
            AppFeatureCodes.ReportsManagement,
            AppFeatureCodes.SystemSettingsWrite,
            AppFeatureCodes.UsersManage,
            AppFeatureCodes.ActionPermissionsManage,
            AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks,
            AppFeatureCodes.ShellOpenFileCatalogAdmin,
            AppFeatureCodes.ShellOpenWorkflowOpsDashboard,
            AppFeatureCodes.ShellOpenProjectTypeWorkflowPolicy,
            AppFeatureCodes.ShellOpenProjectsDashboard,
        }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllFeatureCodes))]
    public void Registered_feature_code_resolves_minimum_role(string featureCode)
    {
        var role = AppFeatureAuthorization.GetRequiredRole(featureCode);
        Assert.True(role >= AppRole.Employee);
    }

    [Theory]
    [InlineData(AppRole.Unauthorized, AppFeatureCodes.ShellOpenEmailSurface, false)]
    [InlineData(AppRole.Employee, AppFeatureCodes.ShellOpenEmailSurface, true)]
    [InlineData(AppRole.Employee, AppFeatureCodes.ProjectCreate, false)]
    [InlineData(AppRole.Management, AppFeatureCodes.ProjectCreate, true)]
    [InlineData(AppRole.Management, AppFeatureCodes.UsersManage, false)]
    [InlineData(AppRole.Administrator, AppFeatureCodes.UsersManage, true)]
    [InlineData(AppRole.Administrator, AppFeatureCodes.ActionPermissionsManage, true)]
    [InlineData(AppRole.Administrator, AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks, true)]
    [InlineData(AppRole.Management, AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks, false)]
    [InlineData(AppRole.Administrator, AppFeatureCodes.ShellOpenFileCatalogAdmin, true)]
    [InlineData(AppRole.Management, AppFeatureCodes.ShellOpenFileCatalogAdmin, false)]
    [InlineData(AppRole.Administrator, AppFeatureCodes.ShellOpenWorkflowOpsDashboard, true)]
    [InlineData(AppRole.Management, AppFeatureCodes.ShellOpenWorkflowOpsDashboard, false)]
    [InlineData(AppRole.Administrator, AppFeatureCodes.ShellOpenProjectTypeWorkflowPolicy, true)]
    [InlineData(AppRole.Management, AppFeatureCodes.ShellOpenProjectTypeWorkflowPolicy, false)]
    [InlineData(AppRole.Management, AppFeatureCodes.ShellOpenProjectsDashboard, true)]
    [InlineData(AppRole.Employee, AppFeatureCodes.ShellOpenProjectsDashboard, false)]
    public void Feature_role_matrix(AppRole role, string featureCode, bool expected)
    {
        Assert.Equal(expected, AppFeatureAuthorization.CanAccessFeature(role, featureCode));
    }
}
