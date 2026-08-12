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

    /// <summary>Edit project metadata / job types / rename («עדכון פרויקט»).</summary>
    public const string ProjectUpdate = "Project.Update";
    public const string ReportsManagement = "Reports.Management";
    public const string SystemSettingsWrite = "System.Settings.Write";
    public const string UsersManage = "Users.Manage";
    public const string ActionPermissionsManage = "ActionPermissions.Manage";
    public const string TaskWorkbenchViewOtherUsersTasks = "TaskWorkbench.ViewOtherUsersTasks";
    /// <summary>Admin global file/folder catalog («ניהול קבצים»).</summary>
    public const string ShellOpenFileCatalogAdmin = "Shell.OpenFileCatalogAdmin";

    /// <summary>Admin workflow ops dashboard («בריאות תהליכים»).</summary>
    public const string ShellOpenWorkflowOpsDashboard = "Shell.OpenWorkflowOpsDashboard";

    /// <summary>Admin JobType ↔ WorkflowDefinition mapping («מדיניות סוג↔תהליך»).</summary>
    public const string ShellOpenProjectTypeWorkflowPolicy = "Shell.OpenProjectTypeWorkflowPolicy";

    /// <summary>Business projects overview dashboard («ריכוז פרויקטים»).</summary>
    public const string ShellOpenProjectsDashboard = "Shell.OpenProjectsDashboard";

    /// <summary>Advance / pause / resume / complete a workflow instance from ops UI.</summary>
    public const string WorkflowOpsAdvance = "WorkflowOps.Advance";

    /// <summary>Cancel a workflow instance from ops UI.</summary>
    public const string WorkflowOpsCancel = "WorkflowOps.Cancel";

    /// <summary>Retry stalled workflow recovery from ops UI.</summary>
    public const string WorkflowOpsRetry = "WorkflowOps.Retry";

    /// <summary>Manually start a workflow instance from ops UI.</summary>
    public const string WorkflowOpsStart = "WorkflowOps.Start";

    /// <summary>Monthly MasterPlan bak restore + replica mismatch log («שחזור חודשי MasterPlan»).</summary>
    public const string ShellOpenMasterPlanMonthlyRestore = "Shell.OpenMasterPlanMonthlyRestore";
}
