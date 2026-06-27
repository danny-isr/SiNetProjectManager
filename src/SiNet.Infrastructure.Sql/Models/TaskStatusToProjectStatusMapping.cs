namespace SiNetSQL.Models;

/// <summary>
/// Maps a Task (ProjectAssignment) status to a Project status.
/// When a task's status is changed to <see cref="TaskStatusId"/>,
/// the parent project's status is automatically updated to <see cref="ProjectStatusId"/>.
/// Only triggers on explicit status changes — NOT on initial task creation.
/// </summary>
public class TaskStatusToProjectStatusMapping
{
    public int Id { get; set; }

    /// <summary>
    /// FK to ProjectAssignmentStatus — the task status that triggers the mapping.
    /// </summary>
    public int TaskStatusId { get; set; }

    /// <summary>
    /// FK to ProjectStatus — the project status to apply when the trigger fires.
    /// </summary>
    public int ProjectStatusId { get; set; }

    // Navigation properties
    public virtual ProjectAssignmentStatus TaskStatus { get; set; } = null!;
    public virtual ProjectStatus ProjectStatus { get; set; } = null!;
}
