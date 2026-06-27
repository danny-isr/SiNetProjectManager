using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="ActionPermission"/>.
/// Each row grants a specific user access to a specific action type.
/// </summary>
public class ActionPermissionConfiguration : IEntityTypeConfiguration<ActionPermission>
{
    public void Configure(EntityTypeBuilder<ActionPermission> builder)
    {
        builder.ToTable("ActionPermission");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.ActionCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ActionDisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // Each (ActionCode, UserId) pair must be unique
        builder.HasIndex(e => new { e.ActionCode, e.UserId }, "IX_ActionPermission_ActionCode_UserId")
            .IsUnique();

        // Fast lookup by action code
        builder.HasIndex(e => e.ActionCode, "IX_ActionPermission_ActionCode");

        // FK to Siuser
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
