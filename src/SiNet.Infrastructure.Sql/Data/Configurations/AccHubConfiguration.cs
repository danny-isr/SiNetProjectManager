using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for AccHub entity.
/// </summary>
public class AccHubConfiguration : IEntityTypeConfiguration<AccHub>
{
    public void Configure(EntityTypeBuilder<AccHub> builder)
    {
        // Table name
        builder.ToTable("AccHub");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        // HubId - required, max 100 chars
        builder.Property(e => e.HubId)
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(true);

        // DisplayName - optional, max 200 chars
        builder.Property(e => e.DisplayName)
            .HasMaxLength(200)
            .IsUnicode(true);

        // IsDefault - required, default false
        builder.Property(e => e.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        // CreatedAtUtc - required
        builder.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        // UpdatedAtUtc - required
        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        // Indexes
        builder.HasIndex(e => e.HubId)
            .IsUnique()
            .HasDatabaseName("UQ_AccHub_HubId");

        builder.HasIndex(e => e.IsDefault)
            .HasDatabaseName("IX_AccHub_IsDefault");
    }
}
