using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Workflow;

/// <summary>
/// Startup upgrade on a private LocalDB database that still has the old JobType
/// title before the first call. It does not connect to DEV or production.
/// </summary>
public sealed class CanonicalJobTypeStartupUpgradeTests
{
    private const string DatabaseName = "SiNet_JobTypeStartupGate";

    private readonly ITestOutputHelper _output;

    public CanonicalJobTypeStartupUpgradeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Startup_normalizes_old_title_then_second_start_does_not_write()
    {
        var options = await CreateDatabaseAsync();
        int legacyId;
        int projectId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            await SeedWorkflowsAsync(db);
            var project = new Project { Title = "לפני נרמול", Number = 3147 };
            var legacy = new JobType { Title = "בדיקה_חוות_דעת" };
            db.Projects.Add(project);
            db.JobTypes.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
            projectId = project.Id;
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
            {
                ProjectId = projectId,
                ProjectTypeId = legacyId,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new SiNetSQLDbContext(options))
        {
            var first = await CanonicalJobTypeStartupUpgrade.RunAsync(db, CancellationToken.None);
            Assert.Equal(CanonicalJobTypeStartupUpgradeOutcome.Upgraded, first.Outcome);
            Assert.Equal(legacyId, first.ReviewJobTypeId);
        }

        await using (var db = new SiNetSQLDbContext(options))
        {
            var review = await db.JobTypes.SingleAsync(j => j.Id == legacyId);
            Assert.Equal("בדיקה", review.Title);
            Assert.Equal(legacyId, (await db.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == projectId)).ProjectTypeId);
            var opinion = await db.JobTypes.SingleAsync(j => j.Title == "חוות דעת");
            Assert.NotEqual(legacyId, opinion.Id);
            var stages = await WizardStageNamesAsync(db, legacyId);
            Assert.NotEmpty(stages);
            _output.WriteLine($"STARTUP id={legacyId} stages={stages.Count} first={string.Join(",", stages.Take(3))}");
        }

        var counter = new SaveCounter();
        var counted = new DbContextOptionsBuilder<SiNetSQLDbContext>(options).AddInterceptors(counter).Options;
        await using (var db = new SiNetSQLDbContext(counted))
        {
            var second = await CanonicalJobTypeStartupUpgrade.RunAsync(db, CancellationToken.None);
            Assert.Equal(CanonicalJobTypeStartupUpgradeOutcome.AlreadyCurrent, second.Outcome);
            Assert.Equal(legacyId, second.ReviewJobTypeId);
        }

