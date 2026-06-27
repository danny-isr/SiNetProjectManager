using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for AccSystemResource entity.
/// </summary>
public class AccSystemResourceConfiguration : IEntityTypeConfiguration<AccSystemResource>
{
    public void Configure(EntityTypeBuilder<AccSystemResource> builder)
    {
        // Table name
        builder.ToTable("AccSystemResource");

        // Primary key - string Key
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key)
            .HasMaxLength(50)
            .IsUnicode(true);

        // AccHubId - required FK
        builder.Property(e => e.AccHubId)
            .IsRequired();

        // AccProjectId - optional, max 100 chars
        builder.Property(e => e.AccProjectId)
            .HasMaxLength(100)
            .IsUnicode(true);

        // AccRootFolderId - optional, max 200 chars
        builder.Property(e => e.AccRootFolderId)
            .HasMaxLength(200)
            .IsUnicode(true);

        // AccInboxFolderId - optional, max 200 chars
        builder.Property(e => e.AccInboxFolderId)
            .HasMaxLength(200)
            .IsUnicode(true);

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

        // Foreign key relationship to AccHub (restrict delete)
        builder.HasOne(e => e.AccHub)
            .WithMany(h => h.SystemResources)
            .HasForeignKey(e => e.AccHubId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_AccSystemResource_AccHub");

        // Indexes
        builder.HasIndex(e => e.AccHubId)
            .HasDatabaseName("IX_AccSystemResource_AccHubId");
    }
}
