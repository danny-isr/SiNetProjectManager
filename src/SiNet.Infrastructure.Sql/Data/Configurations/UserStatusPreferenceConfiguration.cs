using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for UserStatusPreference entity.
/// Maps to dbo.UserStatusPreference — stores per-user status color overrides.
/// </summary>
public class UserStatusPreferenceConfiguration : IEntityTypeConfiguration<UserStatusPreference>
{
    public void Configure(EntityTypeBuilder<UserStatusPreference> builder)
    {
        builder.ToTable("UserStatusPreference");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("ID");

        builder.Property(e => e.SiuserId)
            .HasColumnName("SIUserID");

        builder.Property(e => e.StatusId)
            .HasColumnName("StatusID");

        builder.Property(e => e.OverrideColorHex)
            .IsRequired()
            .HasMaxLength(9);

        // Unique constraint: one override per user per status
        builder.HasIndex(e => new { e.SiuserId, e.StatusId }, "IX_UserStatusPreference_User_Status")
            .IsUnique();

        // FK to Siuser (cascade delete — if user is deleted, overrides go too)
        builder.HasOne(d => d.Siuser)
            .WithMany(p => p.UserStatusPreferences)
            .HasForeignKey(d => d.SiuserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_UserStatusPreference_SIUser");

        // FK to ProjectAssignmentStatus (cascade delete — if status is deleted, overrides go too)
        builder.HasOne(d => d.Status)
            .WithMany(p => p.UserStatusPreferences)
            .HasForeignKey(d => d.StatusId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_UserStatusPreference_ProjectAssignmentStatus");
    }
}
