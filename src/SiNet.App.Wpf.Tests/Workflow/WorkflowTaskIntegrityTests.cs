using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

/// <summary>
/// Integrity coverage for the "prevent orphaned workflow" behavior: a task driving a non-terminal
/// workflow cannot be hard-deleted; deactivating it pauses the workflow (so the watchdog never treats
/// it as stalled); reactivating it resumes the workflow; and after reactivation the task still drives
/// auto-advance. Uses the real native <see cref="IWorkflowCommandService"/> so Pause/Resume genuinely
/// flip <see cref="WorkflowInstance.Status"/>.
/// </summary>
public sealed class WorkflowTaskIntegrityTests
{
    private const int UserId = ProposalWorkflowHarness.UserId;

    [Fact]
    public async Task Delete_is_blocked_for_task_driving_active_workflow()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, _) = await StartProposalAndGetIntakeAsync(provider, options);

            var result = await workbench.DeleteTaskAsync(intakeTaskId, UserId);

            Assert.False(result.Succeeded);
            Assert.True(result.BlockedByWorkflow);

            await using var db = new SiNetSQLDbContext(options);
            Assert.True(await db.ProjectAssignments.AnyAsync(t => t.Id == intakeTaskId));
        }
    }

    [Fact]
    public async Task Delete_is_blocked_for_task_driving_paused_workflow()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, instanceId) = await StartProposalAndGetIntakeAsync(provider, options);

            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            await commands.PauseAsync(new PauseWorkflowCommand(instanceId, UserId, "test pause"), CancellationToken.None);

            var result = await workbench.DeleteTaskAsync(intakeTaskId, UserId);

            Assert.False(result.Succeeded);
            Assert.True(result.BlockedByWorkflow);
        }
    }

    [Fact]
    public async Task Delete_is_allowed_for_non_workflow_task()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var factory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var workbench = new SqlTaskWorkbenchService(factory, commands);

            var taskId = await SeedPlainTaskAsync(options);

            var result = await workbench.DeleteTaskAsync(taskId, UserId);

            Assert.True(result.Succeeded, result.Message);
            Assert.False(result.BlockedByWorkflow);

            await using var db = new SiNetSQLDbContext(options);
            Assert.False(await db.ProjectAssignments.AnyAsync(t => t.Id == taskId));
        }
    }

    [Fact]
    public async Task Delete_is_allowed_when_linked_workflow_is_terminal()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, instanceId) = await StartProposalAndGetIntakeAsync(provider, options);

            // Drive the workflow to a terminal state — the guard must no longer block delete.
            await using (var db = new SiNetSQLDbContext(options))
            {
                var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == instanceId);
                instance.Status = WorkflowStatus.Completed;
                await db.SaveChangesAsync();
            }

            var result = await workbench.DeleteTaskAsync(intakeTaskId, UserId);

            Assert.True(result.Succeeded, result.Message);
            Assert.False(result.BlockedByWorkflow);
        }
    }

    [Fact]
    public async Task Deactivate_cancels_task_pauses_workflow_and_preserves_link()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, instanceId) = await StartProposalAndGetIntakeAsync(provider, options);

            var result = await workbench.DeactivateTaskAsync(intakeTaskId, UserId);
            Assert.True(result.Succeeded, result.Message);

            await using var db = new SiNetSQLDbContext(options);

            var task = await db.ProjectAssignments
                .Include(t => t.AssignmentStatus)
                .FirstAsync(t => t.Id == intakeTaskId);
            Assert.Equal(TaskStatusCodes.Cancelled, task.AssignmentStatus!.Code);
            Assert.Null(task.WorkPriority);

            // The TaskLink (Trigger → WorkflowInstance) is preserved so it can be reactivated.
            Assert.True(await db.TaskLinks.AnyAsync(l =>
                l.TaskId == intakeTaskId
                && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                && l.Role == TaskLinkRole.Trigger));

            var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == instanceId);
            Assert.Equal(WorkflowStatus.Paused, instance.Status);
        }
    }

    [Fact]
    public async Task Deactivated_workflow_is_not_seen_as_stalled_and_creates_no_duplicate()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, instanceId) = await StartProposalAndGetIntakeAsync(provider, options);

            await workbench.DeactivateTaskAsync(intakeTaskId, UserId);

            int tasksBefore;
            await using (var db = new SiNetSQLDbContext(options))
                tasksBefore = await db.ProjectAssignments.CountAsync();

            var watchdog = provider.GetRequiredService<StalledWorkflowWatchdog>();
            var stalled = await watchdog.DetectStalledAsync(CancellationToken.None);

            // The paused instance must not be reported as stalled (watchdog scans Active only).
            Assert.DoesNotContain(stalled, s => s.InstanceId == instanceId);

            var recovered = await watchdog.AttemptRecoveryAsync(stalled, UserId, CancellationToken.None);

            await using (var db = new SiNetSQLDbContext(options))
            {
                var tasksAfter = await db.ProjectAssignments.CountAsync();
                Assert.Equal(tasksBefore, tasksAfter); // no duplicate/re-provisioned task
            }

            Assert.DoesNotContain(stalled, s => s.InstanceId == instanceId);
            Assert.Equal(0, recovered);
        }
    }

    [Fact]
    public async Task Reactivate_reopens_task_and_resumes_workflow()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, instanceId) = await StartProposalAndGetIntakeAsync(provider, options);

            await workbench.DeactivateTaskAsync(intakeTaskId, UserId);
            var result = await workbench.ReactivateTaskAsync(intakeTaskId, UserId);
            Assert.True(result.Succeeded, result.Message);

            await using var db = new SiNetSQLDbContext(options);

            var task = await db.ProjectAssignments
                .Include(t => t.AssignmentStatus)
                .FirstAsync(t => t.Id == intakeTaskId);
            Assert.Equal(TaskStatusCodes.Open, task.AssignmentStatus!.Code);
            Assert.NotNull(task.WorkPriority); // back in the queue

            var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == instanceId);
            Assert.Equal(WorkflowStatus.Active, instance.Status);
        }
    }

    [Fact]
    public async Task Deactivate_then_reactivate_then_complete_auto_advances()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (workbench, intakeTaskId, instanceId) = await StartProposalAndGetIntakeAsync(provider, options);
            var commands = provider.GetRequiredService<IWorkflowCommandService>();

            // Deactivate → Paused, then reactivate → Active.
            await workbench.DeactivateTaskAsync(intakeTaskId, UserId);
            await workbench.ReactivateTaskAsync(intakeTaskId, UserId);

            // The reactivated driving task still advances the workflow when completed.
            await ProposalWorkflowHarness.MarkTaskResultAsync(options, intakeTaskId, TaskResultCodes.QuoteRequestDetected);
            var advance = await commands.CheckAndAutoAdvanceAsync(
                new TaskClosedCommand(intakeTaskId, UserId), CancellationToken.None);

            Assert.NotNull(advance);
            Assert.Equal(StageCompletionActionDto.AutoAdvanced, advance!.Action);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .Include(i => i.CurrentStage)
                .FirstAsync(i => i.Id == instanceId);
            Assert.Equal(ProposalStageCodes.ProjectSetup, instance.CurrentStage!.Code);
        }
    }

    // ────────────────────────────────────────────────────────────────────────

    private static async Task<(SqlTaskWorkbenchService Workbench, int IntakeTaskId, int InstanceId)>
        StartProposalAndGetIntakeAsync(
            Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
            DbContextOptions<SiNetSQLDbContext> options)
    {
        var (projectId, emailId, defId) = await ProposalWorkflowHarness.SeedProjectAndEmailAsync(options);
        var commands = provider.GetRequiredService<IWorkflowCommandService>();

        var start = await commands.StartAsync(
            new StartWorkflowCommand(
                DefinitionId: defId,
                ProjectId: projectId,
                TriggerType: WorkflowTriggerTypeDto.Email,
                TriggerEntityId: emailId,
                UserId: UserId,
                Notes: "integrity",
                IsProjectBound: false),
            CancellationToken.None);

        var intake = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
            options, start.Instance.Id, TaskTypeCodes.IdentifyQuoteRequest);

        var factory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var workbench = new SqlTaskWorkbenchService(factory, commands);

        return (workbench, intake.Id, start.Instance.Id);
    }

    private static async Task<int> SeedPlainTaskAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);

        var open = await db.ProjectAssignmentStatuses.FirstAsync(s => s.Code == TaskStatusCodes.Open);
        var taskType = await db.TaskTypes.FirstAsync(t => t.Code == TaskTypeCodes.IdentifyQuoteRequest);
        var status = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project { Title = "Plain task project", ProjectStatusId = status.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var task = new ProjectAssignment
        {
            Title = "Plain non-workflow task",
            ProjectId = project.Id,
            AssignedToId = UserId,
            StatusId = open.Id,
            Status = open.Code,
            TaskTypeId = taskType.Id,
            WorkQueueBucket = WorkQueueBucketCodes.Quick,
            WorkPriority = 1,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        db.ProjectAssignments.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }
}
