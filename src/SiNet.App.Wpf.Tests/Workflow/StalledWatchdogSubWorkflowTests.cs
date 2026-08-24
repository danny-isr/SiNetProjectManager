using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

/// <summary>
/// Regression coverage for two defects in <see cref="StalledWorkflowWatchdog"/> detection, both found by
/// audit and reproduced here on the real seeded graph rather than a synthetic one.
/// <para>
/// <b>Defect A — historical stage scope.</b> The trigger-link query filters by instance and role only, never
/// by the current stage, even though provisioning records the owning stage in
/// <c>TaskLink.Description</c> as <c>Stage:{id}</c>. So <c>MostRecentClosedTaskId</c> can name a task that
/// was closed at a stage the workflow left long ago, and recovery then re-evaluates that stale task.
/// </para>
/// <para>
/// <b>Defect B — parent waiting for a child.</b> A parent parked on a <c>SubWorkflow</c> stage has no tasks
/// of its own by design, and <see cref="WorkflowStatus"/> has no <c>Waiting</c> value, so a parent that is
/// legitimately waiting for a running child is indistinguishable from a stalled one and is reported as
/// stalled.
/// </para>
/// <para>
/// Detection is asserted directly instead of running recovery, so these tests state what is wrong without
/// depending on which recovery branch happens to be reached.
/// </para>
/// </summary>
public sealed class StalledWatchdogSubWorkflowTests
{
    private const int UserId = ProposalWorkflowHarness.UserId;

