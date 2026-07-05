namespace SiNetSQL.Models;

/// <summary>
/// Represents a type of task (e.g., General, Office Planning, Plan Review).
/// </summary>
public partial class TaskType
{
    public int Id { get; set; }

    /// <summary>
    /// Stable machine identifier (e.g., "General", "OfficePlanning").
    /// Used by seed services and behavior lookups — never changes.
    /// </summary>
    public string Code { get; set; } = null!;

    /// <summary>
    /// Hebrew display name — freely editable without breaking logic.
    /// </summary>
    public string Name { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>
    /// Default personal work-queue bucket for newly created tasks of this type.
    /// When null, the system uses <see cref="WorkQueueBucketCodes.Medium"/>.
    /// </summary>
    public int? DefaultWorkQueueBucket { get; set; }

    // Navigation properties
    public virtual ICollection<ProjectAssignment> ProjectAssignments { get; set; } = new List<ProjectAssignment>();

    // ProjectType mappings - which ProjectTypes allow this TaskType
    public virtual ICollection<ProjectTypeTaskType> AllowedForProjectTypes { get; set; } = new List<ProjectTypeTaskType>();

    // Behavior definition (1:0..1) — defines auto-create/auto-close rules for this task type
    public virtual TaskBehaviorDefinition? BehaviorDefinition { get; set; }
}
