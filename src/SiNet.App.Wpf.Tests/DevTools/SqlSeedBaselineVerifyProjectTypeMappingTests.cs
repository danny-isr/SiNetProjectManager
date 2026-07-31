using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.DevTools;

public sealed class SqlSeedBaselineVerifyProjectTypeMappingTests
{
    [Fact]
    public async Task Verify_when_job_type_lacks_enabled_mapping_then_gap()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JobTypes.Add(new JobType { Id = 1, Title = "סוג בלי מיפוי" });
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = 10,
                Code = "Proposal",
                Name = "Proposal",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var result = await new SqlSeedBaselineVerifyService(factory).VerifyAsync();

        Assert.Contains("סוג בלי מיפוי", result.JobTypesMissingWorkflowMapping);
        Assert.True(result.HasRequiredGaps);
    }

    [Fact]
    public async Task Verify_when_enabled_mapping_exists_then_not_in_gap_list()
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
            db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
            {
                ProjectTypeId = 1,
                WorkflowDefinitionId = 10,
                IsDefault = true,
                IsEnabled = true,
                SortOrder = 1,
            });
            await db.SaveChangesAsync();
        }

        var result = await new SqlSeedBaselineVerifyService(factory).VerifyAsync();

        Assert.DoesNotContain("תכנון", result.JobTypesMissingWorkflowMapping);
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
