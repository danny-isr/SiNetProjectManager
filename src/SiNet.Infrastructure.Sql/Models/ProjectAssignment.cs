using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiNetSQL.Models;

public partial  class ProjectAssignment
{
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// Legacy priority field (string, preserved for backward compatibility).
    /// New code should use <see cref="WorkPriority"/> (int) instead, which represents
    /// the task's position in the assignee's work queue.
    /// </summary>
    public string? Priority { get; set; }

    /// <summary>
    /// Legacy status field (preserved for backward compatibility).
    /// New code should use StatusId instead.
    /// </summary>
    public string? Status { get; set; }

    public float? PercentComplete { get; set; }

    public int? AssignedToId { get; set; }

    public string? Body { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? DueDate { get; set; }

    public float? Grading { get; set; }

    public int? TaskGroupId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    // === NEW: Task Management Fields ===

    /// <summary>
    /// FK to TaskType. Defines what type of task this is.
    /// </summary>
    public int? TaskTypeId { get; set; }

    /// <summary>
    /// FK to ProjectAssignmentStatus. Defines the current status.
    /// </summary>
    public int? StatusId { get; set; }

    /// <summary>
    /// Optional FK to <see cref="TaskResultDefinition"/>. Stores the most recent
    /// professional/business result recorded for this task (convenience snapshot of
    /// the last <see cref="ProjectAssignmentEvent.TaskResultId"/>). Historical truth
    /// lives on the events.
    /// </summary>
    public int? LastTaskResultId { get; set; }

    /// <summary>
    /// Priority in the employee's work queue (1 = highest).
    /// Only set for open tasks. NULL for closed tasks.
    /// Unique per employee across all their open tasks.
    /// </summary>
    public int? WorkPriority { get; set; }

    // === Hierarchy (Smart Tasks P1) ===

    /// <summary>
    /// Optional FK to a parent <see cref="ProjectAssignment"/> (self-reference).
    /// NULL means this task is a top-level task. NOT NULL means it is a child task
    /// belonging to the parent's work breakdown.
    /// </summary>
    public int? ParentAssignmentId { get; set; }

    /// <summary>
    /// When this task is a child (<see cref="ParentAssignmentId"/> is set), determines
    /// whether the parent must wait for this child to close before it can auto-close.
    /// Ignored when the task has no parent.
    /// </summary>
    public bool IsRequiredForParentCompletion { get; set; } = true;

    /// <summary>
    /// Optional ordering of children inside their parent (for UI display).
    /// </summary>
    public int? SortOrderInParent { get; set; }

    // === Computed Display Properties ===

    /// <summary>
    /// Aggregated preview of recent notes (after the last status change).
    /// Populated by TaskService.PopulateRecentNotes() during data load.
    /// </summary>
    [NotMapped]
    public string? RecentNotesSummary { get; set; }

    // === Navigation Properties ===

    public virtual Siuser? AssignedTo { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Project? Project { get; set; }

    public virtual Siuser? TaskGroup { get; set; }

    // NEW: Task Management navigation
    public virtual TaskType? TaskType { get; set; }

    public virtual ProjectAssignmentStatus? AssignmentStatus { get; set; }

    public virtual TaskResultDefinition? LastTaskResult { get; set; }

    public virtual ICollection<ProjectAssignmentEvent> ProjectAssignmentEvents { get; set; } = new List<ProjectAssignmentEvent>();

    public virtual ICollection<TaskLink> TaskLinks { get; set; } = new List<TaskLink>();

    // Hierarchy navigation (self-reference)
    public virtual ProjectAssignment? ParentAssignment { get; set; }

    public virtual ICollection<ProjectAssignment> ChildAssignments { get; set; } = new List<ProjectAssignment>();
}
