namespace SiNetSQL.Models;

/// <summary>
/// A named group that users can belong to.
/// Task/workflow permissions are granted per group.
/// </summary>
public class UserGroup
{
    public int Id { get; set; }

    /// <summary>Stable code for programmatic lookup (e.g. "OfficeManagement").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name shown in UI (e.g. "ניהול משרד").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Default user to assign tasks to when this group is responsible for a workflow stage.
    /// If null and group has 1 member → auto-assign. If multiple → user must pick.
    /// </summary>
    public int? DefaultAssigneeId { get; set; }

    // Navigation
    public virtual Siuser? DefaultAssignee { get; set; }
    public virtual ICollection<UserGroupMembership> Memberships { get; set; } = new List<UserGroupMembership>();
}
