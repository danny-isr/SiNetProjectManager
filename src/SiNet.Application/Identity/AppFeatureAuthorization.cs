namespace SiNet.Application.Identity;

/// <summary>
/// Maps <see cref="AppFeatureCodes"/> to minimum <see cref="AppRole"/> (hierarchical: current role must
/// be &gt;= required). Unknown codes are rejected — never approved silently.
/// </summary>
public static class AppFeatureAuthorization
{
    private static readonly IReadOnlyDictionary<string, AppRole> MinimumRoles =
        new Dictionary<string, AppRole>(StringComparer.Ordinal)
        {
            [AppFeatureCodes.ShellOpenEmailSurface] = AppRole.Employee,
            [AppFeatureCodes.ShellOpenProjectWorkSurface] = AppRole.Employee,
            [AppFeatureCodes.ShellOpenInspectionSurface] = AppRole.Employee,
            [AppFeatureCodes.ShellOpenTaskPanelReadOnly] = AppRole.Employee,
            [AppFeatureCodes.ShellOpenWorkflowClosedViewer] = AppRole.Employee,
            [AppFeatureCodes.DevToolsReset] = AppRole.Management,
            [AppFeatureCodes.DevToolsSeed] = AppRole.Management,
            [AppFeatureCodes.ProjectCreate] = AppRole.Management,
            [AppFeatureCodes.ProjectUpdate] = AppRole.Management,
            [AppFeatureCodes.ShellOpenProjectsDashboard] = AppRole.Management,
            [AppFeatureCodes.ReportsManagement] = AppRole.Management,
            [AppFeatureCodes.SystemSettingsWrite] = AppRole.Administrator,
            [AppFeatureCodes.UsersManage] = AppRole.Administrator,
            [AppFeatureCodes.ActionPermissionsManage] = AppRole.Administrator,
            [AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks] = AppRole.Administrator,
            [AppFeatureCodes.ShellOpenFileCatalogAdmin] = AppRole.Administrator,
            [AppFeatureCodes.ShellOpenWorkflowOpsDashboard] = AppRole.Administrator,
            [AppFeatureCodes.ShellOpenProjectTypeWorkflowPolicy] = AppRole.Administrator,
            [AppFeatureCodes.WorkflowOpsAdvance] = AppRole.Administrator,
            [AppFeatureCodes.WorkflowOpsCancel] = AppRole.Administrator,
            [AppFeatureCodes.WorkflowOpsRetry] = AppRole.Administrator,
            [AppFeatureCodes.WorkflowOpsStart] = AppRole.Administrator,
            [AppFeatureCodes.ShellOpenMasterPlanMonthlyRestore] = AppRole.Management,
            [AppFeatureCodes.ShellImportWorkstationSecrets] = AppRole.Employee,
        };

    /// <summary>
    /// Returns the minimum role required for <paramref name="featureCode"/>.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="featureCode"/> is unknown.</exception>
    public static AppRole GetRequiredRole(string featureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureCode);

        if (MinimumRoles.TryGetValue(featureCode, out var role))
        {
            return role;
        }

        throw new ArgumentException(
            $"Unknown feature code '{featureCode}'. Feature access is deny-by-default; register the code in {nameof(AppFeatureCodes)} and {nameof(AppFeatureAuthorization)}.",
            nameof(featureCode));
    }

    /// <summary>
    /// Returns whether <paramref name="currentRole"/> satisfies <paramref name="requiredRole"/> using
    /// hierarchical comparison (<c>current &gt;= required</c>).
    /// </summary>
    public static bool SatisfiesRole(AppRole currentRole, AppRole requiredRole)
        => currentRole >= requiredRole;

    /// <summary>
    /// Returns whether an authenticated user with <paramref name="currentRole"/> may access
    /// <paramref name="featureCode"/>.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="featureCode"/> is unknown.</exception>
    public static bool CanAccessFeature(AppRole currentRole, string featureCode)
        => SatisfiesRole(currentRole, GetRequiredRole(featureCode));
}