        Assert.Equal(0, counter.Calls);
        _output.WriteLine("SECOND_START writes=0");
    }

    [Fact]
    public async Task Startup_conflict_does_not_write_and_does_not_report_success()
    {
        var options = await CreateDatabaseAsync();
        await using (var db = new SiNetSQLDbContext(options))
        {
            await SeedWorkflowsAsync(db);
            var project = new Project { Title = "התנגשות", Number = 20 };
            var spaced = new JobType { Title = "בדיקה חוות דעת" };
            var underscore = new JobType { Title = "בדיקה_חוות_דעת" };
            db.Projects.Add(project);
            db.JobTypes.AddRange(spaced, underscore);
            await db.SaveChangesAsync();
            db.Bids.Add(new Bid { ProjectsId = project.Id, JobTypeId = spaced.Id, BidValue = 10m, BidSubmission = DateTime.UtcNow, Description = "a" });
            db.Bids.Add(new Bid { ProjectsId = project.Id, JobTypeId = underscore.Id, BidValue = 20m, BidSubmission = DateTime.UtcNow, Description = "b" });
            await db.SaveChangesAsync();
        }

        await using var verify = new SiNetSQLDbContext(options);
        var result = await CanonicalJobTypeStartupUpgrade.RunAsync(verify, CancellationToken.None);
        Assert.Equal(CanonicalJobTypeStartupUpgradeOutcome.Blocked, result.Outcome);
        Assert.Contains("לא שונה דבר", result.Message, StringComparison.Ordinal);
        verify.ChangeTracker.Clear();
        Assert.Equal(2, await verify.JobTypes.CountAsync(j => j.Title != null && j.Title.Contains("בדיקה")));
        Assert.False(await verify.JobTypes.AnyAsync(j => j.Title == "בדיקה"));
        Assert.Equal(0, await verify.ProjectTypeWorkflowDefinitions.CountAsync());
        _output.WriteLine("CONFLICT " + result.Message.ReplaceLineEndings(" "));
    }

    [Fact]
    public async Task Startup_write_failure_rolls_back_and_does_not_report_success()
    {
        var options = await CreateDatabaseAsync();
        await using (var db = new SiNetSQLDbContext(options))
        {
            await SeedWorkflowsAsync(db);
            db.JobTypes.Add(new JobType { Title = "בדיקה_חוות_דעת" });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR ALTER TRIGGER TR_Startup_BlockMapping ON ProjectTypeWorkflowDefinition AFTER INSERT
                AS
                BEGIN
                    THROW 50001, 'startup proof block mapping insert', 1;
                END
                """);
        }

        await using (var db = new SiNetSQLDbContext(options))
        {
            var result = await CanonicalJobTypeStartupUpgrade.RunAsync(db, CancellationToken.None);
            Assert.Equal(CanonicalJobTypeStartupUpgradeOutcome.Failed, result.Outcome);
            Assert.Contains("השינוי בוטל", result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("נורמל", result.Message, StringComparison.Ordinal);
        }

        await using var verify = new SiNetSQLDbContext(options);
        Assert.Equal("בדיקה_חוות_דעת", (await verify.JobTypes.SingleAsync()).Title);
        Assert.Equal(0, await verify.ProjectTypeWorkflowDefinitions.CountAsync());
        await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_Startup_BlockMapping");
        _output.WriteLine("WRITE_FAILURE rolled back");
    }

    [Fact]
    public async Task Two_concurrent_startups_do_not_race()
    {
        var options = await CreateDatabaseAsync();
        int legacyId;
        int projectId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            await SeedWorkflowsAsync(db);
            var project = new Project { Title = "מקביל", Number = 7 };
            var legacy = new JobType { Title = "בדיקה חוות דעת" };
            db.Projects.Add(project);
            db.JobTypes.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
            projectId = project.Id;
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject { ProjectId = projectId, ProjectTypeId = legacyId });
            await db.SaveChangesAsync();
        }

        var first = Task.Run(() => RunIsolatedAsync(options));
        var second = Task.Run(() => RunIsolatedAsync(options));
        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, results.Count(r => r.Outcome == CanonicalJobTypeStartupUpgradeOutcome.Upgraded));
        Assert.Equal(1, results.Count(r => r.Outcome == CanonicalJobTypeStartupUpgradeOutcome.AlreadyCurrent));
        Assert.DoesNotContain(results, r => r.Outcome is CanonicalJobTypeStartupUpgradeOutcome.Failed
            or CanonicalJobTypeStartupUpgradeOutcome.Blocked);

        await using var verify = new SiNetSQLDbContext(options);
        Assert.Equal("בדיקה", (await verify.JobTypes.SingleAsync(j => j.Id == legacyId)).Title);
        Assert.Equal(1, await verify.JobTypes.CountAsync(j => j.Title == "בדיקה"));
        Assert.Equal(legacyId, (await verify.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == projectId)).ProjectTypeId);
        Assert.NotEmpty(await WizardStageNamesAsync(verify, legacyId));
        _output.WriteLine("CONCURRENT " + string.Join(" | ", results.Select(r => r.Outcome.ToString())));
    }

    private static async Task<CanonicalJobTypeStartupUpgradeResult> RunIsolatedAsync(
        DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);
        return await CanonicalJobTypeStartupUpgrade.RunAsync(db, CancellationToken.None);
    }

    private static async Task<List<string>> WizardStageNamesAsync(SiNetSQLDbContext db, int jobTypeId)
    {
        return await db.WorkflowStageDefinitions.AsNoTracking()
            .Where(s => s.WorkflowDefinition.Code == WorkflowCodes.Review)
            .Where(s => s.NodeType != "Start" && s.NodeType != "SubWorkflow" && !s.IsFinal)
            .Where(s => s.Code != null && s.Code != "")
            .Where(s => db.ProjectTypeWorkflowStages.Any(p =>
                p.ProjectTypeId == jobTypeId && p.WorkflowStageDefinitionId == s.Id && p.IsActive))
            .OrderBy(s => s.SortOrder)
            .Select(s => s.Name ?? s.Code!)
            .ToListAsync();
    }

    private static async Task SeedWorkflowsAsync(SiNetSQLDbContext db)
    {
        foreach (var (code, name, stages) in new[]
        {
            (WorkflowCodes.Review, "תהליך בדיקת תוכנית", ReviewWorkflowSeedData.Stages),
            (WorkflowCodes.Opinion, "תהליך חוות דעת", OpinionWorkflowSeedData.Stages),
        })
        {
            var definition = new WorkflowDefinition
            {
                Code = code,
                Name = name,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
            foreach (var stage in stages)
            {
                db.WorkflowStageDefinitions.Add(new WorkflowStageDefinition
                {
                    WorkflowDefinitionId = definition.Id,
                    Code = stage.Code,
                    Name = stage.Name,
                    SortOrder = stage.SortOrder,
                    NodeType = "Stage",
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<DbContextOptions<SiNetSQLDbContext>> CreateDatabaseAsync()
    {
        try
        {
            await ResetAsync();
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "LocalDB is required for the release SQL gate and was not available. " + ex.Message,
                ex);
        }

        var cs = $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseSqlServer(cs).Options;
        await using var db = new SiNetSQLDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return options;
    }

    private static async Task ResetAsync()
    {
        var master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using var conn = new SqlConnection(master);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            CREATE DATABASE [{DatabaseName}];
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Calls { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Calls++;
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
