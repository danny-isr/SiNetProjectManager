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

        // Prefer unique LoginName (operator migration). Concurrent registration also uses
        // sp_getapplock + DbUpdateException re-read — see SqlWindowsCurrentUserAuthenticator.
        // Do not declare HasIndex here until the operator applies SIUser_LoginName_Unique,
        // otherwise the schema gate can fail on DEV before the migration exists.
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
