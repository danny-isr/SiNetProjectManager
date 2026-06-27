using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

public class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> builder)
    {
        builder.ToTable("UserGroups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(g => g.Code).IsUnique();

        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.IsActive).HasDefaultValue(true);

        builder.Property(g => g.DefaultAssigneeId).HasColumnName("DefaultAssigneeID");
        builder.HasOne(g => g.DefaultAssignee)
            .WithMany()
            .HasForeignKey(g => g.DefaultAssigneeId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_UserGroups_DefaultAssignee");
    }
}

public class UserGroupMembershipConfiguration : IEntityTypeConfiguration<UserGroupMembership>
{
    public void Configure(EntityTypeBuilder<UserGroupMembership> builder)
    {
        builder.ToTable("UserGroupMemberships");
        builder.HasKey(m => m.Id);

        // Unique: a user can belong to a group only once
        builder.HasIndex(m => new { m.SiuserId, m.UserGroupId }).IsUnique();

        builder.HasOne(m => m.Siuser)
            .WithMany(u => u.GroupMemberships)
            .HasForeignKey(m => m.SiuserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.UserGroup)
            .WithMany(g => g.Memberships)
            .HasForeignKey(m => m.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
