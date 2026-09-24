using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Workflow;

/// <summary>
/// Upgrade proof on a private LocalDB database. It inserts the pre-normalization
/// JobType rows itself and calls <see cref="CanonicalJobTypeReconciliation"/> before
/// any normalizing seed. It does not connect to the DEV or production catalog.
/// </summary>
public sealed class CanonicalJobTypeSqlUpgradeTests
{
    private const string DatabaseName = "SiNet_JobTypeUpgradeProof";

    private readonly ITestOutputHelper _output;

    public CanonicalJobTypeSqlUpgradeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Old_jobtype_rows_upgrade_on_sql_server_or_roll_back()
    {
        var cs = $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
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

        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseSqlServer(cs).Options;
        await using (var db = new SiNetSQLDbContext(options))
            await db.Database.EnsureCreatedAsync();

        await SeedWorkflowDefinitionsAsync(options);
        await ProveLegacyPairConflictsWhileCanonicalExistsAsync(options);
        await ProveTwoLegacyNamesConflictAsync(options);
        var renamedId = await ProveRenameKeepsIdAsync(options);
        await ProveCleanMergeCopiesStatusAndTaskTypeAsync(options, renamedId);
        await ProveMappingFailureRollsBackRenameAsync(options);
        await ProveSecondApplyIsStableAsync(options);
        _output.WriteLine("SQL_UPGRADE_OK database=" + DatabaseName);
    }

