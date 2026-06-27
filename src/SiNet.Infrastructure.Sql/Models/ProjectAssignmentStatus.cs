namespace SiNetSQL.Models;

/// <summary>
/// Represents the status of a project assignment/task.
/// </summary>
public partial class ProjectAssignmentStatus
{
    public int Id { get; set; }

    /// <summary>
    /// Stable machine identifier (e.g. <c>Open</c>, <c>InProgress</c>, <c>WaitingExternal</c>,
    /// <c>Completed</c>, <c>Cancelled</c>). Unique. Used by workflow logic — never localized.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Indicates whether this status represents an alive/active task (not completed or cancelled).
    /// Open statuses include both actionable tasks and tasks waiting for external parties.
    /// Closed statuses (IsOpen = false) are truly finished (completed, approved, cancelled).
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// Indicates whether the ball is "in our court" — the office has work to do on this task.
    /// <para>
    /// <b>IsOpen=true, IsActionable=true</b>  → Active, we need to act.<br/>
    /// <b>IsOpen=true, IsActionable=false</b> → Alive but waiting for external party.<br/>
    /// <b>IsOpen=false, IsActionable=false</b> → Closed/done.
    /// </para>
    /// WorkPriority queue membership is driven by this flag (not <see cref="IsOpen"/>).
    /// </summary>
    public bool IsActionable { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Global default color for this status (hex, e.g. "#FFEBEE").
    /// Used when no user override exists. Nullable — falls back to #808080.
    /// </summary>
    public string? DefaultColorHex { get; set; }

    // Navigation properties
    public virtual ICollection<ProjectAssignment> ProjectAssignments { get; set; } = new List<ProjectAssignment>();

    public virtual ICollection<ProjectAssignmentEvent> ProjectAssignmentEventsOldStatus { get; set; } = new List<ProjectAssignmentEvent>();

    public virtual ICollection<ProjectAssignmentEvent> ProjectAssignmentEventsNewStatus { get; set; } = new List<ProjectAssignmentEvent>();

    // ProjectType mappings - which ProjectTypes allow this Status
    public virtual ICollection<ProjectTypeStatus> AllowedForProjectTypes { get; set; } = new List<ProjectTypeStatus>();

    // Per-user color overrides for this status
    public virtual ICollection<UserStatusPreference> UserStatusPreferences { get; set; } = new List<UserStatusPreference>();
}
