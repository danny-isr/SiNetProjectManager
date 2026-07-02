namespace SiNet.Infrastructure.Sql.Entities;

/// <summary>Minimal projection of <c>ProjectAssignmentStatus</c> for open-task counts.</summary>
public sealed class ProjectAssignmentStatusEntity
{
    public int Id { get; set; }

    public bool IsOpen { get; set; }

    public ICollection<ProjectAssignmentEntity> ProjectAssignments { get; set; } = [];
}
