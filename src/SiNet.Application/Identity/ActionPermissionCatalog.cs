namespace SiNet.Application.Identity;

/// <summary>
/// Known action permission definitions for admin UI and persistence. Codes match
/// <see cref="ActionPermissionCodes"/> and legacy <c>ActionFollowUp</c> enum names.
/// </summary>
public static class ActionPermissionCatalog
{
    public sealed record Entry(string ActionCode, string DisplayName);

    private static readonly Entry[] AllEntries =
    [
        new(ActionPermissionCodes.NewProjectDialog, "יצירת פרויקט חדש"),
        new(ActionPermissionCodes.ProjectPicker, "שיוך לפרויקט קיים"),
        new(ActionPermissionCodes.TaskCreationDialog, "יצירת / שיוך משימה"),
        new(ActionPermissionCodes.FileImportDialog, "ייבוא קבצים"),
        new(ActionPermissionCodes.DecisionDialog, "העברה להחלטה"),
        new(ActionPermissionCodes.DisciplineDialog, "הוספת תחום"),
        new(ActionPermissionCodes.WorkflowAdvanceDialog, "קידום תהליך"),
    ];

    /// <summary>All registered action definitions in display order.</summary>
    public static IReadOnlyList<Entry> All => AllEntries;

    /// <summary>Returns the Hebrew display name for a known action code, or <see langword="null"/>.</summary>
    public static string? GetDisplayName(string actionCode)
    {
        foreach (var entry in AllEntries)
        {
            if (string.Equals(entry.ActionCode, actionCode, StringComparison.Ordinal))
            {
                return entry.DisplayName;
            }
        }

        return null;
    }
}
