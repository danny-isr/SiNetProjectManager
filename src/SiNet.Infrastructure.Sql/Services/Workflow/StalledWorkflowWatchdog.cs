using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Notifications;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Native (Infrastructure.Sql) periodic scanner for "stalled" workflow instances: active workflows
/// whose current stage has no open tasks. Safety net that catches cases where auto-advance (via
/// <see cref="IWorkflowCommandService.CheckAndAutoAdvanceAsync"/>) was never called or failed after a
/// task closed.
/// <para>
/// Detection: an active <see cref="WorkflowInstance"/> is stalled when its current stage is
/// non-terminal and every linked <see cref="ProjectAssignment"/> (via <see cref="TaskLink"/> Trigger
/// role) is closed. Recovery re-invokes auto-advance through the Application write port
/// (<see cref="IWorkflowCommandService"/>); for 0-task stages it falls back to
/// <see cref="IWorkflowCommandService.ReprovisionStalledStageTasksAsync"/>.
/// </para>
/// <para>
/// This is the native port of the legacy <c>SiNetSQL.Services.Workflow.StalledWorkflowWatchdog</c>.
/// It depends only on the shared <see cref="IDbContextFactory{SiNetSQLDbContext}"/> and the
/// Application command port, so it runs against the single native engine.
/// </para>
/// </summary>
public sealed class StalledWorkflowWatchdog(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IWorkflowCommandService workflowCommands,
    INotificationDeliveryService? notifications = null)
{
    /// <summary>Template code for the structured "orphaned workflow" audit signal.</summary>
    internal const string OrphanNotificationTemplate = "workflow-orphaned";

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;
    private readonly IWorkflowCommandService _workflowCommands = workflowCommands;
    private readonly INotificationDeliveryService? _notifications = notifications;

    /// <summary>Scans for active workflow instances that appear stalled.</summary>
    public async ValueTask<List<StalledWorkflowInfo>> DetectStalledAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var activeInstances = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.CurrentStage)
            .Where(i => i.Status == WorkflowStatus.Active && i.CurrentStageId != null)
            .ToListAsync(ct);

        var stalled = new List<StalledWorkflowInfo>();

        foreach (var instance in activeInstances)
        {
            if (instance.CurrentStage is null) continue;
            if (instance.CurrentStage.IsFinal) continue;
            if (string.Equals(instance.CurrentStage.NodeType, "End", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(instance.CurrentStage.NodeType, "Start", StringComparison.OrdinalIgnoreCase)) continue;

            var linkedTasks = await (
                from link in db.TaskLinks.AsNoTracking()
                join task in db.ProjectAssignments.AsNoTracking()
                    on link.TaskId equals task.Id
                join status in db.ProjectAssignmentStatuses.AsNoTracking()
                    on task.StatusId equals status.Id
                where link.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                   && link.LinkedEntityId == instance.Id
                   && link.Role == TaskLinkRole.Trigger
                select new { task.Id, task.Modified, status.IsOpen }
            ).ToListAsync(ct);

            if (linkedTasks.Count == 0)
            {
                stalled.Add(new StalledWorkflowInfo(
                    instance.Id,
                    instance.WorkflowDefinitionId,
                    instance.CurrentStageId!.Value,
                    instance.CurrentStage.Code ?? instance.CurrentStage.Name,
                    instance.ProjectId,
                    MostRecentClosedTaskId: null,
                    TotalTasks: 0,
                    OpenTasks: 0));
                continue;
            }

            var openCount = linkedTasks.Count(t => t.IsOpen);
            if (openCount > 0) continue;

            var mostRecentClosed = linkedTasks
                .OrderByDescending(t => t.Modified)
                .FirstOrDefault();

            stalled.Add(new StalledWorkflowInfo(
                instance.Id,
                instance.WorkflowDefinitionId,
                instance.CurrentStageId!.Value,
                instance.CurrentStage.Code ?? instance.CurrentStage.Name,
                instance.ProjectId,
                MostRecentClosedTaskId: mostRecentClosed?.Id,
                TotalTasks: linkedTasks.Count,
                OpenTasks: 0));
        }

        return stalled;
    }

    /// <summary>
    /// Attempts to recover stalled workflows by re-invoking auto-advance through the Application write
    /// port. Returns the count of successfully recovered instances. All failures are non-fatal.
    /// </summary>
    public async ValueTask<int> AttemptRecoveryAsync(
        List<StalledWorkflowInfo> stalledWorkflows,
        int systemUserId,
        CancellationToken ct)
    {
        var recovered = 0;

        foreach (var info in stalledWorkflows)
        {
            if (info.MostRecentClosedTaskId is not int taskId)
            {
                try
                {
                    var result = await _workflowCommands.CheckAndAutoAdvanceStalledAsync(
                        new StalledWorkflowCommand(info.InstanceId, systemUserId), ct);
                    if (result is not null)
                    {
                        Trace.TraceInformation(
                            "[Watchdog] Recovered 0-task workflow {0}: Action={1}, TargetStage={2}",
                            info.InstanceId, result.Action, result.TargetStageId);
                        recovered++;
                    }
                    else
                    {
                        var createdTaskCount = await _workflowCommands.ReprovisionStalledStageTasksAsync(
                            new StalledWorkflowCommand(info.InstanceId, systemUserId), ct);
                        if (createdTaskCount > 0)
                        {
                            Trace.TraceInformation(
                                "[Watchdog] Recovered 0-task workflow {0} by provisioning {1} tasks.",
                                info.InstanceId, createdTaskCount);
                            recovered++;
                        }
                        else
                        {
                            await NotifyOrphanAsync(
                                    info,
                                    "0-task stage could not be auto-advanced or re-provisioned",
                                    ct)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("[Watchdog] Recovery failed for 0-task workflow {0} (non-fatal): {1}", info.InstanceId, ex);
                }
                continue;
            }

            try
            {
                var result = await _workflowCommands.CheckAndAutoAdvanceAsync(
                    new TaskClosedCommand(taskId, systemUserId), ct);

                if (result is not null)
                {
                    Trace.TraceInformation(
                        "[Watchdog] Recovered workflow {0}: Action={1}, TargetStage={2}",
                        info.InstanceId, result.Action, result.TargetStageId);
                    recovered++;
                }
                else
                {
                    await NotifyOrphanAsync(
                            info,
                            $"stage '{info.StageName}' has no advancing transition after task #{taskId} closed",
                            ct)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("[Watchdog] Recovery failed for workflow {0}, task #{1} (non-fatal): {2}", info.InstanceId, taskId, ex);
            }
        }

        return recovered;
    }

    /// <summary>
    /// Emits a visible, structured "orphaned workflow — manual intervention needed" signal for a
    /// workflow the watchdog could not recover. Always logs at warning level, and additionally routes
    /// the same intent through the host's audit/notification channel when one is configured, so the
    /// orphan is tracked/flagged rather than only trace-logged. Never throws.
    /// </summary>
    private async ValueTask NotifyOrphanAsync(StalledWorkflowInfo info, string reason, CancellationToken ct)
    {
        Trace.TraceWarning(
            "[Watchdog] ORPHANED WORKFLOW {0} (def {1}, stage '{2}', project {3}): {4}. Manual intervention needed.",
            info.InstanceId, info.WorkflowDefinitionId, info.StageName, info.ProjectId, reason);

        if (_notifications is null)
            return;

        try
        {
            var message =
                $"Workflow instance {info.InstanceId} (definition {info.WorkflowDefinitionId}) is orphaned at " +
                $"stage '{info.StageName}': {reason}. Manual intervention needed.";

            await _notifications.DeliverAsync(
                    new NotificationDeliveryRequest(
                        Template: OrphanNotificationTemplate,
                        Recipients: Array.Empty<string>(),
                        RawConfigJson: null,
                        ProjectId: info.ProjectId,
                        WorkflowInstanceId: info.InstanceId,
                        TaskId: info.MostRecentClosedTaskId,
                        UserId: null),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[Watchdog] Failed to emit orphan notification for workflow {0} (non-fatal): {1}",
                info.InstanceId, ex);
        }
    }
}

/// <summary>Diagnostic information about a stalled workflow instance.</summary>
public sealed record StalledWorkflowInfo(
    int InstanceId,
    int WorkflowDefinitionId,
    int CurrentStageId,
    string StageName,
    int ProjectId,
    int? MostRecentClosedTaskId,
    int TotalTasks,
    int OpenTasks);
