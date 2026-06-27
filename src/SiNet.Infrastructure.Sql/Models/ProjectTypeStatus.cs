namespace SiNetSQL.Models;

/// <summary>
/// Mapping table that defines which ProjectAssignmentStatuses are allowed for a given ProjectType (JobType).
/// This enables filtering of statuses based on the project's type(s).
/// </summary>
public class ProjectTypeStatus
{
    /// <summary>
    /// Foreign key to JobType (ProjectType).
    /// </summary>
    public int ProjectTypeId { get; set; }

    /// <summary>
    /// Foreign key to ProjectAssignmentStatus.
    /// </summary>
    public int StatusId { get; set; }

    // Navigation properties
    public virtual JobType ProjectType { get; set; } = null!;
    public virtual ProjectAssignmentStatus Status { get; set; } = null!;
}
