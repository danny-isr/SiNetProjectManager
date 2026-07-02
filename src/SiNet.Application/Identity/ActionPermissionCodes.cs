namespace SiNet.Application.Identity;

/// <summary>
/// Stable action permission codes persisted in <c>ActionPermission.ActionCode</c>.
/// Values match legacy <c>ActionFollowUp</c> enum names — do not rename without a data migration.
/// </summary>
public static class ActionPermissionCodes
{
    public const string NewProjectDialog = nameof(NewProjectDialog);
    public const string ProjectPicker = nameof(ProjectPicker);
    public const string TaskCreationDialog = nameof(TaskCreationDialog);
    public const string FileImportDialog = nameof(FileImportDialog);
    public const string DecisionDialog = nameof(DecisionDialog);
    public const string DisciplineDialog = nameof(DisciplineDialog);
    public const string WorkflowAdvanceDialog = nameof(WorkflowAdvanceDialog);

    private static readonly HashSet<string> KnownCodes =
        new(StringComparer.Ordinal)
        {
            NewProjectDialog,
            ProjectPicker,
            TaskCreationDialog,
            FileImportDialog,
            DecisionDialog,
            DisciplineDialog,
            WorkflowAdvanceDialog,
        };

    /// <summary>
    /// Returns whether <paramref name="actionCode"/> matches a registered legacy action code.
    /// Query services still deny unknown codes via deny-by-default permission rows; this helper
    /// is for documentation and callers that want explicit validation.
    /// </summary>
    public static bool IsKnownActionCode(string actionCode)
        => !string.IsNullOrWhiteSpace(actionCode) && KnownCodes.Contains(actionCode);
}
