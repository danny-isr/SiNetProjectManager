using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Data.Configurations;

internal sealed class ActionPermissionEntityConfiguration : IEntityTypeConfiguration<ActionPermissionEntity>
{
    public void Configure(EntityTypeBuilder<ActionPermissionEntity> entity)
    {
        entity.ToTable("ActionPermission");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("ID");
        entity.Property(e => e.ActionCode).HasMaxLength(100).IsRequired();
        entity.Property(e => e.ActionDisplayName).HasMaxLength(200).IsRequired();
        entity.Property(e => e.CreatedAtUtc).IsRequired();

        entity.HasIndex(e => new { e.ActionCode, e.UserId }, "IX_ActionPermission_ActionCode_UserId")
            .IsUnique();

        entity.HasIndex(e => e.ActionCode, "IX_ActionPermission_ActionCode");
    }
}
