namespace SiNetSQL.Models;

/// <summary>
/// The kind of entity a <see cref="TaskLink"/> points to.
/// Add new values when new linkable entity types are introduced.
/// </summary>
public enum TaskLinkEntityType
{
    /// <summary>Link to another <see cref="ProjectAssignment"/> (task-to-task).</summary>
    Task = 1,

    /// <summary>Link to an <see cref="InspectionReport"/>.</summary>
    InspectionReport = 2,

    /// <summary>Link to an <see cref="InspectionNote"/>.</summary>
    InspectionNote = 3,

    /// <summary>Link to an <see cref="EmailInboxMessage"/>.</summary>
    EmailInboxMessage = 4,

    /// <summary>Link to a <see cref="ProjectDecision"/>.</summary>
    ProjectDecision = 5,

    /// <summary>Link to a <see cref="WorkflowInstance"/>.</summary>
    WorkflowInstance = 6,

    /// <summary>Link to a <see cref="ProjectFile"/>.</summary>
    ProjectFile = 7,

    /// <summary>Link to a <see cref="Project"/> (e.g. parent intake task → child Review project).</summary>
    Project = 8,
}

/// <summary>
/// Describes the relationship role between a task and its linked entity.
/// </summary>
public enum TaskLinkRole
{
    /// <summary>The linked entity triggered/created this task.</summary>
    Trigger = 1,

    /// <summary>General informational relationship.</summary>
    Related = 2,

    /// <summary>This task is blocked by the linked entity.</summary>
    BlockedBy = 3,

    /// <summary>This task is a follow-up to the linked entity.</summary>
    FollowUp = 4,

    /// <summary>
    /// The linked entity is the originating source of this task's work
    /// (e.g. the incoming email a FileAttachments task must process, or the
    /// inspection report a review task must inspect). Distinct from
    /// <see cref="Trigger"/>, which is reserved for the workflow instance
    /// that produced the task. A task may have at most one <see cref="Source"/>
    /// link per <see cref="TaskLinkEntityType"/>.
    /// </summary>
    Source = 5,
}

/// <summary>
/// Status of a "work target" — i.e. a linked entity that the task must operate on.
/// Only meaningful when <see cref="TaskLink.IsWorkTarget"/> is <c>true</c>.
/// </summary>
public enum WorkTargetStatus
{
    /// <summary>Target has not been started yet.</summary>
    Pending = 0,

    /// <summary>Work on this target is in progress.</summary>
    InProgress = 1,

    /// <summary>Work on this target has been completed.</summary>
    Done = 2,

    /// <summary>Target was intentionally skipped (counts as resolved for aggregation).</summary>
    Skipped = 3,

    /// <summary>
    /// Target is currently blocked and cannot be progressed.
    /// Reserved for future use — not yet wired into UI/aggregation logic.
    /// </summary>
    Blocked = 4,
}

/// <summary>
/// Polymorphic link between a <see cref="ProjectAssignment"/> (task) and any related entity.
/// Supports linking tasks to reports, notes, emails, decisions, or other tasks.
/// <para>
/// A task may have multiple links (1:N), and the same entity can be linked from multiple tasks.
/// </para>
/// </summary>
public class TaskLink
{
    public int Id { get; set; }

    /// <summary>FK to the owning task (<see cref="ProjectAssignment"/>).</summary>
    public int TaskId { get; set; }

    /// <summary>Discriminator: which entity table <see cref="LinkedEntityId"/> refers to.</summary>
    public TaskLinkEntityType LinkedEntityType { get; set; }

    /// <summary>
    /// Primary key of the linked entity. Stored as <c>long</c> because
    /// <see cref="InspectionNote.NoteId"/> is <c>bigint</c>.
    /// </summary>
    public long LinkedEntityId { get; set; }

    /// <summary>Describes how the linked entity relates to the task.</summary>
    public TaskLinkRole Role { get; set; }

    /// <summary>Optional free-text note about this link.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When <c>true</c>, this link represents a concrete unit of work the task must operate on
    /// (e.g. a specific inspection report to review). The task's completion can then be
    /// aggregated from the <see cref="WorkStatus"/> of all its work targets — see
    /// <see cref="TaskBehaviorDefinition.AggregationMode"/>.
    /// </summary>
    public bool IsWorkTarget { get; set; }

    /// <summary>
    /// Per-target work status. Meaningful only when <see cref="IsWorkTarget"/> is <c>true</c>.
    /// </summary>
    public WorkTargetStatus WorkStatus { get; set; } = WorkTargetStatus.Pending;

    /// <summary>UTC timestamp when this work target was marked <see cref="WorkTargetStatus.Done"/> / <see cref="WorkTargetStatus.Skipped"/>.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>User who completed (or skipped) this work target.</summary>
    public int? CompletedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int CreatedByUserId { get; set; }

    // ═══ Navigation ═══

    public virtual ProjectAssignment Task { get; set; } = null!;

    public virtual Siuser CreatedByUser { get; set; } = null!;

    /// <summary>
    /// Events that reference this link as proof/evidence (reverse navigation).
    /// </summary>
    public virtual ICollection<ProjectAssignmentEvent> ProofEvents { get; set; } = new List<ProjectAssignmentEvent>();
}
