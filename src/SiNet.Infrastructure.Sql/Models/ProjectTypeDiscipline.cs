namespace SiNetSQL.Models;

/// <summary>
/// Maps a <see cref="JobType"/> (ProjectType) to <see cref="TaskType"/> rows
/// that act as planning <i>disciplines</i> for that project type (e.g. Traffic,
/// Drainage, Physical). Disciplines are not a global fixed list — they are derived
/// per ProjectType.
/// </summary>
public partial class ProjectTypeDiscipline
{
    public int Id { get; set; }

    public int ProjectTypeId { get; set; }

    public int DisciplineTaskTypeId { get; set; }

    public int? DefaultAssignedGroupId { get; set; }

    public bool IsRequired { get; set; } = true;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    // Navigation
    public virtual JobType ProjectType { get; set; } = null!;

    public virtual TaskType DisciplineTaskType { get; set; } = null!;

    public virtual UserGroup? DefaultAssignedGroup { get; set; }
}