    private async Task ProveLegacyPairConflictsWhileCanonicalExistsAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        int keeperId;
        int spacedId;
        int underscoreId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var project = await AddProjectAsync(db, 9);
            var keeper = new JobType { Title = "בדיקה" };
            var spaced = new JobType { Title = "בדיקה חוות דעת" };
            var underscore = new JobType { Title = "בדיקה_חוות_דעת" };
            db.JobTypes.AddRange(keeper, spaced, underscore);
            await db.SaveChangesAsync();
            keeperId = keeper.Id;
            spacedId = spaced.Id;
            underscoreId = underscore.Id;
            db.Bids.Add(new Bid { ProjectsId = project.Id, JobTypeId = spacedId, BidValue = 1000m, BidSubmission = DateTime.UtcNow, Description = "spaced only" });
            db.Bids.Add(new Bid { ProjectsId = project.Id, JobTypeId = underscoreId, BidValue = 2500m, BidSubmission = DateTime.UtcNow, Description = "underscore only" });
            await db.SaveChangesAsync();
        }

        await using var verify = new SiNetSQLDbContext(options);
        var preview = await CanonicalJobTypeReconciliation.PreviewAsync(verify, CancellationToken.None);
        Assert.Contains(preview.Conflicts, c => c.Contains("Conflict", StringComparison.Ordinal));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CanonicalJobTypeReconciliation.ApplyAsync(verify, CancellationToken.None));
        verify.ChangeTracker.Clear();
        Assert.Equal("בדיקה", (await verify.JobTypes.SingleAsync(j => j.Id == keeperId)).Title);
        Assert.Equal("בדיקה חוות דעת", (await verify.JobTypes.SingleAsync(j => j.Id == spacedId)).Title);
        Assert.Equal("בדיקה_חוות_דעת", (await verify.JobTypes.SingleAsync(j => j.Id == underscoreId)).Title);
        Assert.Equal(1000m, (await verify.Bids.SingleAsync(b => b.JobTypeId == spacedId)).BidValue);
        Assert.Equal(2500m, (await verify.Bids.SingleAsync(b => b.JobTypeId == underscoreId)).BidValue);
        _output.WriteLine("LEGACY-PAIR-WITH-CANONICAL blocked: " + thrown.Message);

        verify.Bids.RemoveRange(verify.Bids);
        verify.JobTypes.RemoveRange(verify.JobTypes);
        await verify.SaveChangesAsync();
    }

    private async Task ProveTwoLegacyNamesConflictAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        int spacedId;
        int underscoreId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var project = await AddProjectAsync(db, 1);
            var spaced = new JobType { Title = "בדיקה חוות דעת" };
            var underscore = new JobType { Title = "בדיקה_חוות_דעת" };
            db.JobTypes.AddRange(spaced, underscore);
            await db.SaveChangesAsync();
            spacedId = spaced.Id;
            underscoreId = underscore.Id;
            db.Bids.Add(new Bid { ProjectsId = project.Id, JobTypeId = spacedId, BidValue = 1000m, BidSubmission = DateTime.UtcNow, Description = "spaced" });
            db.Bids.Add(new Bid { ProjectsId = project.Id, JobTypeId = underscoreId, BidValue = 2500m, BidSubmission = DateTime.UtcNow, Description = "underscore" });
            await db.SaveChangesAsync();
        }

        await using var verify = new SiNetSQLDbContext(options);
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CanonicalJobTypeReconciliation.ApplyAsync(verify, CancellationToken.None));
        Assert.Contains("Conflict", thrown.Message, StringComparison.Ordinal);
        verify.ChangeTracker.Clear();
        Assert.Equal("בדיקה חוות דעת", (await verify.JobTypes.SingleAsync(j => j.Id == spacedId)).Title);
        Assert.Equal("בדיקה_חוות_דעת", (await verify.JobTypes.SingleAsync(j => j.Id == underscoreId)).Title);
        Assert.Equal(2, await verify.Bids.CountAsync());
        _output.WriteLine("TWO-LEGACY blocked: " + thrown.Message);

        verify.Bids.RemoveRange(verify.Bids);
        verify.JobTypes.RemoveRange(verify.JobTypes);
        await verify.SaveChangesAsync();
    }

    private async Task<int> ProveRenameKeepsIdAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        int legacyId;
        int projectId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var project = await AddProjectAsync(db, 2);
            projectId = project.Id;
            var legacy = new JobType { Title = "בדיקה_חוות_דעת" };
            db.JobTypes.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject { ProjectId = projectId, ProjectTypeId = legacyId });
            await db.SaveChangesAsync();
            await CanonicalJobTypeReconciliation.ApplyAsync(db, CancellationToken.None);
        }

        await using var verify = new SiNetSQLDbContext(options);
        var review = await verify.JobTypes.SingleAsync(j => j.Title == "בדיקה");
        Assert.Equal(legacyId, review.Id);
        Assert.Equal(legacyId, (await verify.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == projectId)).ProjectTypeId);
        Assert.False(await verify.JobTypes.AnyAsync(j => j.Title == "בדיקה_חוות_דעת"));
        _output.WriteLine($"RENAME id={legacyId} links=1");
        return legacyId;
    }

    private async Task ProveCleanMergeCopiesStatusAndTaskTypeAsync(DbContextOptions<SiNetSQLDbContext> options, int keeperId)
    {
        await using var db = new SiNetSQLDbContext(options);
        var extra = new JobType { Title = "בדיקה חוות דעת" };
        db.JobTypes.Add(extra);
        await db.SaveChangesAsync();
        var status = new ProjectAssignmentStatus { Code = "OpenUpgrade", Name = "פתוח", IsActive = true, IsOpen = true, IsActionable = true, SortOrder = 10 };
        var taskType = new TaskType { Code = "UpgradeTask", Name = "משימה", IsActive = true };
        db.ProjectAssignmentStatuses.Add(status);
        db.TaskTypes.Add(taskType);
        await db.SaveChangesAsync();
        db.ProjectTypeStatuses.Add(new ProjectTypeStatus { ProjectTypeId = extra.Id, StatusId = status.Id });
        db.ProjectTypeTaskTypes.Add(new ProjectTypeTaskType { ProjectTypeId = extra.Id, TaskTypeId = taskType.Id });
        await db.SaveChangesAsync();
        await CanonicalJobTypeReconciliation.ApplyAsync(db, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.False(await db.JobTypes.AnyAsync(j => j.Title == "בדיקה חוות דעת"));
        var statusId = (await db.ProjectAssignmentStatuses.SingleAsync(s => s.Code == "OpenUpgrade")).Id;
        var taskTypeId = (await db.TaskTypes.SingleAsync(t => t.Code == "UpgradeTask")).Id;
        Assert.Equal(keeperId, (await db.ProjectTypeStatuses.SingleAsync()).ProjectTypeId);
        Assert.Equal(statusId, (await db.ProjectTypeStatuses.SingleAsync()).StatusId);
        Assert.Equal(keeperId, (await db.ProjectTypeTaskTypes.SingleAsync()).ProjectTypeId);
        Assert.Equal(taskTypeId, (await db.ProjectTypeTaskTypes.SingleAsync()).TaskTypeId);
        _output.WriteLine($"MERGE keeper={keeperId} status={statusId} taskType={taskTypeId}");
    }

    private async Task ProveMappingFailureRollsBackRenameAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using (var db = new SiNetSQLDbContext(options))
        {
            var review = await db.JobTypes.SingleAsync(j => j.Title == "בדיקה");
            review.Title = "בדיקה_חוות_דעת";
            db.ProjectTypeWorkflowDefinitions.RemoveRange(db.ProjectTypeWorkflowDefinitions);
            db.ProjectTypeWorkflowStages.RemoveRange(db.ProjectTypeWorkflowStages);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR ALTER TRIGGER TR_Upgrade_BlockMapping ON ProjectTypeWorkflowDefinition AFTER INSERT
                AS
                BEGIN
                    THROW 50001, 'proof block mapping insert', 1;
                END
                """);
        }

        await using (var db = new SiNetSQLDbContext(options))
        {
            var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
                CanonicalJobTypeReconciliation.ApplyAsync(db, CancellationToken.None));
            _output.WriteLine("ROLLBACK error=" + thrown.GetType().Name);
        }

        await using var verify = new SiNetSQLDbContext(options);
        Assert.Equal("בדיקה_חוות_דעת", (await verify.JobTypes.SingleAsync(j => j.Title != null && j.Title.Contains("בדיקה"))).Title);
        Assert.Equal(0, await verify.ProjectTypeWorkflowDefinitions.CountAsync());
        await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_Upgrade_BlockMapping");
        _output.WriteLine("ROLLBACK title restored and mappings=0");
    }

    private async Task ProveSecondApplyIsStableAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);
        await CanonicalJobTypeReconciliation.ApplyAsync(db, CancellationToken.None);
        var first = await SnapshotAsync(db);
        await CanonicalJobTypeReconciliation.ApplyAsync(db, CancellationToken.None);
        var second = await SnapshotAsync(db);
        Assert.Equal(first, second);
        _output.WriteLine("SECOND " + second);
    }

    private static async Task SeedWorkflowDefinitionsAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);
        foreach (var (code, name, stages) in new[]
        {
            (WorkflowCodes.Review, "תהליך בדיקת תוכנית", ReviewWorkflowSeedData.Stages),
            (WorkflowCodes.Opinion, "תהליך חוות דעת", OpinionWorkflowSeedData.Stages),
        })
        {
            var definition = new WorkflowDefinition { Code = code, Name = name, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
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

    private static async Task<Project> AddProjectAsync(SiNetSQLDbContext db, int number)
    {
        var project = new Project { Title = "upgrade " + number, Number = number };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<string> SnapshotAsync(SiNetSQLDbContext db)
    {
        var jobs = await db.JobTypes.CountAsync();
        var maps = await db.ProjectTypeWorkflowDefinitions.CountAsync();
        var stages = await db.ProjectTypeWorkflowStages.CountAsync();
        return $"jobs={jobs} maps={maps} stages={stages}";
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
}
