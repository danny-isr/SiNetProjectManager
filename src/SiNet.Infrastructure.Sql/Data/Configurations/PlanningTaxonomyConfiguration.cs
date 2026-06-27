using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// Fluent configurations for the Planning Workflow taxonomy refactor:
/// <list type="bullet">
/// <item><see cref="TaskResultDefinition"/></item>
/// <item><see cref="ProjectTypeWorkflowStage"/></item>
/// <item><see cref="ProjectTypeDiscipline"/></item>
/// </list>
/// Plus indexes/columns added to existing entities (ProjectStatus.Code,
/// ProjectAssignmentStatus.Code, ProjectAssignment.LastTaskResultId,
/// ProjectAssignmentEvent.TaskResultId).
/// </summary>
public class TaskResultDefinitionConfiguration : IEntityTypeConfiguration<TaskResultDefinition>
{
    public void Configure(EntityTypeBuilder<TaskResultDefinition> builder)
    {
        builder.ToTable("TaskResultDefinition");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Category).HasMaxLength(64);

        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasDatabaseName("IX_TaskResultDefinition_Code");
    }
}

public class ProjectTypeWorkflowStageConfiguration : IEntityTypeConfiguration<ProjectTypeWorkflowStage>
{
    public void Configure(EntityTypeBuilder<ProjectTypeWorkflowStage> builder)
    {
        builder.ToTable("ProjectTypeWorkflowStage");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => new { e.ProjectTypeId, e.WorkflowStageDefinitionId })
            .IsUnique()
            .HasDatabaseName("IX_ProjectTypeWorkflowStage_ProjectType_Stage");

        builder.HasOne(e => e.ProjectType)
            .WithMany(j => j.WorkflowStages)
            .HasForeignKey(e => e.ProjectTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.WorkflowStageDefinition)
            .WithMany()
            .HasForeignKey(e => e.WorkflowStageDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectTypeDisciplineConfiguration : IEntityTypeConfiguration<ProjectTypeDiscipline>
{
    public void Configure(EntityTypeBuilder<ProjectTypeDiscipline> builder)
    {
        builder.ToTable("ProjectTypeDiscipline");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => new { e.ProjectTypeId, e.DisciplineTaskTypeId })
            .IsUnique()
            .HasDatabaseName("IX_ProjectTypeDiscipline_ProjectType_TaskType");

        builder.HasOne(e => e.ProjectType)
            .WithMany(j => j.Disciplines)
            .HasForeignKey(e => e.ProjectTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DisciplineTaskType)
            .WithMany()
            .HasForeignKey(e => e.DisciplineTaskTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DefaultAssignedGroup)
            .WithMany()
            .HasForeignKey(e => e.DefaultAssignedGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Adds Code/SortOrder/IsActive indexes/columns to existing
/// <see cref="ProjectStatus"/> and <see cref="ProjectAssignmentStatus"/>,
/// plus the new TaskResult FKs on
/// <see cref="ProjectAssignment"/> and <see cref="ProjectAssignmentEvent"/>.
/// Applied as a single configuration to keep the change set together.
/// </summary>
public class PlanningTaxonomyExtensionsConfiguration
    : IEntityTypeConfiguration<ProjectStatus>,
      IEntityTypeConfiguration<ProjectAssignmentStatus>,
      IEntityTypeConfiguration<ProjectAssignment>,
      IEntityTypeConfiguration<ProjectAssignmentEvent>
{
    public void Configure(EntityTypeBuilder<ProjectStatus> builder)
    {
        builder.Property(e => e.Code).HasMaxLength(64).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasDatabaseName("IX_ProjectStatus_Code");
    }

    public void Configure(EntityTypeBuilder<ProjectAssignmentStatus> builder)
    {
        builder.Property(e => e.Code).HasMaxLength(64).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasDatabaseName("IX_ProjectAssignmentStatus_Code");
    }

    public void Configure(EntityTypeBuilder<ProjectAssignment> builder)
    {
        builder.HasOne(e => e.LastTaskResult)
            .WithMany(r => r.AssignmentsLastResult)
            .HasForeignKey(e => e.LastTaskResultId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_ProjectAssignment_LastTaskResult");
    }

    public void Configure(EntityTypeBuilder<ProjectAssignmentEvent> builder)
    {
        builder.HasOne(e => e.TaskResult)
            .WithMany(r => r.Events)
            .HasForeignKey(e => e.TaskResultId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProjectAssignmentEvent_TaskResult");
    }
}
