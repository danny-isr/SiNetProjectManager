using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TaskLink"/>.
/// Polymorphic link between tasks and any related entity.
/// </summary>
public class TaskLinkConfiguration : IEntityTypeConfiguration<TaskLink>
{
    public void Configure(EntityTypeBuilder<TaskLink> builder)
    {
        builder.ToTable("TaskLink");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.TaskId).HasColumnName("TaskID").IsRequired();
        builder.Property(e => e.LinkedEntityType).IsRequired();
        builder.Property(e => e.LinkedEntityId).IsRequired();
        builder.Property(e => e.Role).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.CreatedByUserId).HasColumnName("CreatedByUserID").IsRequired();

        // ═══ Smart Tasks P2: Work Target fields ═══
        builder.Property(e => e.IsWorkTarget).HasDefaultValue(false);
        builder.Property(e => e.WorkStatus)
            .HasConversion<int>()
            .HasDefaultValue(WorkTargetStatus.Pending);
        builder.Property(e => e.CompletedAtUtc);
        builder.Property(e => e.CompletedByUserId).HasColumnName("CompletedByUserID");

        // Store enums as int (default, but explicit for clarity)
        builder.Property(e => e.LinkedEntityType).HasConversion<int>();
        builder.Property(e => e.Role).HasConversion<int>();

        // ═══ Indexes ═══

        // Fast lookup: all links for a specific task
        builder.HasIndex(e => e.TaskId, "IX_TaskLink_TaskID");

        // Fast reverse lookup: all tasks linked to a specific entity
        builder.HasIndex(e => new { e.LinkedEntityType, e.LinkedEntityId }, "IX_TaskLink_LinkedEntity");

        // Prevent duplicate links: same task → same entity with same role
        builder.HasIndex(e => new { e.TaskId, e.LinkedEntityType, e.LinkedEntityId, e.Role },
            "IX_TaskLink_Unique").IsUnique();

        // Fast lookup of open work targets for a given task
        builder.HasIndex(e => new { e.TaskId, e.IsWorkTarget, e.WorkStatus },
            "IX_TaskLink_WorkTarget");

        // ═══ Relationships ═══

        builder.HasOne(e => e.Task)
            .WithMany(p => p.TaskLinks)
            .HasForeignKey(e => e.TaskId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TaskLink_ProjectAssignment");

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_TaskLink_CreatedByUser");
    }
}
