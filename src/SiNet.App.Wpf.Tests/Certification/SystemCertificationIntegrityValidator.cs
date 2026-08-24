using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Global integrity assertions run against the live database after every important transition.
/// <para>
/// These are audit assertions, not new production abstractions. The engine already <i>prevents</i> most of
/// these situations (task delete/deactivate guards, duplicate-track guard, provisioning idempotency); what
/// did not exist is anything that <i>detects</i> them on a real database. All existing workflow tests run
/// on EF InMemory, so no integrity rule was previously proven against SQL Server.
/// </para>
/// <para>
/// Checks are global rather than scoped to rows this run created, as requested. To make that usable on a
/// restored DEV database, a <see cref="BaselineAsync"/> snapshot is taken before the first write and every
/// later check is reported as a delta: pre-existing violations are informational, while any <b>new</b>
/// violation fails the run. Without the baseline, inherited data would either mask real regressions or
/// make a pass impossible.
/// </para>
/// </summary>
internal sealed class SystemCertificationIntegrityValidator(
    IDbContextFactory<SiNetSQLDbContext> dbFactory)
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;

    internal sealed record Violation(string Check, string Detail);

    internal sealed record Report(IReadOnlyList<Violation> All, IReadOnlyList<Violation> New)
    {
        public bool IsClean => New.Count == 0;

        public string Describe() =>
            New.Count == 0
                ? $"no new integrity violations ({All.Count} pre-existing, unchanged)"
                : $"{New.Count} NEW violation(s): "
                  + string.Join(" || ", New.Select(v => $"[{v.Check}] {v.Detail}"));
    }

    private HashSet<string>? _baseline;

    /// <summary>Snapshots existing violations so later checks can report only what this run introduced.</summary>
    public async Task<Report> BaselineAsync(CancellationToken cancellationToken = default)
    {
        var violations = await CollectAsync(cancellationToken);
        _baseline = violations.Select(Key).ToHashSet(StringComparer.Ordinal);
        return new Report(violations, []);
    }

    /// <summary>Re-runs every check and returns violations that were not present at baseline.</summary>
    public async Task<Report> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_baseline is null)
        {
            throw new InvalidOperationException(
                $"{nameof(BaselineAsync)} must run before the first write, otherwise inherited DEV data "
                + "cannot be told apart from violations this run caused.");
        }

        var violations = await CollectAsync(cancellationToken);
        var newViolations = violations.Where(v => !_baseline.Contains(Key(v))).ToList();
        return new Report(violations, newViolations);
    }

    private static string Key(Violation violation) => $"{violation.Check}::{violation.Detail}";

    private async Task<List<Violation>> CollectAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var violations = new List<Violation>();

        // The definition of a "workflow-driving" task is taken verbatim from StalledWorkflowWatchdog:
        // a TaskLink of role Trigger pointing at a WorkflowInstance. Inventing a competing definition
        // here would let the validator and the engine disagree about the same row.
        var drivingTasks = await (
            from link in db.TaskLinks.AsNoTracking()
            join task in db.ProjectAssignments.AsNoTracking() on link.TaskId equals task.Id
            join status in db.ProjectAssignmentStatuses.AsNoTracking() on task.StatusId equals status.Id
            where link.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
               && link.Role == TaskLinkRole.Trigger
            select new
            {
                LinkId = link.Id,
                TaskId = task.Id,
                InstanceId = (int)link.LinkedEntityId,
                status.IsOpen,
                task.AssignedToId,
                task.Title,
                task.TaskTypeId,
            }).ToListAsync(cancellationToken);

        var instances = await db.WorkflowInstances
            .AsNoTracking()
            .Select(i => new
            {
                i.Id,
                i.WorkflowDefinitionId,
                i.ProjectId,
                i.JobTypeId,
                i.Status,
                i.CurrentStageId,
                i.ParentWorkflowInstanceId,
            })
            .ToListAsync(cancellationToken);

        var instanceIds = instances.Select(i => i.Id).ToHashSet();

        var stages = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.Code,
                s.WorkflowDefinitionId,
                s.IsFinal,
                s.NodeType,
                s.SubWorkflowDefinitionId,
                s.AssignedGroupId,
            })
            .ToListAsync(cancellationToken);
        var stageById = stages.ToDictionary(s => s.Id);

        // 1. Orphan links and dangling instance references.
        foreach (var task in drivingTasks.Where(t => !instanceIds.Contains(t.InstanceId)))
        {
            violations.Add(new Violation(
                "OrphanTaskLink",
                $"TaskLink {task.LinkId} (task {task.TaskId}) points at missing WorkflowInstance "
                + $"{task.InstanceId}"));
        }

        foreach (var instance in instances.Where(i =>
            i.CurrentStageId is not null && !stageById.ContainsKey(i.CurrentStageId.Value)))
        {
            violations.Add(new Violation(
                "DanglingCurrentStage",
                $"Instance {instance.Id} references missing stage {instance.CurrentStageId}"));
        }

        // A stage must belong to the same definition as the instance sitting on it.
        foreach (var instance in instances.Where(i => i.CurrentStageId is not null))
        {
            if (stageById.TryGetValue(instance.CurrentStageId!.Value, out var stage)
                && stage.WorkflowDefinitionId != instance.WorkflowDefinitionId)
            {
                violations.Add(new Violation(
                    "CrossDefinitionStage",
                    $"Instance {instance.Id} (definition {instance.WorkflowDefinitionId}) sits on stage "
                    + $"{stage.Code} belonging to definition {stage.WorkflowDefinitionId}"));
            }
        }

        // 2. Terminal instances that still have open driving tasks.
        var terminalStatuses = new[] { WorkflowStatus.Completed, WorkflowStatus.Cancelled };
        foreach (var instance in instances.Where(i => terminalStatuses.Contains(i.Status)))
        {
            var open = drivingTasks
                .Where(t => t.InstanceId == instance.Id && t.IsOpen)
                .Select(t => t.TaskId)
                .ToList();

            if (open.Count > 0)
            {
                violations.Add(new Violation(
                    "TerminalInstanceWithOpenTasks",
                    $"Instance {instance.Id} is {instance.Status} but has open driving task(s) "
                    + $"{string.Join(",", open)}"));
            }
        }

        // 3a. Duplicate driving links: the same task linked to the same instance more than once.
        foreach (var group in drivingTasks
            .GroupBy(t => (t.InstanceId, t.TaskId))
            .Where(g => g.Count() > 1))
        {
            violations.Add(new Violation(
                "DuplicateTriggerLink",
                $"Task {group.Key.TaskId} is linked to instance {group.Key.InstanceId} "
                + $"{group.Count()} times"));
        }

        // 3b. Duplicate active tracks for the same project + definition + job type. The starter prevents
        // this; nothing detected it.
        foreach (var group in instances
            .Where(i => i.Status == WorkflowStatus.Active && i.ParentWorkflowInstanceId is null)
            .GroupBy(i => (i.ProjectId, i.WorkflowDefinitionId, i.JobTypeId))
            .Where(g => g.Count() > 1))
        {
            violations.Add(new Violation(
                "DuplicateActiveTrack",
                $"Project {group.Key.ProjectId} has {group.Count()} active root instances of definition "
                + $"{group.Key.WorkflowDefinitionId} / jobType {group.Key.JobTypeId?.ToString() ?? "none"}: "
                + string.Join(",", group.Select(i => i.Id))));
        }

        // 4. Open driving tasks whose assignee cannot act. Distinct from the stage-definition readiness
        // check, which inspects the definition rather than live tasks.
        var activeUserIds = await db.Siusers
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        var activeUsers = activeUserIds.ToHashSet();

        foreach (var task in drivingTasks.Where(t => t.IsOpen))
        {
            if (task.AssignedToId is null)
            {
                violations.Add(new Violation(
                    "UnassignedOpenTask",
                    $"Open driving task {task.TaskId} on instance {task.InstanceId} has no assignee"));
            }
            else if (!activeUsers.Contains(task.AssignedToId.Value))
            {
                violations.Add(new Violation(
                    "InactiveAssignee",
                    $"Open driving task {task.TaskId} on instance {task.InstanceId} is assigned to "
                    + $"user {task.AssignedToId} who is missing or inactive"));
            }

            if (task.TaskTypeId is null)
            {
                violations.Add(new Violation(
                    "OpenTaskWithoutTaskType",
                    $"Open driving task {task.TaskId} on instance {task.InstanceId} has no TaskTypeId, so "
                    + "no transition rule can match its result"));
            }
        }

        // 5. Parent/child integrity.
        foreach (var child in instances.Where(i => i.ParentWorkflowInstanceId is not null))
        {
            if (!instanceIds.Contains(child.ParentWorkflowInstanceId!.Value))
            {
                violations.Add(new Violation(
                    "MissingParentInstance",
                    $"Instance {child.Id} references missing parent "
                    + $"{child.ParentWorkflowInstanceId}"));
            }
        }

        // 6. Every active instance must have a way forward: an open driving task, or an active child while
        // parked on a SubWorkflow stage. Because WorkflowStatus has no Waiting value, "waiting for child"
        // has to be reconstructed from the graph rather than read from the row.
        foreach (var instance in instances.Where(i => i.Status == WorkflowStatus.Active))
        {
            if (instance.CurrentStageId is null)
            {
                violations.Add(new Violation(
                    "ActiveInstanceWithoutStage",
                    $"Instance {instance.Id} is Active with no CurrentStageId"));
                continue;
            }

            if (!stageById.TryGetValue(instance.CurrentStageId.Value, out var stage) || stage.IsFinal)
            {
                continue;
            }

            if (string.Equals(stage.NodeType, "End", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage.NodeType, "Start", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hasOpenTask = drivingTasks.Any(t => t.InstanceId == instance.Id && t.IsOpen);
            if (hasOpenTask)
            {
                continue;
            }

            var isSubWorkflowHost =
                string.Equals(stage.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase);

            if (isSubWorkflowHost)
            {
                var activeChildren = instances.Count(i =>
                    i.ParentWorkflowInstanceId == instance.Id && i.Status == WorkflowStatus.Active);

                if (activeChildren == 1)
                {
                    // Legitimately waiting. Not a violation — but note that the production watchdog
                    // cannot make this distinction (audit §2.1).
                    continue;
                }

                violations.Add(new Violation(
                    activeChildren == 0 ? "SubWorkflowHostWithoutChild" : "SubWorkflowHostWithManyChildren",
                    $"Instance {instance.Id} is parked on SubWorkflow stage {stage.Code} with "
                    + $"{activeChildren} active child instance(s)"));
                continue;
            }

            violations.Add(new Violation(
                "ActiveInstanceWithNoWayForward",
                $"Instance {instance.Id} is Active on non-terminal stage {stage.Code} "
                + $"(NodeType={stage.NodeType}) with no open driving task and no active child"));
        }

        return violations;
    }
}
