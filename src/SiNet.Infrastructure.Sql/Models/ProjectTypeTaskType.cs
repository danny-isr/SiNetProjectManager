namespace SiNetSQL.Models;

/// <summary>
/// Mapping table that defines which TaskTypes are allowed for a given ProjectType (JobType).
/// This enables filtering of task types based on the project's type(s).
/// </summary>
public class ProjectTypeTaskType
{
    /// <summary>
    /// Foreign key to JobType (ProjectType).
    /// </summary>
    public int ProjectTypeId { get; set; }

    /// <summary>
    /// Foreign key to TaskType.
    /// </summary>
    public int TaskTypeId { get; set; }

    // Navigation properties
    public virtual JobType ProjectType { get; set; } = null!;
    public virtual TaskType TaskType { get; set; } = null!;
}
