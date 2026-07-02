namespace SiNet.Infrastructure.Sql.Entities;

/// <summary>Minimal projection of <c>ProjectAssignment</c> for open-task counts on user admin.</summary>
public sealed class ProjectAssignmentEntity
{
    public int Id { get; set; }

    public int? AssignedToId { get; set; }

    public int? StatusId { get; set; }

    public ProjectAssignmentStatusEntity? AssignmentStatus { get; set; }
}
