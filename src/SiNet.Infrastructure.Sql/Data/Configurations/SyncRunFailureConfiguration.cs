using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for SyncRunFailure entity.
/// Maps to dbo.Sync_RunFailures in the SiData database.
/// </summary>
public class SyncRunFailureConfiguration : IEntityTypeConfiguration<SyncRunFailure>
{
    public void Configure(EntityTypeBuilder<SyncRunFailure> builder)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Table Name
        // ═══════════════════════════════════════════════════════════════════════
        builder.ToTable("Sync_RunFailures", "dbo");

        // ═══════════════════════════════════════════════════════════════════════
        // Primary Key
        // ═══════════════════════════════════════════════════════════════════════
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        // ═══════════════════════════════════════════════════════════════════════
        // Columns
        // ═══════════════════════════════════════════════════════════════════════
        builder.Property(e => e.RunId)
            .IsRequired();

        builder.Property(e => e.StartedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(e => e.FailedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(e => e.MachineName)
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(true);

        builder.Property(e => e.AppVersion)
            .IsRequired()
            .HasMaxLength(50)
            .IsUnicode(true);

        builder.Property(e => e.ErrorStage)
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(true);

        builder.Property(e => e.ErrorType)
            .IsRequired()
            .HasMaxLength(200)
            .IsUnicode(true);

        builder.Property(e => e.ErrorMessage)
            .IsRequired()
            .IsUnicode(true);

        builder.Property(e => e.StackTrace)
            .IsRequired(false)
            .IsUnicode(true);

        // ═══════════════════════════════════════════════════════════════════════
        // Indexes
        // ═══════════════════════════════════════════════════════════════════════
        builder.HasIndex(e => e.RunId)
            .HasDatabaseName("IX_Sync_RunFailures_RunId");

        builder.HasIndex(e => e.FailedAt)
            .IsDescending()
            .HasDatabaseName("IX_Sync_RunFailures_FailedAt");
    }
}
