using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for ProjectAccMapping entity.
/// </summary>
public class ProjectAccMappingConfiguration : IEntityTypeConfiguration<ProjectAccMapping>
{
    public void Configure(EntityTypeBuilder<ProjectAccMapping> builder)
    {
        // Table name
        builder.ToTable("ProjectAccMapping");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        // ProjectId - required FK
        builder.Property(e => e.ProjectId)
            .IsRequired();

        // AccHubId - required FK
        builder.Property(e => e.AccHubId)
            .IsRequired();

        // AccProjectId - optional, max 100 chars
        builder.Property(e => e.AccProjectId)
            .HasMaxLength(100)
            .IsUnicode(true);

        // AccProjectName - optional, max 200 chars
        builder.Property(e => e.AccProjectName)
            .HasMaxLength(200)
            .IsUnicode(true);

        // AccTargetFolderId - optional, max 200 chars
        builder.Property(e => e.AccTargetFolderId)
            .HasMaxLength(200)
            .IsUnicode(true);

        // AccTargetFolderPath - optional, max 500 chars
        builder.Property(e => e.AccTargetFolderPath)
            .HasMaxLength(500)
            .IsUnicode(true);

        // LastVerifiedUtc - optional
        builder.Property(e => e.LastVerifiedUtc)
            .HasColumnType("datetime2");

        // === Platform and Docs Status columns ===

        // AccPlatform - stored as int, default Unknown (0)
        builder.Property(e => e.AccPlatform)
            .HasConversion<int>()
            .HasDefaultValue(AccPlatform.Unknown)
            .IsRequired();

        // DocsStatus - stored as int, default Unknown (0)
        builder.Property(e => e.DocsStatus)
            .HasConversion<int>()
            .HasDefaultValue(DocsStatus.Unknown)
            .IsRequired();

        // DocsLastCheckedUtc - optional
        builder.Property(e => e.DocsLastCheckedUtc)
            .HasColumnType("datetime2");

        // DocsLastError - optional, max 500 chars
        builder.Property(e => e.DocsLastError)
            .HasMaxLength(500)
            .IsUnicode(true);

        // === END Platform and Docs Status columns ===

        // Notes - optional, max 500 chars
        builder.Property(e => e.Notes)
            .HasMaxLength(500)
            .IsUnicode(true);

        // CreatedAtUtc - required
        builder.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        // UpdatedAtUtc - required
        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        // Foreign key relationship to Project (restrict delete)
        builder.HasOne(e => e.Project)
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProjectAccMapping_Project");

        // Foreign key relationship to AccHub (restrict delete)
        builder.HasOne(e => e.AccHub)
            .WithMany(h => h.ProjectMappings)
            .HasForeignKey(e => e.AccHubId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProjectAccMapping_AccHub");

        // Indexes and constraints
        builder.HasIndex(e => e.ProjectId)
            .IsUnique()
            .HasDatabaseName("UQ_ProjectAccMapping_ProjectId");

        builder.HasIndex(e => e.AccHubId)
            .HasDatabaseName("IX_ProjectAccMapping_AccHubId");

        builder.HasIndex(e => e.AccProjectId)
            .HasDatabaseName("IX_ProjectAccMapping_AccProjectId");

        // Index on DocsStatus for filtering ready projects
        builder.HasIndex(e => e.DocsStatus)
            .HasDatabaseName("IX_ProjectAccMapping_DocsStatus");
    }
}
