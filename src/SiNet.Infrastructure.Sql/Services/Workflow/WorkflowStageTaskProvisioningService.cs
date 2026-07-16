using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Owns the canonical stage-task provisioning logic for a <see cref="WorkflowInstance"/>:
/// translating <see cref="WorkflowStageTask"/> templates into real <see cref="ProjectAssignment"/>
/// tasks, linking them via <see cref="TaskLink"/>, resolving assignees from the stage's
/// <see cref="WorkflowStageDefinition.AssignedGroupId"/>, and adding source back-links.
/// Re-homed from the legacy <c>SiNetSQL.Services.Workflow.WorkflowStageTaskProvisioningService</c>
/// onto native primitives. Depends only on <see cref="IDbContextFactory{T}"/>,
/// <see cref="WorkflowEngine"/>, and an optional <see cref="ISystemSettingsQueryService"/> so the
/// dependency graph stays acyclic (it never pulls a process-action handler).
/// </summary>
internal sealed class WorkflowStageTaskProvisioningService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly WorkflowEngine _engine;
    private readonly ISystemSettingsQueryService? _systemSettings;

    public WorkflowStageTaskProvisioningService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        WorkflowEngine engine,
        ISystemSettingsQueryService? systemSettings = null)
    {
        _dbFactory = dbFactory;
        _engine = engine;
        _systemSettings = systemSettings;
    }

    /// <summary>
    /// Walks past a Start node (if any) and provisions tasks for the resulting first real stage.
    /// Used by both top-level workflow starts and the StartSubWorkflow action handler.
    /// </summary>
    public async ValueTask<(WorkflowInstance Instance, List<ProjectAssignment> Tasks)> EnsureInitialStageTasksAsync(
        WorkflowInstance instance,
        int userId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.CurrentStageId.HasValue)
        {
            instance = await AutoAdvancePastStartNodeAsync(instance, userId, ct).ConfigureAwait(false);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        WorkflowStageDefinition? currentStage = null;
        if (instance.CurrentStageId.HasValue)
        {
            currentStage = await db.WorkflowStageDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == instance.CurrentStageId.Value, ct)
                .ConfigureAwait(false);
        }

        List<ProjectAssignment> tasks = [];
        if (currentStage is not null && string.Equals(currentStage.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
        {
            if (currentStage.SubWorkflowDefinitionId.HasValue)
            {
                var subExists = await db.WorkflowInstances
                    .AnyAsync(i => i.ParentWorkflowInstanceId == instance.Id
                                && i.WorkflowDefinitionId == currentStage.SubWorkflowDefinitionId.Value, ct)
                    .ConfigureAwait(false);

                if (!subExists)
                {
                    var maxOpen = await WorkflowOpenChildInstanceCap.ResolveMaxAsync(_systemSettings, ct).ConfigureAwait(false);
                    var (allowed, _, _, blockMessage) = await WorkflowOpenChildInstanceCap.TryAllowStartAsync(
                        db, instance.ProjectId, currentStage.SubWorkflowDefinitionId.Value, maxOpen, ct)
                        .ConfigureAwait(false);
                    if (!allowed)
                    {
                        Trace.TraceWarning($"[Provisioning] Blocked auto-start of subworkflow def={currentStage.SubWorkflowDefinitionId.Value}: {blockMessage}");
                        throw new InvalidOperationException(blockMessage);
                    }

                    Trace.TraceInformation($"[Provisioning] Initial stage {currentStage.Code} is SubWorkflow. Auto-starting subworkflow definition {currentStage.SubWorkflowDefinitionId.Value}.");
                    var subInstance = await _engine.StartAsync(
                        currentStage.SubWorkflowDefinitionId.Value,
                        instance.ProjectId,
                        instance.TriggerType,
                        triggerEntityId: instance.TriggerEntityId,
                        userId,
                        notes: $"תת-תהליך שהופעל מ-Workflow {instance.Id}, שלב {currentStage.Id} (התחלה ישירה)",
                        ct,
                        parentWorkflowInstanceId: instance.Id).ConfigureAwait(false);

                    var (_, subTasks) = await EnsureInitialStageTasksAsync(subInstance, userId, ct).ConfigureAwait(false);
                    tasks.AddRange(subTasks);
                }
            }
        }
        else
        {
            tasks = instance.CurrentStageId.HasValue
                ? await CreateStageTasksAsync(instance.Id, instance.CurrentStageId.Value, userId, ct).ConfigureAwait(false)
                : [];
        }

        return (instance, tasks);
    }

    /// <summary>
    /// If the current stage is a Start node, advances to the next stage via the first outgoing transition.
    /// </summary>
    public async ValueTask<WorkflowInstance> AutoAdvancePastStartNodeAsync(
        WorkflowInstance instance,
        int userId,
        CancellationToken ct)
    {
        if (instance.CurrentStageId is null) return instance;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var stage = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == instance.CurrentStageId, ct)
            .ConfigureAwait(false);

        if (stage is null || stage.NodeType != "Start") return instance;

        var nextRule = await db.WorkflowTransitionRules
            .AsNoTracking()
            .Where(r => r.WorkflowDefinitionId == instance.WorkflowDefinitionId
                     && r.FromStageId == instance.CurrentStageId)
            .OrderBy(r => r.Priority)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (nextRule is null)
        {
            Trace.TraceWarning($"[Provisioning] Start node {stage.Id} has no outgoing transitions.");
            return instance;
        }

        Trace.TraceInformation($"[Provisioning] Auto-advancing past Start node → stage {nextRule.ToStageId}.");
        return await _engine.AdvanceStageAsync(instance.Id, nextRule.ToStageId, userId, "מעבר אוטומטי מנקודת התחלה", ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates <see cref="ProjectAssignment"/> tasks from the active <see cref="WorkflowStageTask"/>
    /// templates for the given stage and links each to the workflow instance. Returns an empty list
    /// for SubWorkflow host stages or when no usable templates/group exist.
    /// </summary>
    public async ValueTask<List<ProjectAssignment>> CreateStageTasksAsync(
        int instanceId,
        int stageId,
        int userId,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await CreateStageTasksAsync(db, instanceId, stageId, userId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared-context overload used by the atomic task-close + auto-advance path. Provisions the new
    /// stage's tasks against the caller-provided <paramref name="db"/> so they enlist in the caller's
    /// transaction.
    /// </summary>
    public async ValueTask<List<ProjectAssignment>> CreateStageTasksAsync(
        SiNetSQLDbContext db,
        int instanceId,
        int stageId,
        int userId,
        CancellationToken ct)
    {
        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        var stageNode = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Where(s => s.Id == stageId)
            .Select(s => new { s.NodeType })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (stageNode is not null
            && string.Equals(stageNode.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var stageTasks = await db.WorkflowStageTasks
            .Include(st => st.TaskType)
            .Where(st => st.StageDefinitionId == stageId && st.IsActive)
            .OrderBy(st => st.SortOrder)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (stageTasks.Count == 0)
        {
            return await CreateGroupBasedTaskAsync(db, instance, stageId, userId, ct).ConfigureAwait(false);
        }

        var stageDef = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stageId, ct)
            .ConfigureAwait(false);

        UserGroup? stageGroup = stageDef?.AssignedGroupId.HasValue == true
            ? await LoadGroupWithActiveMembersAsync(db, stageDef.AssignedGroupId.Value, ct).ConfigureAwait(false)
            : null;

        var openStatusId = await WorkflowTaskFactory.GetOpenStatusIdAsync(db, ct).ConfigureAwait(false);
        var createdTasks = new List<ProjectAssignment>(stageTasks.Count);
        var stageTag = WorkflowConstants.BuildStageTag(stageId);
        var emailSource = await TryLoadEmailSourceAsync(db, instance, ct).ConfigureAwait(false);

        foreach (var template in stageTasks)
        {
            int? assigneeId = template.DefaultAssigneeId;
            if (!assigneeId.HasValue)
            {
                var (resolvedId, _) = TryResolveAssigneeFromGroup(stageGroup);
                assigneeId = resolvedId;
            }

            if (!assigneeId.HasValue)
            {
                var groupLabel = stageGroup?.Name ?? "(לא הוגדרה קבוצה)";
                var taskLabel = template.TaskType?.Name ?? $"#{template.TaskTypeId}";
                throw new InvalidOperationException(
                    $"[Provisioning] Cannot create stage task {template.Id} (TaskType={taskLabel}) " +
                    $"for stage {stageId}: no DefaultAssigneeId on template and group '{groupLabel}' " +
                    $"cannot resolve a default assignee.");
            }

            try
            {
                var taskTypeName = template.TaskType?.Name ?? $"משימה #{template.TaskTypeId}";

                // IX_ProjectAssignment_UniqueOpenTask: one open queued row per
                // (project, assignee, taskType, parent=null). On the office project many Proposal
                // emails share that key.
                var existingOpenTask = await db.ProjectAssignments
                    .Include(t => t.AssignmentStatus)
                    .FirstOrDefaultAsync(t =>
                        t.ProjectId == instance.ProjectId
                        && t.AssignedToId == assigneeId
                        && t.TaskTypeId == template.TaskTypeId
                        && t.ParentAssignmentId == null
                        && t.WorkPriority != null
                        && t.AssignmentStatus != null
                        && t.AssignmentStatus.IsOpen, ct)
                    .ConfigureAwait(false);

                if (existingOpenTask is not null && instance.IsProjectBound)
                {
                    // Bound workflows: legacy Tier-2 reuse (one parent + extra WorkTarget/instance links).
                    Trace.TraceWarning(
                        $"[Provisioning] Open task #{existingOpenTask.Id} already exists for " +
                        $"(Project={instance.ProjectId}, Assignee={assigneeId}, TaskType={template.TaskTypeId}). " +
                        "Reusing parent and linking this email/instance.");

                    var alreadyLinked = await db.TaskLinks.AnyAsync(l =>
                        l.TaskId == existingOpenTask.Id
                        && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                        && l.LinkedEntityId == instanceId
                        && l.Description == stageTag, ct).ConfigureAwait(false);

                    if (!alreadyLinked)
                    {
                        db.TaskLinks.Add(new TaskLink
                        {
                            TaskId = existingOpenTask.Id,
                            LinkedEntityType = TaskLinkEntityType.WorkflowInstance,
                            LinkedEntityId = instanceId,
                            Role = TaskLinkRole.Trigger,
                            Description = stageTag,
                            CreatedAtUtc = DateTime.UtcNow,
                            CreatedByUserId = userId,
                        });
                    }

                    if (string.IsNullOrWhiteSpace(existingOpenTask.Title)
                        || existingOpenTask.Title.StartsWith("WF-", StringComparison.Ordinal))
                    {
                        existingOpenTask.Title = taskTypeName;
                        db.ProjectAssignments.Update(existingOpenTask);
                    }

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await AddSourceLinkIfApplicableAsync(db, existingOpenTask.Id, instance, userId, ct)
                        .ConfigureAwait(false);

                    createdTasks.Add(existingOpenTask);
                    // TEMP WF-DEBUG
                    WorkflowDebugTrace.Step("Provisioning.TaskCreated",
                        $"instance={instanceId} stage={stageId} taskId={existingOpenTask.Id} taskTypeId={template.TaskTypeId} assignedTo={assigneeId} REUSED parent+link email={(emailSource?.Id.ToString() ?? "none")} title='{existingOpenTask.Title}'");
                    continue;
                }

                // Unbound (Proposal): each instance needs its own driving task. When the unique-open
                // slot is taken, create a non-queued shell parent + queued child (ParentAssignmentId
                // differs → index allows another open row of the same type).
                int? parentAssignmentId = null;
                if (existingOpenTask is not null && !instance.IsProjectBound)
                {
                    var shell = new ProjectAssignment
                    {
                        ProjectId = instance.ProjectId,
                        AssignedToId = assigneeId,
                        TaskTypeId = template.TaskTypeId,
                        StatusId = openStatusId,
                        ParentAssignmentId = null,
                        WorkPriority = null,
                        Title = $"{taskTypeName} — תהליך #{instanceId}",
                    };
                    db.ProjectAssignments.Add(shell);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    parentAssignmentId = shell.Id;

                    Trace.TraceWarning(
                        $"[Provisioning] Unbound collision on open task #{existingOpenTask.Id}; " +
                        $"creating child under shell #{shell.Id} for instance {instanceId}.");
                }

                var task = new ProjectAssignment
                {
                    ProjectId = instance.ProjectId,
                    AssignedToId = assigneeId,
                    TaskTypeId = template.TaskTypeId,
                    StatusId = openStatusId,
                    ParentAssignmentId = parentAssignmentId,
                    // Parent queue title = task type name; email subject lives on TaskLinks / body.
                    Title = taskTypeName,
                };

                await WorkflowTaskFactory.CreateAsync(db, task, userId,
                    link: new WorkflowTaskFactory.TaskLinkInfo(
                        TaskLinkEntityType.WorkflowInstance, instanceId, Description: stageTag),
                    eventNote: template.Notes ?? $"משימה נוצרה אוטומטית מ-Workflow (Instance={instanceId})",
                    ct: ct).ConfigureAwait(false);

                await AddSourceLinkIfApplicableAsync(db, task.Id, instance, userId, ct).ConfigureAwait(false);

                createdTasks.Add(task);
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Provisioning.TaskCreated",
                    $"instance={instanceId} stage={stageId} taskId={task.Id} taskTypeId={template.TaskTypeId} assignedTo={task.AssignedToId?.ToString() ?? "(none)"} parent={parentAssignmentId?.ToString() ?? "null"} title='{task.Title}'");
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    $"[Provisioning] failed to create task from template (Instance={instanceId}, Stage={stageId}, Template={template.Id}, TaskType={template.TaskTypeId}): {ex}");
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Provisioning.TaskCreated",
                    $"instance={instanceId} stage={stageId} template={template.Id} taskType={template.TaskTypeId} FAILED: {ex.Message}");

                // Inside the atomic close+advance transaction, swallowing would leave the
                // instance advanced with zero stage tasks (seen after OpenQuoteProject).
                if (db.Database.CurrentTransaction is not null)
                    throw;
            }
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Provisioning.Stage",
            $"instance={instanceId} stage={stageId} tasksCreated={createdTasks.Count}");

        return createdTasks;
    }

    public static async ValueTask<UserGroup?> LoadGroupWithActiveMembersAsync(
        SiNetSQLDbContext db, int groupId, CancellationToken ct)
    {
        return await db.UserGroups
            .AsNoTracking()
            .Include(g => g.Memberships)
                .ThenInclude(m => m.Siuser)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Unified assignee resolver: exactly one active member → that member; multiple active members
    /// with a <see cref="UserGroup.DefaultAssigneeId"/> that is itself active → the default; otherwise null.
    /// </summary>
    public static (int? AssigneeId, int ActiveMemberCount) TryResolveAssigneeFromGroup(UserGroup? group)
    {
        if (group is null) return (null, 0);

        var activeMembers = group.Memberships
            .Where(m => m.Siuser != null && m.Siuser.IsActive)
            .Select(m => m.Siuser)
            .ToList();

        if (activeMembers.Count == 0) return (null, 0);
        if (activeMembers.Count == 1) return (activeMembers[0].Id, 1);

        if (group.DefaultAssigneeId.HasValue
            && activeMembers.Any(u => u.Id == group.DefaultAssigneeId.Value))
        {
            return (group.DefaultAssigneeId.Value, activeMembers.Count);
        }

        return (null, activeMembers.Count);
    }

    private static async ValueTask<List<ProjectAssignment>> CreateGroupBasedTaskAsync(
        SiNetSQLDbContext db,
        WorkflowInstance instance,
        int stageId,
        int userId,
        CancellationToken ct)
    {
        var stage = await db.WorkflowStageDefinitions
            .Include(s => s.AssignedGroup)
                .ThenInclude(g => g!.Memberships)
                    .ThenInclude(m => m.Siuser)
            .FirstOrDefaultAsync(s => s.Id == stageId, ct)
            .ConfigureAwait(false);

        if (stage?.AssignedGroupId is null)
        {
            Trace.TraceInformation($"[Provisioning] No task templates and no assigned group for stage {stageId} — skipping.");
            return [];
        }

        var group = stage.AssignedGroup!;
        var activeMembers = group.Memberships
            .Where(m => m.Siuser.IsActive)
            .Select(m => m.Siuser)
            .ToList();

        if (activeMembers.Count == 0)
        {
            Trace.TraceWarning($"[Provisioning] Group '{group.Name}' has no active members — task created unassigned.");
        }

        int? assigneeId = activeMembers.Count switch
        {
            1 => activeMembers[0].Id,
            > 1 when group.DefaultAssigneeId.HasValue
                && activeMembers.Any(m => m.Id == group.DefaultAssigneeId) => group.DefaultAssigneeId,
            _ => null,
        };

        var openStatusId = await WorkflowTaskFactory.GetOpenStatusIdAsync(db, ct).ConfigureAwait(false);
        var stageTag = WorkflowConstants.BuildStageTag(stageId);

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == instance.ProjectId)
            .Select(p => new { p.NameAndNumber, p.Title })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var projectLabel = project?.NameAndNumber ?? project?.Title ?? $"פרויקט #{instance.ProjectId}";
        var emailSource = await TryLoadEmailSourceAsync(db, instance, ct).ConfigureAwait(false);
        var taskTitle = emailSource is not null
            ? BuildHumanReadableTaskTitle(stage.Name, emailSource, instance.Id, stageId, taskTypeId: 0)
            : $"{stage.Name} — {projectLabel}";

        var bodyLines = new List<string>
        {
            stageTag,
            $"שלב: {stage.Name}",
            $"קבוצה: {group.Name}",
            $"תהליך עבודה #{instance.Id}",
        };

        if (stage.Description is not null)
            bodyLines.Add($"הנחיות: {stage.Description}");

        if (emailSource is not null)
        {
            bodyLines.Add($"מייל מקור: {emailSource.Subject}");
            if (!string.IsNullOrWhiteSpace(emailSource.FromAddress))
                bodyLines.Add($"מאת: {emailSource.FromAddress}");
        }
        else if (instance.TriggerEntityId.HasValue && instance.TriggerType == WorkflowTriggerType.Email)
        {
            bodyLines.Add($"[מייל מקור: #{instance.TriggerEntityId}]");
        }

        var alreadyExists = await db.TaskLinks
            .AnyAsync(l => l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                        && l.LinkedEntityId == instance.Id
                        && l.Role == TaskLinkRole.Trigger
                        && l.Description == stageTag, ct)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            Trace.TraceInformation($"[Provisioning] Task for stage {stageId} already exists — skipping.");
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Provisioning.TaskCreated",
                $"instance={instance.Id} stage={stageId} SKIPPED (task already exists for stageTag='{stageTag}')");
            return [];
        }

        var task = new ProjectAssignment
        {
            ProjectId = instance.ProjectId,
            AssignedToId = assigneeId,
            StatusId = openStatusId,
            Title = taskTitle,
            Body = string.Join("\n", bodyLines),
            StartDate = DateTime.Now,
        };

        await WorkflowTaskFactory.CreateAsync(db, task, userId,
            link: new WorkflowTaskFactory.TaskLinkInfo(
                TaskLinkEntityType.WorkflowInstance, instance.Id, Description: stageTag),
            eventNote: $"משימה נוצרה אוטומטית מתהליך עבודה #{instance.Id}, שלב: {stage.Name}",
            ct: ct).ConfigureAwait(false);

        await AddSourceLinkIfApplicableAsync(db, task.Id, instance, userId, ct).ConfigureAwait(false);

        return [task];
    }

    private sealed record EmailSourceInfo(int Id, string Subject, string? FromAddress);

    private static async ValueTask<EmailSourceInfo?> TryLoadEmailSourceAsync(
        SiNetSQLDbContext db,
        WorkflowInstance instance,
        CancellationToken ct)
    {
        if (instance.TriggerType != WorkflowTriggerType.Email
            || instance.TriggerEntityId is not int emailId
            || emailId <= 0)
        {
            return null;
        }

        var row = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.Id == emailId)
            .Select(m => new { m.Id, m.Subject, m.FromAddress })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var subject = string.IsNullOrWhiteSpace(row.Subject) ? "(ללא נושא)" : row.Subject.Trim();
        return new EmailSourceInfo(row.Id, subject, row.FromAddress);
    }

    /// <summary>
    /// Builds a human-readable task title: "{task type} — {email subject}" (optional from),
    /// instead of the opaque WF-{instance}-S{stage}-… machine key.
    /// </summary>
    private static string BuildHumanReadableTaskTitle(
        string taskTypeOrStageName,
        EmailSourceInfo? email,
        int instanceId,
        int stageId,
        int taskTypeId)
    {
        if (email is null)
        {
            return $"WF-{instanceId}-S{stageId}-{taskTypeId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var title = string.IsNullOrWhiteSpace(email.FromAddress)
            ? $"{taskTypeOrStageName} — {email.Subject}"
            : $"{taskTypeOrStageName} — {email.Subject} ({email.FromAddress})";

        return title.Length <= 240 ? title : title[..237] + "...";
    }

    private static async ValueTask AddSourceLinkIfApplicableAsync(
        SiNetSQLDbContext db,
        int taskId,
        WorkflowInstance instance,
        int userId,
        CancellationToken ct)
    {
        if (!instance.TriggerEntityId.HasValue) return;

        TaskLinkEntityType? sourceType = instance.TriggerType switch
        {
            WorkflowTriggerType.Email => TaskLinkEntityType.EmailInboxMessage,
            _ => null,
        };

        if (sourceType is null) return;

        var entityId = instance.TriggerEntityId.Value;

        try
        {
            var exists = await db.TaskLinks.AnyAsync(l =>
                   l.TaskId == taskId
                && l.LinkedEntityType == sourceType.Value
                && l.LinkedEntityId == entityId
                && l.Role == TaskLinkRole.Source, ct).ConfigureAwait(false);

            if (!exists)
            {
                db.TaskLinks.Add(new TaskLink
                {
                    TaskId = taskId,
                    LinkedEntityType = sourceType.Value,
                    LinkedEntityId = entityId,
                    Role = TaskLinkRole.Source,
                    Description = $"מקור: {sourceType.Value} #{entityId}",
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = userId,
                });
            }

            var workTargetExists = await db.TaskLinks.AnyAsync(l =>
                   l.TaskId == taskId
                && l.LinkedEntityType == sourceType.Value
                && l.LinkedEntityId == entityId
                && l.Role == TaskLinkRole.Related, ct).ConfigureAwait(false);

            if (!workTargetExists)
            {
                db.TaskLinks.Add(new TaskLink
                {
                    TaskId = taskId,
                    LinkedEntityType = sourceType.Value,
                    LinkedEntityId = entityId,
                    Role = TaskLinkRole.Related,
                    IsWorkTarget = true,
                    WorkStatus = WorkTargetStatus.Pending,
                    Description = $"יעד עבודה: {sourceType.Value} #{entityId}",
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = userId,
                });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                $"[Provisioning] failed to add Source TaskLink (TaskId={taskId}, Instance={instance.Id}, SourceType={sourceType.Value}, EntityId={entityId}): {ex}");
        }
    }
}
