using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="ProjectAlternative"/>.
/// Each row represents a design alternative belonging to a single Project.
/// Hierarchy: Project → ProjectAlternative.
/// </summary>
public class ProjectAlternativeConfiguration : IEntityTypeConfiguration<ProjectAlternative>
{
    public void Configure(EntityTypeBuilder<ProjectAlternative> builder)
    {
        builder.ToTable("ProjectAlternative");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        // ─── Required FK to Project ───
        builder.Property(e => e.ProjectId)
            .HasColumnName("ProjectID")
            .IsRequired();

        // ─── Name (required, max 20, Hebrew collation) ───
        // Business rule: full alternative name (including the optional "~" second level)
        // is capped at 20 characters. Bootstrap of "1" is done in code, not via DB default.
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(20)
            .UseCollation("Hebrew_100_CI_AS");

        // ─── NormalizedName (required, max 20, used for duplicate detection) ───
        // No default value: bootstrap of "1" is handled in code, not at DB level.
        builder.Property(e => e.NormalizedName)
            .IsRequired()
            .HasMaxLength(20);

        // ─── Code (optional, max 50) ───
        builder.Property(e => e.Code)
            .HasMaxLength(50);

        // ─── Description (optional, nvarchar(max)) ───
        builder.Property(e => e.Description)
            .UseCollation("Hebrew_100_CI_AS");

        builder.Property(e => e.IsPrimary)
            .HasDefaultValue(false);

        builder.Property(e => e.SortOrder)
            .HasDefaultValue(0);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        // ─── FolderPath (optional, max 1000) ───
        builder.Property(e => e.FolderPath)
            .HasMaxLength(1000);

        builder.Property(e => e.CreatedFromFolderScan)
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedByUserID");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("datetime2");

        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedByUserID");

        // ═══════════════════════════════════════════════════════════════════════
        // Indexes
        // ═══════════════════════════════════════════════════════════════════════

        // Unique: no duplicate NormalizedName among ACTIVE alternatives within the same Project.
        // Filtered so soft-deleted (IsActive=0) rows do not block re-creation.
        builder.HasIndex(e => new { e.ProjectId, e.NormalizedName },
            "UX_ProjectAlternative_ProjectID_NormalizedName")
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        // Fast lookup by parent
        builder.HasIndex(e => e.ProjectId,
            "IX_ProjectAlternative_ProjectID");

        // Filter by active state
        builder.HasIndex(e => e.IsActive,
            "IX_ProjectAlternative_IsActive");

        builder.HasIndex(e => e.IsPrimary,
            "IX_ProjectAlternative_IsPrimary");

        // ═══════════════════════════════════════════════════════════════════════
        // Relationships
        // ═══════════════════════════════════════════════════════════════════════

        // FK to Project (cascade — delete alternatives when project removed)
        builder.HasOne(e => e.Project)
            .WithMany(p => p.Alternatives)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ProjectAlternative_Project");

        // FK to CreatedByUser (no cascade — keep record even if user deactivated)
        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProjectAlternative_CreatedByUser");

        // FK to UpdatedByUser (no cascade)
        builder.HasOne(e => e.UpdatedByUser)
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProjectAlternative_UpdatedByUser");
    }
}
