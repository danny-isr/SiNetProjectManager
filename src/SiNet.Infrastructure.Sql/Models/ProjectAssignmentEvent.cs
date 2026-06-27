namespace SiNetSQL.Models;

/// <summary>
/// Represents a log entry/event for a project assignment/task.
/// Used to track status changes, external communications, and notes.
/// </summary>
public partial class ProjectAssignmentEvent
{
    public int Id { get; set; }

    public int ProjectAssignmentId { get; set; }

    /// <summary>
    /// Type of event (e.g., "StatusChange", "Note", "ExternalWait", "EmailLink").
    /// </summary>
    public string EventType { get; set; } = null!;

    public int? OldStatusId { get; set; }

    public int? NewStatusId { get; set; }

    /// <summary>
    /// Optional reference to an external contact (when task is waiting for external input).
    /// </summary>
    public int? ContactId { get; set; }

    /// <summary>
    /// Optional reference to an external company (when task is waiting for external input).
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>
    /// Free-text reference for external party (e.g., name, description).
    /// </summary>
    public string? ExternalReferenceText { get; set; }

    /// <summary>
    /// Note or description for this event.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Optional Gmail thread ID for email reference (no FK - simple string).
    /// </summary>
    public string? EmailThreadId { get; set; }

    /// <summary>
    /// Optional reference to a <see cref="TaskLink"/> that serves as proof/evidence
    /// for this event (e.g., the email sent when moving to "waiting for external").
    /// </summary>
    public int? TaskLinkId { get; set; }

    /// <summary>
    /// Optional reference to a <see cref="TaskResultDefinition"/> recorded for this event.
    /// Used by workflow actions to capture professional/business outcomes
    /// (e.g. AuthorityApproved, QuoteSent) — not generic task statuses.
    /// </summary>
    public int? TaskResultId { get; set; }

    public int CreatedByUserId { get; set; }

    /// <summary>
    /// Event creation date stored in UTC.
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Returns CreatedDate converted to local time for display.
    /// </summary>
    public DateTime LocalCreatedDate => CreatedDate.Kind == DateTimeKind.Utc 
        ? CreatedDate.ToLocalTime() 
        : DateTime.SpecifyKind(CreatedDate, DateTimeKind.Utc).ToLocalTime();

    // Navigation properties
    public virtual ProjectAssignment ProjectAssignment { get; set; } = null!;

    public virtual ProjectAssignmentStatus? OldStatus { get; set; }

    public virtual ProjectAssignmentStatus? NewStatus { get; set; }

    public virtual Contact? Contact { get; set; }

    public virtual Company? Company { get; set; }

    /// <summary>
    /// Optional proof/evidence link associated with this event.
    /// </summary>
    public virtual TaskLink? TaskLink { get; set; }

    /// <summary>
    /// Optional professional/business result recorded for this event.
    /// </summary>
    public virtual TaskResultDefinition? TaskResult { get; set; }

    public virtual Siuser CreatedByUser { get; set; } = null!;
}
