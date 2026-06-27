using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core configuration for the three Project Decisions tables:
/// DecisionCategory, ProjectDecision, DecisionHistory.
/// </summary>
public class DecisionCategoryConfiguration : IEntityTypeConfiguration<DecisionCategory>
{
    public void Configure(EntityTypeBuilder<DecisionCategory> builder)
    {
        builder.ToTable("DecisionCategory");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(e => e.Name, "IX_DecisionCategory_Name")
            .IsUnique();
    }
}

public class ProjectDecisionConfiguration : IEntityTypeConfiguration<ProjectDecision>
{
    public void Configure(EntityTypeBuilder<ProjectDecision> builder)
    {
        builder.ToTable("ProjectDecision");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.ProjectId).HasColumnName("ProjectID");
        builder.Property(e => e.CategoryId).HasColumnName("CategoryID");
        builder.Property(e => e.CreatedByUserId).HasColumnName("CreatedByUserID");
        builder.Property(e => e.LastUpdatedByUserId).HasColumnName("LastUpdatedByUserID");

        builder.Property(e => e.Content)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETDATE()");

        // Index: fast lookup by project
        builder.HasIndex(e => e.ProjectId, "IX_ProjectDecision_Project");

        // FK to Project (cascade — delete decisions when project is deleted)
        builder.HasOne(d => d.Project)
            .WithMany()
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ProjectDecision_Project");

        // FK to DecisionCategory (restrict — cannot delete category while decisions reference it)
        builder.HasOne(d => d.Category)
            .WithMany(c => c.Decisions)
            .HasForeignKey(d => d.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProjectDecision_Category");

        // FK to CreatedByUser (restrict — cannot delete user while they created decisions)
        builder.HasOne(d => d.CreatedByUser)
            .WithMany()
            .HasForeignKey(d => d.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProjectDecision_CreatedByUser");

        // FK to LastUpdatedByUser (optional, restrict)
        builder.HasOne(d => d.LastUpdatedByUser)
            .WithMany()
            .HasForeignKey(d => d.LastUpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProjectDecision_LastUpdatedByUser");
    }
}

public class DecisionHistoryConfiguration : IEntityTypeConfiguration<DecisionHistory>
{
    public void Configure(EntityTypeBuilder<DecisionHistory> builder)
    {
        builder.ToTable("DecisionHistory");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.DecisionId).HasColumnName("DecisionID");
        builder.Property(e => e.ChangedByUserId).HasColumnName("ChangedByUserID");

        builder.Property(e => e.OldContent)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(e => e.ChangedAt)
            .HasDefaultValueSql("GETDATE()");

        // Index: fast lookup by decision
        builder.HasIndex(e => e.DecisionId, "IX_DecisionHistory_Decision");

        // FK to ProjectDecision (cascade — delete history when decision is deleted)
        builder.HasOne(d => d.Decision)
            .WithMany(p => p.History)
            .HasForeignKey(d => d.DecisionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_DecisionHistory_Decision");

        // FK to ChangedByUser (restrict)
        builder.HasOne(d => d.ChangedByUser)
            .WithMany()
            .HasForeignKey(d => d.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_DecisionHistory_ChangedByUser");
    }
}
