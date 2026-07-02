using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Data.Configurations;

internal sealed class SiUserEntityConfiguration : IEntityTypeConfiguration<SiUserEntity>
{
    public void Configure(EntityTypeBuilder<SiUserEntity> entity)
    {
        entity.ToTable("SIUser");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("ID");
        entity.Property(e => e.Email).HasMaxLength(255).IsUnicode(false);
        entity.Property(e => e.LoginName).HasMaxLength(255).IsUnicode(false);
        entity.Property(e => e.Name).HasMaxLength(255).IsUnicode(false);
        entity.Property(e => e.Notes).IsUnicode(false);
        entity.Property(e => e.IsActive).HasDefaultValue(true);
        entity.Property(e => e.Role).HasDefaultValue(1);
        entity.Property(e => e.AccUserType).HasDefaultValue(0);

        entity.HasIndex(e => e.MasterPlanEmployeeId)
            .IsUnique()
            .HasFilter("[MasterPlanEmployeeId] IS NOT NULL");
    }
}

internal sealed class ProjectAssignmentEntityConfiguration : IEntityTypeConfiguration<ProjectAssignmentEntity>
{
    public void Configure(EntityTypeBuilder<ProjectAssignmentEntity> entity)
    {
        entity.ToTable("ProjectAssignment");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("ID");
        entity.Property(e => e.AssignedToId).HasColumnName("AssignedToID");
        entity.Property(e => e.StatusId).HasColumnName("StatusID");

        entity.HasOne(e => e.AssignmentStatus)
            .WithMany(s => s.ProjectAssignments)
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ProjectAssignmentStatusEntityConfiguration : IEntityTypeConfiguration<ProjectAssignmentStatusEntity>
{
    public void Configure(EntityTypeBuilder<ProjectAssignmentStatusEntity> entity)
    {
        entity.ToTable("ProjectAssignmentStatus");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("ID");
    }
}
