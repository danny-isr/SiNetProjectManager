using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="WorkflowStageTask"/>.
/// Links workflow stage definitions to task types with optional default assignees.
/// </summary>
public class WorkflowStageTaskConfiguration : IEntityTypeConfiguration<WorkflowStageTask>
{
    public void Configure(EntityTypeBuilder<WorkflowStageTask> builder)
    {
        builder.ToTable("WorkflowStageTask");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.StageDefinitionId)
            .HasColumnName("StageDefinitionID")
            .IsRequired();

        builder.Property(e => e.TaskTypeId)
            .HasColumnName("TaskTypeID")
            .IsRequired();

        builder.Property(e => e.DefaultAssigneeId)
            .HasColumnName("DefaultAssigneeID");

        builder.Property(e => e.Notes)
            .HasMaxLength(500);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // Each (Stage, TaskType) pair must be unique
        builder.HasIndex(e => new { e.StageDefinitionId, e.TaskTypeId },
            "IX_WorkflowStageTask_Stage_TaskType").IsUnique();

        // Fast lookup by stage
        builder.HasIndex(e => e.StageDefinitionId, "IX_WorkflowStageTask_Stage");

        // FK to WorkflowStageDefinition
        builder.HasOne(e => e.StageDefinition)
            .WithMany(s => s.StageTasks)
            .HasForeignKey(e => e.StageDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowStageTask_StageDefinition");

        // FK to TaskType (no cascade — don't delete tasks if type is removed)
        builder.HasOne(e => e.TaskType)
            .WithMany()
            .HasForeignKey(e => e.TaskTypeId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowStageTask_TaskType");

        // FK to Siuser (optional default assignee)
        builder.HasOne(e => e.DefaultAssignee)
            .WithMany()
            .HasForeignKey(e => e.DefaultAssigneeId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_WorkflowStageTask_DefaultAssignee");
    }
}
