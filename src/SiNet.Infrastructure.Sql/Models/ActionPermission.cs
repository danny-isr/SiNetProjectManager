namespace SiNetSQL.Models;

/// <summary>
/// Maps an action type (the <c>ActionFollowUp</c> enum, defined in the legacy host) to an authorized user.
/// Only users listed here may execute (or be assigned) the corresponding action.
/// 
/// Authorization model: deny-by-default.
/// When no rows exist for a given action code, the action is blocked for all non-admin users.
/// Administrators bypass action-level permission checks.
/// 
/// Legacy note: prior behavior was open-access when no rows existed.
/// That behavior is superseded by AuthorizationPrinciples-2026-06-18.
/// </summary>
public class ActionPermission
{
    public int Id { get; set; }

    /// <summary>
    /// The action identifier this permission applies to.
    /// Matches the <c>ActionFollowUp</c> enum name (defined in the legacy host)
    /// (e.g., "NewProjectDialog", "TaskCreationDialog", "DecisionDialog").
    /// </summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>
    /// Display name for this action in the UI (Hebrew).
    /// E.g., "יצירת פרויקט חדש", "יצירת משימה".
    /// </summary>
    public string ActionDisplayName { get; set; } = string.Empty;

    /// <summary>FK to the authorized user.</summary>
    public int UserId { get; set; }

    /// <summary>Soft-delete / disable flag.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ═══ Navigation ═══

    public virtual Siuser User { get; set; } = null!;
}
