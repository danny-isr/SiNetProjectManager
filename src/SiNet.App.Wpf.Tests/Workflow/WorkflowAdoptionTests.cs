using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class WorkflowAdoptionTests
{
    [Fact]
    public async Task Atomic_start_rolls_back_when_stage_task_has_no_assignee()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: false);
            var orchestrator = provider.GetRequiredService<WorkflowTaskOrchestrator>();

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                orchestrator.StartWorkflowAtomicAsync(
                    ctx.DefinitionId,
                    ctx.ProjectId,
                    WorkflowTriggerType.System,
                    triggerEntityId: null,
                    ProposalWorkflowHarness.UserId,
                    "should roll back",
                    CancellationToken.None,
                    initialStageCode: ReviewStageCodes.ProfessionalReview,
                    jobTypeId: ctx.JobTypeId,
                    requireCurrentStageTask: true).AsTask());

            await using var db = new SiNetSQLDbContext(options);
            Assert.Equal(0, await db.WorkflowInstances.CountAsync());
            Assert.Equal(0, await db.WorkflowStageTransitions.CountAsync());
            Assert.Equal(0, await db.ProjectAssignments.CountAsync(t => t.ProjectId == ctx.ProjectId));
        }
    }

    [Fact]
    public async Task Adopt_Review_at_ProfessionalReview_creates_only_current_work()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(ctx, ReviewStageCodes.ProfessionalReview);

            var preview = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, preview.Disposition);
            Assert.True(preview.CanCommit);
            Assert.Contains(preview.WillCreateTasks, t => t.TaskTypeCode == TaskTypeCodes.PerformProfessionalReview);
            Assert.Contains(preview.WillNotCreate, t => t == TaskTypeCodes.OpenReviewProject);
            Assert.Contains(preview.HistoricalStages, s => s.Code == ReviewStageCodes.ProjectSetup);
            Assert.Contains(preview.HistoricalStages, s => s.Code == ReviewStageCodes.MaterialIntake);
            Assert.Contains(preview.WillNotCreate, t => t.Contains("child workflow", StringComparison.Ordinal));

            var committed = await adoption.CommitAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);
            Assert.NotNull(committed.WorkflowInstanceId);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .Include(i => i.CurrentStage)
                .Include(i => i.StageTransitions)
                .SingleAsync();
            Assert.Equal(WorkflowStatus.Active, instance.Status);
            Assert.Equal(ctx.JobTypeId, instance.JobTypeId);
            Assert.Equal(ReviewStageCodes.ProfessionalReview, instance.CurrentStage!.Code);
            Assert.Equal(WorkflowTriggerType.System, instance.TriggerType);
            Assert.Contains("[ADOPTED]", instance.Notes, StringComparison.Ordinal);
            Assert.Single(instance.StageTransitions);
            Assert.Null(instance.StageTransitions.Single().FromStageId);

            Assert.Equal(1, await CountTasksAsync(db, instance.Id, TaskTypeCodes.PerformProfessionalReview));
            Assert.Equal(0, await CountTasksAsync(db, instance.Id, TaskTypeCodes.OpenReviewProject));
            var matDefId = await db.WorkflowDefinitions
                .Where(d => d.Code == WorkflowCodes.MaterialIntake)
                .Select(d => d.Id)
                .FirstAsync();
            Assert.Equal(0, await db.WorkflowInstances.CountAsync(i => i.WorkflowDefinitionId == matDefId));
        }
    }

    [Fact]
    public async Task Second_commit_does_not_duplicate_instance_or_task()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(ctx, ReviewStageCodes.ProfessionalReview);

            var first = await adoption.CommitAsync(request, CancellationToken.None);
            var second = await adoption.CommitAsync(request, CancellationToken.None);

            Assert.Equal(WorkflowAdoptionDisposition.Committed, first.Disposition);
            Assert.Equal(WorkflowAdoptionDisposition.AlreadyExists, second.Disposition);
            Assert.Equal(first.WorkflowInstanceId, second.WorkflowInstanceId);

            await using var db = new SiNetSQLDbContext(options);
            Assert.Equal(1, await db.WorkflowInstances.CountAsync());
            Assert.Equal(1, await CountTasksAsync(db, first.WorkflowInstanceId!.Value, TaskTypeCodes.PerformProfessionalReview));
        }
    }

    [Fact]
    public async Task Existing_active_earlier_stage_requires_review_and_does_not_advance()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var started = await adoption.CommitAsync(Request(ctx, ReviewStageCodes.ProjectSetup), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.Committed, started.Disposition);

            var preview = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.RequiresReviewExistingWorkflow, preview.Disposition);
            Assert.False(preview.CanCommit);

            var commit = await adoption.CommitAsync(Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.RequiresReviewExistingWorkflow, commit.Disposition);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .Include(i => i.CurrentStage)
                .Include(i => i.StageTransitions)
                .SingleAsync();
            Assert.Equal(ReviewStageCodes.ProjectSetup, instance.CurrentStage!.Code);
            Assert.Single(instance.StageTransitions);
        }
    }

    [Fact]
    public async Task Existing_paused_and_completed_are_not_duplicated()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var request = Request(ctx, ReviewStageCodes.ProfessionalReview);
            var started = await adoption.CommitAsync(request, CancellationToken.None);

            await commands.PauseAsync(
                new PauseWorkflowCommand(started.WorkflowInstanceId!.Value, ProposalWorkflowHarness.UserId, null),
                CancellationToken.None);
            var paused = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedAlreadyPaused, paused.Disposition);

            await commands.ResumeAsync(
                new ResumeWorkflowCommand(started.WorkflowInstanceId.Value, ProposalWorkflowHarness.UserId, null),
                CancellationToken.None);
            await commands.CompleteInstanceAsync(
                new CompleteWorkflowCommand(started.WorkflowInstanceId.Value, ProposalWorkflowHarness.UserId, null),
                CancellationToken.None);
            var completed = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.RequiresReviewCompletedExists, completed.Disposition);

            await using var db = new SiNetSQLDbContext(options);
            Assert.Equal(1, await db.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task SubWorkflow_host_final_stage_and_inactive_jobtype_stage_are_blocked()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();

            var material = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.MaterialIntake), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, material.Disposition);

            var final = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.Completed), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, final.Disposition);

            await using (var db = new SiNetSQLDbContext(options))
            {
                var stageId = await db.WorkflowStageDefinitions
                    .Where(s => s.Code == ReviewStageCodes.ProfessionalReview)
                    .Select(s => s.Id)
                    .FirstAsync();
                var row = await db.ProjectTypeWorkflowStages
                    .FirstAsync(p => p.ProjectTypeId == ctx.JobTypeId && p.WorkflowStageDefinitionId == stageId);
                row.IsActive = false;
                var setup = await db.WorkflowStageDefinitions.FirstAsync(s => s.Code == ReviewStageCodes.ProjectSetup);
                setup.NodeType = "Start";
                await db.SaveChangesAsync();
            }

            var inactive = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, inactive.Disposition);

            var startNode = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.ProjectSetup), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, startNode.Disposition);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task Active_report_is_linked_and_historical_reports_are_not_duplicated()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int report1;
            int report2;
            int report3;
            await using (var db = new SiNetSQLDbContext(options))
            {
                report1 = await AddReportAsync(db, ctx.ProjectId, 1);
                report2 = await AddReportAsync(db, ctx.ProjectId, 2);
                report3 = await AddReportAsync(db, ctx.ProjectId, 3);
            }

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(
                ctx,
                ReviewStageCodes.ProfessionalReview,
                reports:
                [
                    new WorkflowAdoptionReportIntent(report1, WorkflowAdoptionReportMode.Historical),
                    new WorkflowAdoptionReportIntent(report2, WorkflowAdoptionReportMode.Historical),
                    new WorkflowAdoptionReportIntent(report3, WorkflowAdoptionReportMode.Active),
                ]);

            var preview = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, preview.Disposition);
            Assert.Contains(preview.Warnings, w => w.Contains("MarkReportAsSentAsync", StringComparison.Ordinal));

            var committed = await adoption.CommitAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);
            Assert.NotNull(committed.ActiveReportLinkId);

            await adoption.CommitAsync(request, CancellationToken.None);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(3, await verify.InspectionReports.CountAsync(r => r.ProjectId == ctx.ProjectId));
            var links = await verify.TaskLinks
                .Where(l =>
                    l.LinkedEntityType == TaskLinkEntityType.InspectionReport
                    && l.IsWorkTarget)
                .ToListAsync();
            var link = Assert.Single(links);
            Assert.Equal(report3, link.LinkedEntityId);
            Assert.Equal(TaskLinkRole.Related, link.Role);
            Assert.Equal(WorkTargetStatus.Pending, link.WorkStatus);
            Assert.True(await verify.InspectionReports.Where(r => r.ReportId == report1).AllAsync(r => !r.IsLockedAfterSend));
        }
    }

    [Fact]
    public async Task Historical_only_reports_leave_the_current_task_without_a_report_link()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int reportId;
            await using (var db = new SiNetSQLDbContext(options))
                reportId = await AddReportAsync(db, ctx.ProjectId, 1);

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var committed = await adoption.CommitAsync(
                Request(
                    ctx,
                    ReviewStageCodes.ProfessionalReview,
                    reports: [new WorkflowAdoptionReportIntent(reportId, WorkflowAdoptionReportMode.Historical)]),
                CancellationToken.None);

            Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);
            Assert.Null(committed.ActiveReportLinkId);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.TaskLinks.CountAsync(l =>
                l.LinkedEntityType == TaskLinkEntityType.InspectionReport && l.IsWorkTarget));
            Assert.Equal(1, await CountTasksAsync(verify, committed.WorkflowInstanceId!.Value, TaskTypeCodes.PerformProfessionalReview));
        }
    }

    [Fact]
    public void Adopted_marker_matches_only_the_notes_prefix()
    {
        Assert.True(WorkflowAdoptionMarkers.IsMarked("[ADOPTED] Existing workflow adopted into SiNet at REV.ProfessionalReview."));
        Assert.False(WorkflowAdoptionMarkers.IsMarked("some text [ADOPTED]"));
        Assert.False(WorkflowAdoptionMarkers.IsMarked(null));
    }

    [Fact]
    public async Task Active_report_links_to_the_registry_inspection_task_for_later_review_stages()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();

            await AssertActiveReportLinksTaskAsync(
                options, adoption, ctx, ReviewStageCodes.AwaitingManagerApproval, TaskTypeCodes.ApproveReviewReport);
        }

        var (provider2, options2) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider2)
        {
            var ctx = await PrepareReviewProjectAsync(options2, assignReviewers: true);
            var adoption = provider2.GetRequiredService<IWorkflowAdoptionService>();
            await AssertActiveReportLinksTaskAsync(
                options2, adoption, ctx, ReviewStageCodes.RecheckRound, TaskTypeCodes.RecheckPlan);
        }
    }

    [Fact]
    public async Task Active_report_on_a_non_report_stage_is_blocked_without_writes()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int reportId;
            await using (var db = new SiNetSQLDbContext(options))
                reportId = await AddReportAsync(db, ctx.ProjectId, 1);

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(
                ctx,
                ReviewStageCodes.ProjectSetup,
                reports: [new WorkflowAdoptionReportIntent(reportId, WorkflowAdoptionReportMode.Active)]);

            var preview = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedNotAllowed, preview.Disposition);
            Assert.False(preview.CanCommit);

            var commit = await adoption.CommitAsync(request, CancellationToken.None);
            Assert.NotEqual(WorkflowAdoptionDisposition.Committed, commit.Disposition);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.WorkflowInstances.CountAsync());
            Assert.Equal(0, await verify.TaskLinks.CountAsync(l => l.IsWorkTarget));
            Assert.False(SqlWorkflowAdoptionService.IsInspectionReportWorkTarget(TaskTypeCodes.OpenReviewProject));
            Assert.True(SqlWorkflowAdoptionService.IsInspectionReportWorkTarget(TaskTypeCodes.PerformProfessionalReview));
        }
    }

    [Fact]
    public async Task Report_preview_distinguishes_the_same_number_in_two_series()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            await using (var db = new SiNetSQLDbContext(options))
            {
                var traffic = new InspectionSeries
                {
                    ProjectId = ctx.ProjectId,
                    SeriesName = "בדיקת תנועה",
                    Created = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                };
                var safety = new InspectionSeries
                {
                    ProjectId = ctx.ProjectId,
                    SeriesName = "בדיקת בטיחות",
                    Created = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                };
                db.InspectionSeries.AddRange(traffic, safety);
                await db.SaveChangesAsync();
                db.InspectionReports.AddRange(
                    new InspectionReport
                    {
                        ProjectId = ctx.ProjectId,
                        SeriesId = traffic.SeriesId,
                        ReportNumber = 1,
                        InspectionDate = new DateTime(2024, 1, 1),
                    },
                    new InspectionReport
                    {
                        ProjectId = ctx.ProjectId,
                        SeriesId = safety.SeriesId,
                        ReportNumber = 1,
                        InspectionDate = new DateTime(2024, 2, 1),
                    });
                await db.SaveChangesAsync();
            }

            var optionsResult = await provider.GetRequiredService<IWorkflowAdoptionService>()
                .GetOptionsAsync(ctx.ProjectId, CancellationToken.None);
            var numberedOne = optionsResult.ExistingReports.Where(r => r.ReportNumber == 1).ToList();
            Assert.Equal(2, numberedOne.Count);
            Assert.Contains(numberedOne, r => r.DisplayLabel == "בדיקת תנועה — Report 1");
            Assert.Contains(numberedOne, r => r.DisplayLabel == "בדיקת בטיחות — Report 1");
        }
    }

    [Fact]
    public async Task Review_adoption_follows_the_selected_jobtype_mapping()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int otherJobTypeId;
            int proposalId;
            await using (var db = new SiNetSQLDbContext(options))
            {
                proposalId = await db.WorkflowDefinitions
                    .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
                    .Select(d => d.Id)
                    .FirstAsync();
                var other = new JobType { Title = "חוות דעת" };
                db.JobTypes.Add(other);
                await db.SaveChangesAsync();
                otherJobTypeId = other.Id;
                db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
                {
                    ProjectId = ctx.ProjectId,
                    ProjectTypeId = otherJobTypeId,
                    Title = other.Title,
                });
                db.ProjectTypeWorkflowDefinitions.AddRange(
                    new ProjectTypeWorkflowDefinition
                    {
                        ProjectTypeId = ctx.JobTypeId,
                        WorkflowDefinitionId = ctx.DefinitionId,
                        IsEnabled = true,
                        SortOrder = 1,
                    },
                    new ProjectTypeWorkflowDefinition
                    {
                        ProjectTypeId = otherJobTypeId,
                        WorkflowDefinitionId = proposalId,
                        IsEnabled = true,
                        SortOrder = 1,
                    });
                await db.SaveChangesAsync();
            }

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var listed = await adoption.GetOptionsAsync(ctx.ProjectId, CancellationToken.None);
            var review = Assert.Single(listed.Workflows, w => w.Code == WorkflowCodes.Review);
            Assert.Contains(review.JobTypes, j => j.JobTypeId == ctx.JobTypeId);
            Assert.DoesNotContain(review.JobTypes, j => j.JobTypeId == otherJobTypeId);

            var allowed = await adoption.PreviewAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, allowed.Disposition);

            var blocked = await adoption.PreviewAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview, jobTypeId: otherJobTypeId),
                CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedNotAllowed, blocked.Disposition);
            Assert.False(blocked.CanCommit);

            var commit = await adoption.CommitAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview, jobTypeId: otherJobTypeId),
                CancellationToken.None);
            Assert.NotEqual(WorkflowAdoptionDisposition.Committed, commit.Disposition);
            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task Review_adoption_stays_open_when_the_project_has_no_workflow_mappings()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();

            var listed = await adoption.GetOptionsAsync(ctx.ProjectId, CancellationToken.None);
            var review = Assert.Single(listed.Workflows, w => w.Code == WorkflowCodes.Review);
            Assert.Contains(review.JobTypes, j => j.JobTypeId == ctx.JobTypeId);

            var preview = await adoption.PreviewAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, preview.Disposition);
        }
    }

    [Fact]
    public async Task Review_mapping_on_another_jobtype_does_not_allow_an_unmapped_track()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int unmappedJobTypeId;
            await using (var db = new SiNetSQLDbContext(options))
            {
                var other = new JobType { Title = "ללא מיפוי" };
                db.JobTypes.Add(other);
                await db.SaveChangesAsync();
                unmappedJobTypeId = other.Id;
                db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
                {
                    ProjectId = ctx.ProjectId,
                    ProjectTypeId = unmappedJobTypeId,
                    Title = other.Title,
                });
                db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
                {
                    ProjectTypeId = ctx.JobTypeId,
                    WorkflowDefinitionId = ctx.DefinitionId,
                    IsEnabled = true,
                    SortOrder = 1,
                });
                await db.SaveChangesAsync();
            }

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var listed = await adoption.GetOptionsAsync(ctx.ProjectId, CancellationToken.None);
            var review = Assert.Single(listed.Workflows, w => w.Code == WorkflowCodes.Review);
            Assert.Contains(review.JobTypes, j => j.JobTypeId == ctx.JobTypeId);
            Assert.DoesNotContain(review.JobTypes, j => j.JobTypeId == unmappedJobTypeId);

            var blocked = await adoption.PreviewAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview, jobTypeId: unmappedJobTypeId),
                CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedNotAllowed, blocked.Disposition);
            Assert.False(blocked.CanCommit);

            var commit = await adoption.CommitAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview, jobTypeId: unmappedJobTypeId),
                CancellationToken.None);
            Assert.NotEqual(WorkflowAdoptionDisposition.Committed, commit.Disposition);
            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task Disabled_review_mapping_blocks_adoption_for_that_jobtype()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            await using (var db = new SiNetSQLDbContext(options))
            {
                db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
                {
                    ProjectTypeId = ctx.JobTypeId,
                    WorkflowDefinitionId = ctx.DefinitionId,
                    IsEnabled = false,
                    SortOrder = 1,
                });
                await db.SaveChangesAsync();
            }

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var listed = await adoption.GetOptionsAsync(ctx.ProjectId, CancellationToken.None);
            Assert.DoesNotContain(listed.Workflows, w =>
                w.Code == WorkflowCodes.Review
                && w.JobTypes.Any(j => j.JobTypeId == ctx.JobTypeId));

            var preview = await adoption.PreviewAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedNotAllowed, preview.Disposition);
            Assert.False(preview.CanCommit);
        }
    }

    [Fact]
    public async Task Enabled_review_mapping_allows_adoption_for_that_jobtype()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            await using (var db = new SiNetSQLDbContext(options))
            {
                db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
                {
                    ProjectTypeId = ctx.JobTypeId,
                    WorkflowDefinitionId = ctx.DefinitionId,
                    IsEnabled = true,
                    SortOrder = 1,
                });
                await db.SaveChangesAsync();
            }

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var listed = await adoption.GetOptionsAsync(ctx.ProjectId, CancellationToken.None);
            var review = Assert.Single(listed.Workflows, w => w.Code == WorkflowCodes.Review);
            Assert.Contains(review.JobTypes, j => j.JobTypeId == ctx.JobTypeId);

            var preview = await adoption.PreviewAsync(
                Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, preview.Disposition);
            Assert.True(preview.CanCommit);
        }
    }

    private static async Task AssertActiveReportLinksTaskAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        IWorkflowAdoptionService adoption,
        ReviewAdoptionContext ctx,
        string stageCode,
        string taskTypeCode)
    {
        int reportId;
        await using (var db = new SiNetSQLDbContext(options))
            reportId = await AddReportAsync(db, ctx.ProjectId, 4);

        var committed = await adoption.CommitAsync(
            Request(
                ctx,
                stageCode,
                reports: [new WorkflowAdoptionReportIntent(reportId, WorkflowAdoptionReportMode.Active)]),
            CancellationToken.None);
        Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);

        await using var verify = new SiNetSQLDbContext(options);
        var link = Assert.Single(await verify.TaskLinks
            .Where(l => l.LinkedEntityType == TaskLinkEntityType.InspectionReport && l.IsWorkTarget)
            .ToListAsync());
        Assert.Equal(reportId, link.LinkedEntityId);
        var taskType = await verify.ProjectAssignments
            .Where(t => t.Id == link.TaskId)
            .Select(t => t.TaskType!.Code)
            .SingleAsync();
        Assert.Equal(taskTypeCode, taskType);
    }

    private static WorkflowAdoptionRequest Request(
        ReviewAdoptionContext ctx,
        string stageCode,
        IReadOnlyList<WorkflowAdoptionReportIntent>? reports = null,
        int? jobTypeId = null) =>
        new(
            ctx.ProjectId,
            ctx.DefinitionId,
            jobTypeId ?? ctx.JobTypeId,
            stageCode,
            ProposalWorkflowHarness.UserId,
            OriginalStartedAt: new DateTime(2024, 3, 1),
            Notes: "manual adoption",
            Reports: reports);

    private static async Task<ReviewAdoptionContext> PrepareReviewProjectAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        bool assignReviewers)
    {
        await using var db = new SiNetSQLDbContext(options);
        var active = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project { Title = "יבנה מזרח", Number = 1844, ProjectStatusId = active.Id };
        var jobType = new JobType { Title = "בדיקת תוכנית" };
        db.Projects.Add(project);
        db.JobTypes.Add(jobType);
        await db.SaveChangesAsync();
        db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
        {
            ProjectId = project.Id,
            ProjectTypeId = jobType.Id,
            Title = jobType.Title,
        });

        var definition = await db.WorkflowDefinitions.FirstAsync(d => d.Code == WorkflowCodes.Review && d.IsActive);
        var stages = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .ToListAsync();
        foreach (var stage in stages)
        {
            db.ProjectTypeWorkflowStages.Add(new ProjectTypeWorkflowStage
            {
                ProjectTypeId = jobType.Id,
                WorkflowStageDefinitionId = stage.Id,
                IsActive = true,
                IsRequired = true,
                SortOrder = stage.SortOrder,
            });
        }

        if (assignReviewers)
        {
            var groupCodes = ReviewWorkflowSeedData.StageGroupAssignments.Values
                .Append(UserGroupCodes.OfficeManagement)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groups = await db.UserGroups
                .Where(g => g.Code != null && groupCodes.Contains(g.Code))
                .ToListAsync();
            foreach (var group in groups)
            {
                var member = await db.UserGroupMemberships
                    .AnyAsync(m => m.UserGroupId == group.Id && m.SiuserId == ProposalWorkflowHarness.UserId);
                if (!member)
                {
                    db.UserGroupMemberships.Add(new UserGroupMembership
                    {
                        UserGroupId = group.Id,
                        SiuserId = ProposalWorkflowHarness.UserId,
                    });
                }
            }
        }

        await EnsureReviewStageTasksAsync(db, stages);
        await db.SaveChangesAsync();
        return new ReviewAdoptionContext(project.Id, jobType.Id, definition.Id);
    }

    /// <summary>
    /// Workflow seed skips REV stage-task templates when the TaskType rows are absent.
    /// The task-type seed lives in a different service, so this fixture creates the
    /// missing types and templates from <see cref="ReviewWorkflowSeedData"/>.
    /// </summary>
    private static async Task EnsureReviewStageTasksAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<WorkflowStageDefinition> stages)
    {
        foreach (var def in ReviewWorkflowSeedData.StageTasks)
        {
            var taskType = await db.TaskTypes.FirstOrDefaultAsync(t => t.Code == def.TaskTypeCode);
            if (taskType is null)
            {
                taskType = new TaskType { Code = def.TaskTypeCode, Name = def.TaskTypeCode, IsActive = true };
                db.TaskTypes.Add(taskType);
                await db.SaveChangesAsync();
            }

            var stage = stages.FirstOrDefault(s => s.Code == def.StageCode);
            if (stage is null)
                continue;

            var exists = await db.WorkflowStageTasks.AnyAsync(t =>
                t.StageDefinitionId == stage.Id && t.TaskTypeId == taskType.Id && t.IsActive);
            if (exists)
                continue;

            db.WorkflowStageTasks.Add(new WorkflowStageTask
            {
                StageDefinitionId = stage.Id,
                TaskTypeId = taskType.Id,
                SortOrder = def.SortOrder,
                IsRequired = def.IsRequired,
                Notes = def.Notes,
                IsActive = true,
            });
        }
    }

    private static async Task<int> AddReportAsync(SiNetSQLDbContext db, int projectId, int number)
    {
        var report = new InspectionReport
        {
            ProjectId = projectId,
            ReportNumber = number,
            InspectionDate = new DateTime(2024, 1, number),
        };
        db.InspectionReports.Add(report);
        await db.SaveChangesAsync();
        return report.ReportId;
    }

    private static async Task<int> CountTasksAsync(SiNetSQLDbContext db, int instanceId, string taskTypeCode) =>
        await db.ProjectAssignments.CountAsync(t =>
            t.TaskType!.Code == taskTypeCode
            && t.TaskLinks.Any(l =>
                l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                && l.LinkedEntityId == instanceId));

    private sealed record ReviewAdoptionContext(int ProjectId, int JobTypeId, int DefinitionId);
}