    [Fact]
    public async Task Parent_waiting_for_active_child_on_subworkflow_stage_is_not_stalled()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var graph = await BuildParentWaitingForChildAsync(options, childStatus: WorkflowStatus.Active);
            var watchdog = BuildWatchdog(provider);

            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            Assert.DoesNotContain(graph.ParentInstanceId, stalled.Select(s => s.InstanceId));
        }
    }

    [Fact]
    public async Task Parent_waiting_for_paused_child_on_subworkflow_stage_is_not_stalled()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var graph = await BuildParentWaitingForChildAsync(options, childStatus: WorkflowStatus.Paused);
            var watchdog = BuildWatchdog(provider);

            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            Assert.DoesNotContain(graph.ParentInstanceId, stalled.Select(s => s.InstanceId));
        }
    }

    [Fact]
    public async Task Parent_on_subworkflow_stage_without_any_child_is_still_detected()
    {
        // The fix must not become "ignore every SubWorkflow stage": a host stage with no child really is
        // stuck and must stay detectable.
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var graph = await BuildParentWaitingForChildAsync(options, childStatus: null);
            var watchdog = BuildWatchdog(provider);

            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            Assert.Contains(graph.ParentInstanceId, stalled.Select(s => s.InstanceId));
        }
    }

    [Fact]
    public async Task Parent_whose_child_is_completed_is_still_detected()
    {
        // A completed child means the parent should have advanced. If it did not, that is a genuine stall
        // and the watchdog is the safety net that must catch it.
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var graph = await BuildParentWaitingForChildAsync(
                options,
                childStatus: WorkflowStatus.Completed);
            var watchdog = BuildWatchdog(provider);

            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            Assert.Contains(graph.ParentInstanceId, stalled.Select(s => s.InstanceId));
        }
    }

    [Fact]
    public async Task Stalled_report_does_not_name_a_task_closed_at_an_earlier_stage()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var graph = await BuildStalledWithHistoricalTaskAsync(options);
            var watchdog = BuildWatchdog(provider);

            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            var report = Assert.Single(stalled, s => s.InstanceId == graph.ParentInstanceId);
            Assert.NotEqual(graph.HistoricalTaskId, report.MostRecentClosedTaskId);
        }
    }

    [Fact]
    public async Task Stalled_report_counts_only_tasks_of_the_current_stage()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var graph = await BuildStalledWithHistoricalTaskAsync(options);
            var watchdog = BuildWatchdog(provider);

            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            var report = Assert.Single(stalled, s => s.InstanceId == graph.ParentInstanceId);
            Assert.Equal(0, report.TotalTasks);
        }
    }

    private static StalledWorkflowWatchdog BuildWatchdog(IServiceProvider provider) =>
        new(provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>(),
            provider.GetRequiredService<IWorkflowCommandService>());

    private sealed record Graph(int ParentInstanceId, int? ChildInstanceId, int HistoricalTaskId);

    /// <summary>
    /// Builds a Review parent parked on its real seeded <c>SubWorkflow</c> host stage, with a closed task
    /// left over from an earlier stage, and optionally a child MaterialIntake instance in
    /// <paramref name="childStatus"/>.
    /// </summary>
    private static async Task<Graph> BuildParentWaitingForChildAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        WorkflowStatus? childStatus)
    {
        await using var db = new SiNetSQLDbContext(options);

        var reviewDefId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Review && d.IsActive)
            .Select(d => d.Id)
            .FirstAsync();

        // Located by NodeType rather than by code, so the test follows the seeded graph instead of
        // restating it.
        var hostStage = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == reviewDefId && s.NodeType == "SubWorkflow")
            .FirstAsync();

        var earlierStage = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == reviewDefId
                     && s.Id != hostStage.Id
                     && !s.IsFinal
                     && s.SortOrder < hostStage.SortOrder)
            .OrderBy(s => s.SortOrder)
            .FirstAsync();

        var projectId = await AddProjectAsync(db, "Watchdog SubWorkflow parent");

        var parent = new WorkflowInstance
        {
            WorkflowDefinitionId = reviewDefId,
            ProjectId = projectId,
            IsProjectBound = true,
            Status = WorkflowStatus.Active,
            CurrentStageId = hostStage.Id,
            TriggerType = WorkflowTriggerType.Manual,
            CreatedByUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.WorkflowInstances.Add(parent);
        await db.SaveChangesAsync();

        var historicalTaskId = await AddClosedTriggerTaskAsync(
            db, projectId, parent.Id, earlierStage.Id, "Closed at an earlier REV stage");

        int? childId = null;
        if (childStatus is not null)
        {
            var matDefId = await db.WorkflowDefinitions
                .Where(d => d.Code == WorkflowCodes.MaterialIntake && d.IsActive)
                .Select(d => d.Id)
                .FirstAsync();

            var childEntry = await db.WorkflowStageDefinitions
                .Where(s => s.WorkflowDefinitionId == matDefId && s.IsInitial)
                .OrderBy(s => s.SortOrder)
                .FirstAsync();

            var child = new WorkflowInstance
            {
                WorkflowDefinitionId = matDefId,
                ProjectId = projectId,
                IsProjectBound = true,
                Status = childStatus.Value,
                CurrentStageId = childEntry.Id,
                TriggerType = WorkflowTriggerType.Manual,
                ParentWorkflowInstanceId = parent.Id,
                CreatedByUserId = UserId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.WorkflowInstances.Add(child);
            await db.SaveChangesAsync();
            childId = child.Id;

            // The child carries the open work while the parent waits, which is exactly why the parent has
            // no open task of its own.
            if (childStatus is WorkflowStatus.Active or WorkflowStatus.Paused)
            {
                await AddOpenTriggerTaskAsync(db, projectId, child.Id, childEntry.Id, "Child MAT work");
            }
        }

        return new Graph(parent.Id, childId, historicalTaskId);
    }

    /// <summary>
    /// Builds an instance that is genuinely stalled on an ordinary stage, whose only trigger-linked task
    /// belongs to an earlier stage. Isolates defect A from the SubWorkflow question.
    /// </summary>
    private static async Task<Graph> BuildStalledWithHistoricalTaskAsync(
        DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);

        var defId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
            .Select(d => d.Id)
            .FirstAsync();

        var stages = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == defId && !s.IsFinal && s.NodeType == "Stage")
            .OrderBy(s => s.SortOrder)
            .Take(2)
            .ToListAsync();

        var earlierStage = stages[0];
        var currentStage = stages[1];

        var projectId = await AddProjectAsync(db, "Watchdog historical scope");

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = defId,
            ProjectId = projectId,
            IsProjectBound = true,
            Status = WorkflowStatus.Active,
            CurrentStageId = currentStage.Id,
            TriggerType = WorkflowTriggerType.Manual,
            CreatedByUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        var historicalTaskId = await AddClosedTriggerTaskAsync(
            db, projectId, instance.Id, earlierStage.Id, "Closed at an earlier PRP stage");

        return new Graph(instance.Id, null, historicalTaskId);
    }

    private static async Task<int> AddProjectAsync(SiNetSQLDbContext db, string title)
    {
        var statusId = await db.ProjectStatuses
            .Where(s => s.Code == ProjectStatusCodes.Active)
            .Select(s => s.Id)
            .FirstAsync();

        var project = new Project { Title = title, ProjectStatusId = statusId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static Task<int> AddClosedTriggerTaskAsync(
        SiNetSQLDbContext db, int projectId, int instanceId, int stageId, string title) =>
        AddTriggerTaskAsync(db, projectId, instanceId, stageId, title, TaskStatusCodes.Completed);

    private static Task<int> AddOpenTriggerTaskAsync(
        SiNetSQLDbContext db, int projectId, int instanceId, int stageId, string title) =>
        AddTriggerTaskAsync(db, projectId, instanceId, stageId, title, TaskStatusCodes.Open);

    private static async Task<int> AddTriggerTaskAsync(
        SiNetSQLDbContext db,
        int projectId,
        int instanceId,
        int stageId,
        string title,
        string statusCode)
    {
        var status = await db.ProjectAssignmentStatuses.FirstAsync(s => s.Code == statusCode);

        var task = new ProjectAssignment
        {
            ProjectId = projectId,
            Title = title,
            StatusId = status.Id,
            Status = status.Code,
            AssignedToId = UserId,
            Modified = DateTime.UtcNow,
            Created = DateTime.UtcNow,
        };
        db.ProjectAssignments.Add(task);
        await db.SaveChangesAsync();

        db.TaskLinks.Add(new TaskLink
        {
            TaskId = task.Id,
            LinkedEntityType = TaskLinkEntityType.WorkflowInstance,
            LinkedEntityId = instanceId,
            Role = TaskLinkRole.Trigger,
            // The stage tag provisioning already writes. Its presence is what makes a stage-scoped
            // watchdog query possible without any schema change.
            Description = $"Stage:{stageId}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = UserId,
        });
        await db.SaveChangesAsync();

        return task.Id;
    }
}
