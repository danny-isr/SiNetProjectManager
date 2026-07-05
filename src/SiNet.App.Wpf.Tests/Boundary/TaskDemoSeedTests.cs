using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Integration and guard tests for <see cref="SqlTaskDemoSeedService"/>.
/// </summary>
public sealed class TaskDemoSeedTests
{
    [Fact]
    public async Task Task_demo_seed_creates_three_bucket_queues_without_unique_index_violation()
    {
        var (options, userId) = await SeedMinimalDatabaseAsync();
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskDemoSeedService(factory);

        var result = await svc.SeedAsync();
        Assert.True(result.Succeeded, result.Summary);

        await using var db = factory.CreateDbContext();
        var openWithPriority = await db.ProjectAssignments
            .Where(t => t.Title != null && t.Title.StartsWith(SqlTaskDemoSeedService.TitlePrefix))
            .Where(t => t.WorkPriority != null)
            .ToListAsync();

        Assert.Equal(10, openWithPriority.Count); // 9 bucket + 1 resolve candidate

        var quick = openWithPriority.Count(t => t.WorkQueueBucket == WorkQueueBucketCodes.Quick);
        var medium = openWithPriority.Count(t => t.WorkQueueBucket == WorkQueueBucketCodes.Medium);
        var longBucket = openWithPriority.Count(t => t.WorkQueueBucket == WorkQueueBucketCodes.Long);
        Assert.Equal(4, quick); // 3 demo + resolve
        Assert.Equal(3, medium);
        Assert.Equal(3, longBucket);

        var identityKeys = openWithPriority
            .Select(t => (t.ProjectId, t.AssignedToId, t.TaskTypeId, t.ParentAssignmentId))
            .ToList();
        Assert.Equal(identityKeys.Count, identityKeys.Distinct().Count());
    }

    [Fact]
    public async Task Task_demo_seed_is_idempotent()
    {
        var (options, _) = await SeedMinimalDatabaseAsync();
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskDemoSeedService(factory);

        var first = await svc.SeedAsync();
        Assert.True(first.Succeeded);

        await using (var db = factory.CreateDbContext())
        {
            var countAfterFirst = await db.ProjectAssignments
                .CountAsync(t => t.Title != null && t.Title.StartsWith(SqlTaskDemoSeedService.TitlePrefix));

            var second = await svc.SeedAsync();
            Assert.True(second.Succeeded);
            Assert.Contains("already present", second.Summary, StringComparison.OrdinalIgnoreCase);

            var countAfterSecond = await db.ProjectAssignments
                .CountAsync(t => t.Title != null && t.Title.StartsWith(SqlTaskDemoSeedService.TitlePrefix));

            Assert.Equal(countAfterFirst, countAfterSecond);
        }
    }

