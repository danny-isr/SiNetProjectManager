using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="ProjectTypeWorkflowDefinition"/>.
/// Maps ProjectType (JobType) ↔ WorkflowDefinition with enable/default/sort control.
/// </summary>
public class ProjectTypeWorkflowDefinitionConfiguration : IEntityTypeConfiguration<ProjectTypeWorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<ProjectTypeWorkflowDefinition> builder)
    {
        builder.ToTable("ProjectTypeWorkflowDefinition");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.ProjectTypeId)
            .HasColumnName("ProjectTypeID")
            .IsRequired();

        builder.Property(e => e.WorkflowDefinitionId)
            .HasColumnName("WorkflowDefinitionID")
            .IsRequired();

        builder.Property(e => e.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // Unique: each ProjectType can map to a WorkflowDefinition only once
        builder.HasIndex(e => new { e.ProjectTypeId, e.WorkflowDefinitionId },
            "IX_ProjectTypeWorkflowDefinition_Unique").IsUnique();

        // Fast lookup: all workflows for a project type
        builder.HasIndex(e => e.ProjectTypeId,
            "IX_ProjectTypeWorkflowDefinition_ProjectType");

        // FK to JobType (ProjectType)
        builder.HasOne(e => e.ProjectType)
            .WithMany(j => j.AllowedWorkflows)
            .HasForeignKey(e => e.ProjectTypeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ProjectTypeWorkflowDefinition_JobType");

        // FK to WorkflowDefinition
        builder.HasOne(e => e.WorkflowDefinition)
            .WithMany(d => d.AllowedForProjectTypes)
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ProjectTypeWorkflowDefinition_WorkflowDefinition");
    }
}
