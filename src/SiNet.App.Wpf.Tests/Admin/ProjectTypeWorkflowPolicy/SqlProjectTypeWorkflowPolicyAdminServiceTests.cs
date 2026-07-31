using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin.ProjectTypeWorkflowPolicy;

public sealed class SqlProjectTypeWorkflowPolicyAdminServiceTests
{
    [Fact]
    public async Task Upsert_then_snapshot_includes_mapping()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JobTypes.Add(new JobType { Id = 1, Title = "תכנון" });
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = 10,
                Code = "PlanningWorkflow",
                Name = "Planning",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var sut = new SqlProjectTypeWorkflowPolicyAdminService(factory);
        var write = await sut.UpsertMappingAsync(1, 10, isDefault: true, isEnabled: true, sortOrder: 1);
        Assert.True(write.Success, write.Error);

        var snapshot = await sut.GetSnapshotAsync();
        Assert.Contains(snapshot.Mappings, m =>
            m.ProjectTypeId == 1 && m.WorkflowDefinitionId == 10 && m.IsDefault && m.IsEnabled);
    }

    [Fact]
    public async Task SetDefault_demotes_previous_default()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JobTypes.Add(new JobType { Id = 1, Title = "תכנון" });
            db.WorkflowDefinitions.AddRange(
                new WorkflowDefinition { Id = 10, Code = "PlanningWorkflow", Name = "Planning", IsActive = true },
                new WorkflowDefinition { Id = 11, Code = "Review", Name = "Review", IsActive = true });
            db.ProjectTypeWorkflowDefinitions.AddRange(
                new ProjectTypeWorkflowDefinition
                {
                    Id = 1,
                    ProjectTypeId = 1,
                    WorkflowDefinitionId = 10,
                    IsDefault = true,
                    IsEnabled = true,
                    SortOrder = 1,
                },
                new ProjectTypeWorkflowDefinition
                {
                    Id = 2,
                    ProjectTypeId = 1,
                    WorkflowDefinitionId = 11,
                    IsDefault = false,
                    IsEnabled = true,
                    SortOrder = 2,
                });
            await db.SaveChangesAsync();
        }

        var sut = new SqlProjectTypeWorkflowPolicyAdminService(factory);
        var result = await sut.SetDefaultAsync(2);
        Assert.True(result.Success, result.Error);

        await using var verify = await factory.CreateDbContextAsync();
        var rows = await verify.ProjectTypeWorkflowDefinitions.AsNoTracking().ToListAsync();
        Assert.False(rows.Single(r => r.Id == 1).IsDefault);
        Assert.True(rows.Single(r => r.Id == 2).IsDefault);
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