    [Fact]
    public async Task Task_demo_seed_uses_unique_task_types_for_open_demo_tasks()
    {
        var (options, _) = await SeedMinimalDatabaseAsync();
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskDemoSeedService(factory);

        Assert.True((await svc.SeedAsync()).Succeeded);

        await using var db = factory.CreateDbContext();
        var openDemo = await db.ProjectAssignments
            .Include(t => t.TaskType)
            .Where(t => t.Title != null && t.Title.StartsWith(SqlTaskDemoSeedService.TitlePrefix))
            .Where(t => t.WorkPriority != null)
            .ToListAsync();

        var taskTypeIds = openDemo.Select(t => t.TaskTypeId).ToList();
        Assert.Equal(taskTypeIds.Count, taskTypeIds.Distinct().Count());

        foreach (var task in openDemo.Where(t => t.TaskType?.Code?.StartsWith(SqlTaskDemoSeedService.DemoTaskTypeCodePrefix) == true))
        {
            Assert.StartsWith(SqlTaskDemoSeedService.DemoTaskTypeCodePrefix, task.TaskType!.Code, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Task_demo_seed_does_not_modify_unique_open_task_index()
    {
        var snapshot = File.ReadAllText(SnapshotPath);
        var indexBlock = snapshot.Split("IX_ProjectAssignment_UniqueOpenTask")[1];
        Assert.DoesNotContain("WorkQueueBucket", indexBlock.Split("HasFilter")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DevToolsCoordinator_demo_seed_failure_shows_error_not_crash()
    {
        var source = File.ReadAllText(CoordinatorPath);
        Assert.Contains("DbUpdateException", source, StringComparison.Ordinal);
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", source, StringComparison.Ordinal);
        Assert.Contains("!result.Succeeded", source, StringComparison.Ordinal);
        Assert.Contains("ShowError", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_panel_can_load_demo_seeded_tasks_by_bucket()
    {
        var (options, userId) = await SeedMinimalDatabaseAsync();
        var factory = new StubDbContextFactory(options);

        Assert.True((await new SqlTaskDemoSeedService(factory).SeedAsync()).Succeeded);

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSiNetProcessBackbone();
        await using var provider = services.BuildServiceProvider();
        var query = provider.GetRequiredService<ITaskQueryService>();

        var quick = await query.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Quick, CancellationToken.None);
        var medium = await query.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Medium, CancellationToken.None);
        var longBucket = await query.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Long, CancellationToken.None);

        var bucketDemoTitles = SqlTaskDemoSeedService.DemoTaskCatalog
            .Where(d => d.WorkPriority != null)
            .Select(d => d.Title)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, quick.Count(t => t.Title != null && bucketDemoTitles.Contains(t.Title)));
        Assert.Equal(3, medium.Count(t => t.Title != null && bucketDemoTitles.Contains(t.Title)));
        Assert.Equal(3, longBucket.Count(t => t.Title != null && bucketDemoTitles.Contains(t.Title)));
    }

    [Fact]
    public void Task_demo_seed_defines_dedicated_task_type_codes()
    {
        var codes = SqlTaskDemoSeedService.DemoTaskCatalog.Select(d => d.TaskTypeCode).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(codes, c => Assert.StartsWith(SqlTaskDemoSeedService.DemoTaskTypeCodePrefix, c, StringComparison.Ordinal));
        Assert.Contains("DEBUG_TASK_SEED_QUICK_1", codes, StringComparer.Ordinal);
        Assert.Contains("DEBUG_TASK_SEED_MEDIUM_3", codes, StringComparer.Ordinal);
        Assert.Contains("DEBUG_TASK_SEED_LONG_3", codes, StringComparer.Ordinal);
    }

    private static async Task<(DbContextOptions<SiNetSQLDbContext> Options, int UserId)> SeedMinimalDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);

        var user = new Siuser { Name = "DemoUser", IsActive = true };
        db.Siusers.Add(user);

        db.Projects.Add(new Project { Title = "P1", Created = DateTime.UtcNow });

        db.ProjectAssignmentStatuses.AddRange(
            new ProjectAssignmentStatus
            {
                Code = TaskStatusCodes.Open,
                Name = "Open",
                IsOpen = true,
                IsActionable = true,
            },
            new ProjectAssignmentStatus
            {
                Code = TaskStatusCodes.Completed,
                Name = "Completed",
                IsOpen = false,
                IsActionable = false,
            });

        db.TaskTypes.Add(new TaskType
        {
            Code = TaskTypeCodes.FileInitialInquiry,
            Name = "File Initial",
            IsActive = true,
            SortOrder = 102,
        });

        await db.SaveChangesAsync();
        return (options, user.Id);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string SnapshotPath => Path.Combine(
        RepoRoot, "src", "SiNet.Infrastructure.Sql", "Migrations", "SiNetSQLDbContextModelSnapshot.cs");

    private static string CoordinatorPath => Path.Combine(
        RepoRoot, "src", "SiNet.App.Wpf", "DevTools", "DevToolsCoordinator.cs");

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
