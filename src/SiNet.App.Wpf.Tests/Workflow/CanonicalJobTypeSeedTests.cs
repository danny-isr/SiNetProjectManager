using Microsoft.EntityFrameworkCore;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class CanonicalJobTypeSeedTests
{
    [Fact]
    public async Task Legacy_review_jobtype_is_renamed_in_place_and_mapped_to_review()
    {
        var (factory, options) = CreateFactory();
        int legacyId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var jobType = new JobType { Title = "בדיקה חוות דעת" };
            db.JobTypes.Add(jobType);
            await db.SaveChangesAsync();
            legacyId = jobType.Id;
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
            {
                ProjectId = 44,
                ProjectTypeId = legacyId,
            });
            await db.SaveChangesAsync();
        }

        await new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None);

        await using var verify = new SiNetSQLDbContext(options);
        var reviewType = await verify.JobTypes.SingleAsync(j => j.Title == "בדיקה");
        Assert.Equal(legacyId, reviewType.Id);
        Assert.False(await verify.JobTypes.AnyAsync(j => j.Title == "בדיקה חוות דעת"));
        Assert.Equal(legacyId, (await verify.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == 44)).ProjectTypeId);
        await AssertReviewMappingAsync(verify, reviewType.Id);
        await AssertOpinionAsync(verify);
    }

    [Fact]
    public async Task Underscore_legacy_title_is_the_same_row_after_seed()
    {
        var (factory, options) = CreateFactory();
        int legacyId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var jobType = new JobType { Title = "בדיקה_חוות_דעת" };
            db.JobTypes.Add(jobType);
            await db.SaveChangesAsync();
            legacyId = jobType.Id;
        }

        await new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None);

        await using var verify = new SiNetSQLDbContext(options);
        var reviewType = await verify.JobTypes.SingleAsync(j => j.Title == "בדיקה");
        Assert.Equal(legacyId, reviewType.Id);
    }

    [Fact]
    public async Task Missing_opinion_jobtype_is_created_with_opinion_profile()
    {
        var (factory, options) = CreateFactory();
        await new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None);

        await using var verify = new SiNetSQLDbContext(options);
        await AssertOpinionAsync(verify);
    }

    [Fact]
    public async Task Already_normalized_jobtypes_stay_stable()
    {
        var (factory, options) = CreateFactory();
        await using (var db = new SiNetSQLDbContext(options))
        {
            db.JobTypes.Add(new JobType { Title = "בדיקה" });
            db.JobTypes.Add(new JobType { Title = "חוות דעת" });
            await db.SaveChangesAsync();
        }

        var seed = new SqlWorkflowSeedService(factory);
        await seed.SeedAllAsync(CancellationToken.None);
        var afterFirst = await SnapshotAsync(options);
        await seed.SeedAllAsync(CancellationToken.None);
        var afterSecond = await SnapshotAsync(options);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(1, afterSecond.ReviewJobTypes);
        Assert.Equal(1, afterSecond.OpinionJobTypes);
    }

    [Fact]
    public async Task Conflicting_review_titles_merge_without_duplicate_title()
    {
        var (factory, options) = CreateFactory();
        int keeperId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var keeper = new JobType { Title = "בדיקה" };
            var legacy = new JobType { Title = "בדיקה חוות דעת" };
            db.JobTypes.AddRange(keeper, legacy);
            await db.SaveChangesAsync();
            keeperId = keeper.Id;
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
            {
                ProjectId = 7,
                ProjectTypeId = legacy.Id,
            });
            await db.SaveChangesAsync();
        }

        await new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None);

        await using var verify = new SiNetSQLDbContext(options);
        var reviewTypes = await verify.JobTypes.Where(j => j.Title == "בדיקה").ToListAsync();
        Assert.Single(reviewTypes);
        Assert.Equal(keeperId, reviewTypes[0].Id);
        Assert.False(await verify.JobTypes.AnyAsync(j => j.Title == "בדיקה חוות דעת"));
        Assert.Equal(keeperId, (await verify.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == 7)).ProjectTypeId);
    }

    [Fact]
    public async Task Two_legacy_titles_conflict_with_each_other_even_when_review_exists()
    {
        var (_, options) = CreateFactory();
        int keeperId;
        int spacedId;
        int underscoreId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var keeper = new JobType { Title = "בדיקה" };
            var spaced = new JobType { Title = "בדיקה חוות דעת" };
            var underscore = new JobType { Title = "בדיקה_חוות_דעת" };
            db.JobTypes.AddRange(keeper, spaced, underscore);
            await db.SaveChangesAsync();
            keeperId = keeper.Id;
            spacedId = spaced.Id;
            underscoreId = underscore.Id;
            db.Bids.Add(new Bid
            {
                ProjectsId = 11,
                JobTypeId = spacedId,
                BidValue = 1000m,
                BidSubmission = new DateTime(2024, 1, 1),
                Description = "spaced",
            });
            db.Bids.Add(new Bid
            {
                ProjectsId = 11,
                JobTypeId = underscoreId,
                BidValue = 2500m,
                BidSubmission = new DateTime(2024, 2, 1),
                Description = "underscore",
            });
            await db.SaveChangesAsync();
        }

        await using var verify = new SiNetSQLDbContext(options);
        var preview = await CanonicalJobTypeReconciliation.PreviewAsync(verify, CancellationToken.None);
        Assert.Contains(preview.Conflicts, c => c.Contains("Conflict", StringComparison.Ordinal));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CanonicalJobTypeReconciliation.ApplyAsync(verify, CancellationToken.None));
        Assert.Contains("No rows were changed", thrown.Message, StringComparison.Ordinal);
        verify.ChangeTracker.Clear();
        Assert.Equal("בדיקה", (await verify.JobTypes.SingleAsync(j => j.Id == keeperId)).Title);
        Assert.Equal("בדיקה חוות דעת", (await verify.JobTypes.SingleAsync(j => j.Id == spacedId)).Title);
        Assert.Equal("בדיקה_חוות_דעת", (await verify.JobTypes.SingleAsync(j => j.Id == underscoreId)).Title);
        Assert.Equal(1000m, (await verify.Bids.SingleAsync(b => b.JobTypeId == spacedId)).BidValue);
        Assert.Equal(2500m, (await verify.Bids.SingleAsync(b => b.JobTypeId == underscoreId)).BidValue);
    }

    [Fact]
    public async Task Conflicting_bids_are_kept_and_the_merge_does_not_change_either_jobtype()
    {
        var (factory, options) = CreateFactory();
        int keeperId;
        int legacyId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var keeper = new JobType { Title = "בדיקה" };
            var legacy = new JobType { Title = "בדיקה חוות דעת" };
            db.JobTypes.AddRange(keeper, legacy);
            await db.SaveChangesAsync();
            keeperId = keeper.Id;
            legacyId = legacy.Id;
            db.Bids.Add(new Bid
            {
                ProjectsId = 9,
                JobTypeId = keeperId,
                BidValue = 1000m,
                BidSubmission = new DateTime(2024, 1, 1),
                Description = "keeper quote",
            });
            db.Bids.Add(new Bid
            {
                ProjectsId = 9,
                JobTypeId = legacyId,
                BidValue = 2500m,
                BidSubmission = new DateTime(2024, 2, 1),
                Description = "legacy quote",
            });
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
            {
                ProjectId = 9,
                ProjectTypeId = legacyId,
            });
            await db.SaveChangesAsync();
        }

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None).AsTask());
        Assert.Contains("Conflict", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("No rows were changed", thrown.Message, StringComparison.Ordinal);

        await using var verify = new SiNetSQLDbContext(options);
        Assert.Equal(2, await verify.Bids.CountAsync(b => b.ProjectsId == 9));
        Assert.Equal(1000m, (await verify.Bids.SingleAsync(b => b.JobTypeId == keeperId)).BidValue);
        Assert.Equal(2500m, (await verify.Bids.SingleAsync(b => b.JobTypeId == legacyId)).BidValue);
        Assert.True(await verify.JobTypes.AnyAsync(j => j.Id == legacyId && j.Title == "בדיקה חוות דעת"));
        Assert.Equal(legacyId, (await verify.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == 9)).ProjectTypeId);
    }

    [Fact]
    public async Task Status_mapping_that_exists_only_on_the_legacy_jobtype_is_copied_not_rekeyed()
    {
        var (factory, options) = CreateFactory();
        int keeperId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var keeper = new JobType { Title = "בדיקה" };
            var legacy = new JobType { Title = "בדיקה_חוות_דעת" };
            db.JobTypes.AddRange(keeper, legacy);
            await db.SaveChangesAsync();
            keeperId = keeper.Id;
            db.ProjectTypeStatuses.Add(new ProjectTypeStatus { ProjectTypeId = legacy.Id, StatusId = 4 });
            db.ProjectTypeTaskTypes.Add(new ProjectTypeTaskType { ProjectTypeId = legacy.Id, TaskTypeId = 6 });
            await db.SaveChangesAsync();
        }

        var seed = new SqlWorkflowSeedService(factory);
        await seed.SeedAllAsync(CancellationToken.None);
        await seed.SeedAllAsync(CancellationToken.None);

        await using var verify = new SiNetSQLDbContext(options);
        Assert.False(await verify.JobTypes.AnyAsync(j => j.Title == "בדיקה_חוות_דעת"));
        var status = await verify.ProjectTypeStatuses.SingleAsync();
        Assert.Equal(keeperId, status.ProjectTypeId);
        Assert.Equal(4, status.StatusId);
        var taskType = await verify.ProjectTypeTaskTypes.SingleAsync();
        Assert.Equal(keeperId, taskType.ProjectTypeId);
        Assert.Equal(6, taskType.TaskTypeId);
    }

    [Fact]
    public async Task Active_workflow_clash_leaves_both_jobtypes_and_their_links_unchanged()
    {
        var (factory, options) = CreateFactory();
        int keeperId;
        int legacyId;
        await using (var db = new SiNetSQLDbContext(options))
        {
            var keeper = new JobType { Title = "בדיקה" };
            var legacy = new JobType { Title = "בדיקה חוות דעת" };
            db.JobTypes.AddRange(keeper, legacy);
            await db.SaveChangesAsync();
            keeperId = keeper.Id;
            legacyId = legacy.Id;
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject { ProjectId = 3, ProjectTypeId = legacyId });
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                ProjectId = 3,
                WorkflowDefinitionId = 2,
                JobTypeId = keeperId,
                Status = WorkflowStatus.Active,
                IsProjectBound = true,
                CreatedByUserId = 1,
                CreatedAtUtc = DateTime.UtcNow,
            });
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                ProjectId = 3,
                WorkflowDefinitionId = 2,
                JobTypeId = legacyId,
                Status = WorkflowStatus.Active,
                IsProjectBound = true,
                CreatedByUserId = 1,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None).AsTask());
        Assert.Contains("Conflict", thrown.Message, StringComparison.Ordinal);

        await using var verify = new SiNetSQLDbContext(options);
        Assert.True(await verify.JobTypes.AnyAsync(j => j.Id == legacyId && j.Title == "בדיקה חוות דעת"));
        Assert.Equal(legacyId, (await verify.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == 3)).ProjectTypeId);
        Assert.Equal(keeperId, (await verify.WorkflowInstances.SingleAsync(i => i.JobTypeId == keeperId)).JobTypeId);
        Assert.Equal(legacyId, (await verify.WorkflowInstances.SingleAsync(i => i.JobTypeId == legacyId)).JobTypeId);
    }

    private static (ProposalWorkflowHarness.StubDbContextFactory Factory, DbContextOptions<SiNetSQLDbContext> Options) CreateFactory()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return (new ProposalWorkflowHarness.StubDbContextFactory(options), options);
    }

    private static async Task AssertReviewMappingAsync(SiNetSQLDbContext db, int jobTypeId)
    {
        var reviewId = await db.WorkflowDefinitions.Where(d => d.Code == WorkflowCodes.Review).Select(d => d.Id).SingleAsync();
        var planningId = await db.WorkflowDefinitions.Where(d => d.Code == WorkflowCodes.PlanningWorkflow).Select(d => d.Id).SingleAsync();
        var review = await db.ProjectTypeWorkflowDefinitions.SingleAsync(
            m => m.ProjectTypeId == jobTypeId && m.WorkflowDefinitionId == reviewId);
        Assert.True(review.IsEnabled);
        Assert.True(review.IsDefault);
        var planning = await db.ProjectTypeWorkflowDefinitions.SingleOrDefaultAsync(
            m => m.ProjectTypeId == jobTypeId && m.WorkflowDefinitionId == planningId);
        Assert.True(planning is null || !planning.IsEnabled);
        var stageCount = await db.ProjectTypeWorkflowStages.CountAsync(s =>
            s.ProjectTypeId == jobTypeId
            && db.WorkflowStageDefinitions.Any(stage =>
                stage.Id == s.WorkflowStageDefinitionId
                && stage.WorkflowDefinitionId == reviewId
                && stage.Code.StartsWith("REV.")));
        Assert.Equal(ReviewWorkflowSeedData.Stages.Length, stageCount);
    }

    private static async Task AssertOpinionAsync(SiNetSQLDbContext db)
    {
        var opinionType = await db.JobTypes.SingleAsync(j => j.Title == "חוות דעת");
        var opinionId = await db.WorkflowDefinitions.Where(d => d.Code == WorkflowCodes.Opinion).Select(d => d.Id).SingleAsync();
        var planningId = await db.WorkflowDefinitions.Where(d => d.Code == WorkflowCodes.PlanningWorkflow).Select(d => d.Id).SingleAsync();
        var mapping = await db.ProjectTypeWorkflowDefinitions.SingleAsync(
            m => m.ProjectTypeId == opinionType.Id && m.WorkflowDefinitionId == opinionId);
        Assert.True(mapping.IsEnabled);
        Assert.True(mapping.IsDefault);
        var planning = await db.ProjectTypeWorkflowDefinitions.SingleOrDefaultAsync(
            m => m.ProjectTypeId == opinionType.Id && m.WorkflowDefinitionId == planningId);
        Assert.True(planning is null || !planning.IsEnabled);
        var stageCount = await db.ProjectTypeWorkflowStages.CountAsync(s =>
            s.ProjectTypeId == opinionType.Id
            && db.WorkflowStageDefinitions.Any(stage =>
                stage.Id == s.WorkflowStageDefinitionId && stage.WorkflowDefinitionId == opinionId));
        Assert.Equal(OpinionWorkflowSeedData.Stages.Length, stageCount);
    }

    private static async Task<SeedSnapshot> SnapshotAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);
        return new SeedSnapshot(
            await db.JobTypes.CountAsync(j => j.Title == "בדיקה"),
            await db.JobTypes.CountAsync(j => j.Title == "חוות דעת"),
            await db.JobTypes.CountAsync(j => j.Title == "בדיקה חוות דעת" || j.Title == "בדיקה_חוות_דעת"),
            await db.ProjectTypeWorkflowDefinitions.CountAsync(),
            await db.ProjectTypeWorkflowStages.CountAsync());
    }

    private sealed record SeedSnapshot(
        int ReviewJobTypes,
        int OpinionJobTypes,
        int LegacyJobTypes,
        int Mappings,
        int StageProfiles);
}
