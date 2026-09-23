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
