namespace SiNet.Application.Identity;

/// <summary>
/// Stable feature codes for <see cref="IAuthorizationQueryService.CanCurrentUserAccessFeatureAsync"/>.
/// See <c>docs/IDENTITY_AND_PERMISSIONS.md</c> §7.3 and <see cref="AppFeatureAuthorization"/>.
/// </summary>
public static class AppFeatureCodes
{
    public const string ShellOpenEmailSurface = "Shell.OpenEmailSurface";
    public const string ShellOpenProjectWorkSurface = "Shell.OpenProjectWorkSurface";
    public const string ShellOpenInspectionSurface = "Shell.OpenInspectionSurface";
    public const string ShellOpenTaskPanelReadOnly = "Shell.OpenTaskPanelReadOnly";
    public const string ShellOpenWorkflowClosedViewer = "Shell.OpenWorkflowClosedViewer";
    public const string DevToolsReset = "DevTools.Reset";
    public const string DevToolsSeed = "DevTools.Seed";
    public const string ProjectCreate = "Project.Create";
    public const string ReportsManagement = "Reports.Management";
    public const string SystemSettingsWrite = "System.Settings.Write";
    public const string UsersManage = "Users.Manage";
    public const string ActionPermissionsManage = "ActionPermissions.Manage";
    public const string TaskWorkbenchViewOtherUsersTasks = "TaskWorkbench.ViewOtherUsersTasks";
}
