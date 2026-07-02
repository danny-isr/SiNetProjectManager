using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Data.Configurations;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Data;

/// <summary>
/// Minimal EF context for native New System slices. Maps only the entities required by each slice —
/// not the full legacy EF graph (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>).
/// </summary>
public sealed class SiNetDbContext : DbContext
{
    public SiNetDbContext(DbContextOptions<SiNetDbContext> options)
        : base(options)
    {
    }

    public DbSet<SiUserEntity> Users => Set<SiUserEntity>();

    public DbSet<ProjectAssignmentEntity> ProjectAssignments => Set<ProjectAssignmentEntity>();

    public DbSet<ProjectAssignmentStatusEntity> ProjectAssignmentStatuses => Set<ProjectAssignmentStatusEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SiUserEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectAssignmentEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectAssignmentStatusEntityConfiguration());
    }
}
