using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for TaskStatusToProjectStatusMapping.
/// Maps to dbo.TaskStatusToProjectStatusMapping — pairs task statuses with project statuses.
/// </summary>
public class TaskStatusToProjectStatusMappingConfiguration : IEntityTypeConfiguration<TaskStatusToProjectStatusMapping>
{
    public void Configure(EntityTypeBuilder<TaskStatusToProjectStatusMapping> builder)
    {
        builder.ToTable("TaskStatusToProjectStatusMapping");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("ID");

        builder.Property(e => e.TaskStatusId)
            .HasColumnName("TaskStatusID");

        builder.Property(e => e.ProjectStatusId)
            .HasColumnName("ProjectStatusID");

        // Unique constraint: each task status maps to at most one project status
        builder.HasIndex(e => e.TaskStatusId, "IX_TaskStatusToProjectStatusMapping_TaskStatus")
            .IsUnique();

        // FK to ProjectAssignmentStatus (no cascade — must delete mapping before deleting status)
        builder.HasOne(d => d.TaskStatus)
            .WithMany()
            .HasForeignKey(d => d.TaskStatusId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TaskStatusProjectStatusMapping_TaskStatus");

        // FK to ProjectStatus (no cascade — must delete mapping before deleting status)
        builder.HasOne(d => d.ProjectStatus)
            .WithMany()
            .HasForeignKey(d => d.ProjectStatusId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TaskStatusProjectStatusMapping_ProjectStatus");
    }
}
