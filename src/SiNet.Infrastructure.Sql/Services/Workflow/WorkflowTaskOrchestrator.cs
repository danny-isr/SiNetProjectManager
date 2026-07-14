using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Native orchestrator bridging the <see cref="WorkflowEngine"/> (lifecycle) and the task system
/// (<see cref="ProjectAssignment"/> + <see cref="TaskLink"/>). Re-homed from the legacy
/// <c>SiNetSQL.Services.Workflow.WorkflowTaskOrchestrator</c>. On stage entry it provisions tasks
/// from templates; on task close it evaluates transitions and auto-advances when possible.
/// External callers go through the Application write port <see cref="IWorkflowCommandService"/>
/// (implemented by <see cref="NativeWorkflowCommandService"/>).
/// </summary>
internal sealed class WorkflowTaskOrchestrator(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    WorkflowEngine engine,
    WorkflowTransitionEvaluator evaluator,
    WorkflowActionExecutor actionExecutor,
    WorkflowStageTaskProvisioningService provisioning)
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;
    private readonly WorkflowEngine _engine = engine;
    private readonly WorkflowTransitionEvaluator _evaluator = evaluator;
    private readonly WorkflowActionExecutor _actionExecutor = actionExecutor;
    private readonly WorkflowStageTaskProvisioningService _provisioning = provisioning;

    // ── High-level operations ──────────────────────────────────────────────

    public async ValueTask<WorkflowStartResultDto> StartWorkflowAsync(
        int definitionId,
        int projectId,
        WorkflowTriggerType triggerType,
        int? triggerEntityId,
        int userId,
        string? notes,
        CancellationToken ct,
        bool isProjectBound = true,
        string? initialStageCode = null)
    {
        await PreflightStartAsync(definitionId, ct, initialStageCode).ConfigureAwait(false);

        var instance = await _engine.StartAsync(
            definitionId, projectId, triggerType, triggerEntityId, userId, notes, ct, isProjectBound,
            initialStageCode: initialStageCode).ConfigureAwait(false);

        var (advancedInstance, tasks) = await _provisioning.EnsureInitialStageTasksAsync(instance, userId, ct)
            .ConfigureAwait(false);
        instance = advancedInstance;

        Trace.TraceInformation($"[Orchestrator] Workflow {instance.Id} started → stage {instance.CurrentStageId}, {tasks.Count} tasks created.");

        return new WorkflowStartResultDto(instance.ToDto(), tasks.ToSummaryDtoList());
    }

    public async ValueTask<WorkflowAdvanceResultDto> AdvanceWithTasksAsync(
        int instanceId,
        int targetStageId,
        int userId,
        string? notes,
        CancellationToken ct)
    {
        await PreflightAdvanceAsync(instanceId, targetStageId, ct).ConfigureAwait(false);

        var instance = await _engine.AdvanceStageAsync(instanceId, targetStageId, userId, notes, ct)
            .ConfigureAwait(false);

        var tasks = instance.Status == WorkflowStatus.Active && instance.CurrentStageId.HasValue
            ? await CreateStageTasksAsync(instance.Id, instance.CurrentStageId.Value, userId, ct).ConfigureAwait(false)
            : [];

        Trace.TraceInformation($"[Orchestrator] Workflow {instanceId} advanced → stage {targetStageId}, {tasks.Count} tasks created.");

        if (instance.Status == WorkflowStatus.Completed && instance.ParentWorkflowInstanceId.HasValue)
        {
            await NotifyParentOfSubWorkflowCompletionAsync(instance, succeeded: true, userId, ct).ConfigureAwait(false);
        }

        return new WorkflowAdvanceResultDto(instance.ToDto(), tasks.ToSummaryDtoList());
    }

    private async ValueTask NotifyParentOfSubWorkflowCompletionAsync(
        WorkflowInstance childInstance,
        bool succeeded,
        int userId,
        CancellationToken ct)
    {
        var parentId = childInstance.ParentWorkflowInstanceId
            ?? throw new InvalidOperationException("Child instance has no ParentWorkflowInstanceId.");

        var evalContext = new TransitionEvaluationContext
        {
            SubWorkflowSucceeded = succeeded,
            CompletedSubWorkflowInstanceId = childInstance.Id,
        };

        var evaluated = await _evaluator.EvaluateAsync(
            parentId, WorkflowTransitionTriggerType.SubWorkflowCompleted, evalContext, ct).ConfigureAwait(false);

        if (evaluated.Count == 0)
        {
            Trace.TraceInformation(
                $"[Orchestrator] Sub-workflow {childInstance.Id} completed (succeeded={succeeded}) but parent {parentId} has no matching SubWorkflowCompleted transition.");
            return;
        }

        var best = evaluated[0];
        if (best.EvaluationMode != WorkflowEvaluationMode.Auto)
        {
            Trace.TraceInformation(
                $"[Orchestrator] Parent {parentId} has matching SubWorkflowCompleted transition rule {best.Rule.Id} but EvaluationMode={best.EvaluationMode}; skipping auto-advance.");
            return;
        }

        await ExecuteTransitionAsync(best.Rule, parentId, userId, ct).ConfigureAwait(false);
    }

    // ── Task creation from stage templates ─────────────────────────────────

    public ValueTask<List<ProjectAssignment>> CreateStageTasksAsync(
        int instanceId, int stageId, int userId, CancellationToken ct)
        => _provisioning.CreateStageTasksAsync(instanceId, stageId, userId, ct);

    public async ValueTask<int> ReprovisionCurrentStageTasksAsync(
        int instanceId, int userId, CancellationToken ct)
    {
        int stageId;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            var currentStageId = await db.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Id == instanceId)
                .Select(i => i.CurrentStageId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (currentStageId is not int resolvedStageId) return 0;
            stageId = resolvedStageId;
        }

        var created = await _provisioning.CreateStageTasksAsync(instanceId, stageId, userId, ct).ConfigureAwait(false);
        return created.Count;
    }

    // ── Stage completion monitoring ────────────────────────────────────────

    public async ValueTask<bool> IsStageCompleteAsync(int instanceId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.CurrentStageId is null) return false;

        return await WorkflowStageQueries.AreAllRequiredTasksCompleteAsync(
            db, instanceId, instance.CurrentStageId.Value, ct).ConfigureAwait(false);
    }

    public async ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(
        int taskId, int userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var workflowLinks = await db.TaskLinks
            .AsNoTracking()
            .Where(l => l.TaskId == taskId
                     && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                     && l.Role == TaskLinkRole.Trigger)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (workflowLinks.Count == 0) return null;

        var task = await db.ProjectAssignments
            .AsNoTracking()
            .Include(pa => pa.LastTaskResult)
            .FirstOrDefaultAsync(pa => pa.Id == taskId, ct)
            .ConfigureAwait(false);

        var evalContext = task is not null
            ? new TransitionEvaluationContext
            {
                ChangedTaskTypeId = task.TaskTypeId,
                ChangedTaskStatusId = task.StatusId,
                ChangedTaskResultCode = task.LastTaskResult?.Code,
            }
            : null;

        foreach (var link in workflowLinks)
        {
            var instanceId = (int)link.LinkedEntityId;

            var evaluated = await _evaluator.EvaluateAsync(
                instanceId, WorkflowTransitionTriggerType.AllRequiredTasksClosed, evalContext, ct).ConfigureAwait(false);

            var statusEvaluated = await _evaluator.EvaluateAsync(
                instanceId, WorkflowTransitionTriggerType.TaskStatusChanged, evalContext, ct).ConfigureAwait(false);

            evaluated.AddRange(statusEvaluated);

            if (evaluated.Count == 0)
            {
                await LogNoTransitionDiagnosticAsync(db, instanceId, taskId, task, evalContext, ct).ConfigureAwait(false);
                continue;
            }

            var best = evaluated[0];

            switch (best.EvaluationMode)
            {
                case WorkflowEvaluationMode.Auto:
                    return MapToDto(await ExecuteTransitionAsync(best.Rule, instanceId, userId, ct).ConfigureAwait(false));

                case WorkflowEvaluationMode.AutoWithConfirm:
                    var instance = await db.WorkflowInstances
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
                        .ConfigureAwait(false);

                    Trace.TraceInformation($"[Orchestrator] Transition to stage {best.Rule.ToStageId} requires confirmation for workflow {instanceId}.");

                    return new StageCompletionResultDto(
                        instanceId,
                        instance?.CurrentStageId ?? 0,
                        StageCompletionActionDto.ConfirmationRequired,
                        TargetStageId: best.Rule.ToStageId,
                        TransitionRuleId: best.Rule.Id);

                case WorkflowEvaluationMode.Manual:
                    continue;
            }
        }

        return null;
    }

    public async ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledWorkflowAsync(
        int instanceId, int userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false);

        if (instance is null || instance.Status != WorkflowStatus.Active)
            return null;

        var evaluated = await _evaluator.EvaluateAsync(
            instanceId, WorkflowTransitionTriggerType.AllRequiredTasksClosed, null, ct).ConfigureAwait(false);

        if (evaluated.Count == 0)
            return null;

        var best = evaluated[0];
        if (best.EvaluationMode == WorkflowEvaluationMode.Auto)
            return MapToDto(await ExecuteTransitionAsync(best.Rule, instanceId, userId, ct).ConfigureAwait(false));

        return null;
    }

    private async ValueTask LogNoTransitionDiagnosticAsync(
        SiNetSQLDbContext db,
        int instanceId,
        int taskId,
        ProjectAssignment? task,
        TransitionEvaluationContext? ctx,
        CancellationToken ct)
    {
        try
        {
            var instance = await db.WorkflowInstances
                .AsNoTracking()
                .Include(i => i.WorkflowDefinition)
                .Include(i => i.CurrentStage)
                .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
                .ConfigureAwait(false);
            if (instance is null) return;

            var taskTypeCode = task?.TaskTypeId is int ttid
                ? await db.TaskTypes.AsNoTracking()
                    .Where(t => t.Id == ttid).Select(t => t.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                : null;

            var resultCode = ctx?.ChangedTaskResultCode;

            Trace.TraceWarning(
                "[Orchestrator] no transition — Instance=" + instanceId +
                ", Workflow=" + (instance.WorkflowDefinition?.Code ?? "?") +
                ", CurrentStage=" + (instance.CurrentStage?.Code ?? instance.CurrentStageId?.ToString() ?? "?") +
                ", CompletedTaskId=" + taskId +
                ", TaskType=" + (taskTypeCode ?? "?") +
                ", StatusId=" + (task?.StatusId.ToString() ?? "?") +
                ", LastTaskResultId=" + (task?.LastTaskResultId?.ToString() ?? "null") +
                ", LastTaskResultCode=" + (resultCode ?? "null"));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[Orchestrator] LogNoTransitionDiagnosticAsync failed (non-fatal): {ex}");
        }
    }

    /// <summary>
    /// Shared-context / atomic variant of <see cref="CheckAndAutoAdvanceAsync"/>. Evaluates and executes
    /// auto-advance against the caller-provided <paramref name="db"/> so the whole task-close + advance
    /// participates in a single transaction (Phase 1d). On any action/advance failure it throws, so the
    /// caller's transaction rolls back — task closure and advance are all-or-nothing. Used by
    /// <c>SqlTaskCompletionService</c> when the native command service is in effect.
    /// </summary>
    public async ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceSharedAsync(
        SiNetSQLDbContext db, int taskId, int userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var workflowLinks = await db.TaskLinks
            .AsNoTracking()
            .Where(l => l.TaskId == taskId
                     && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                     && l.Role == TaskLinkRole.Trigger)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (workflowLinks.Count == 0) return null;

        var task = await db.ProjectAssignments
            .AsNoTracking()
            .Include(pa => pa.LastTaskResult)
            .FirstOrDefaultAsync(pa => pa.Id == taskId, ct)
            .ConfigureAwait(false);

        var evalContext = task is not null
            ? new TransitionEvaluationContext
            {
                ChangedTaskTypeId = task.TaskTypeId,
                ChangedTaskStatusId = task.StatusId,
                ChangedTaskResultCode = task.LastTaskResult?.Code,
            }
            : null;

        foreach (var link in workflowLinks)
        {
            var instanceId = (int)link.LinkedEntityId;

            var evaluated = await _evaluator.EvaluateAsync(
                db, instanceId, WorkflowTransitionTriggerType.AllRequiredTasksClosed, evalContext, ct).ConfigureAwait(false);

            var statusEvaluated = await _evaluator.EvaluateAsync(
                db, instanceId, WorkflowTransitionTriggerType.TaskStatusChanged, evalContext, ct).ConfigureAwait(false);

            evaluated.AddRange(statusEvaluated);

            if (evaluated.Count == 0)
                continue;

            var best = evaluated[0];

            switch (best.EvaluationMode)
            {
                case WorkflowEvaluationMode.Auto:
                    return MapToDto(await ExecuteTransitionSharedAsync(db, best.Rule, instanceId, userId, ct).ConfigureAwait(false));

                case WorkflowEvaluationMode.AutoWithConfirm:
                    var instance = await db.WorkflowInstances
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
                        .ConfigureAwait(false);

                    return new StageCompletionResultDto(
                        instanceId,
                        instance?.CurrentStageId ?? 0,
                        StageCompletionActionDto.ConfirmationRequired,
                        TargetStageId: best.Rule.ToStageId,
                        TransitionRuleId: best.Rule.Id);

                case WorkflowEvaluationMode.Manual:
                    continue;
            }
        }

        return null;
    }

    /// <summary>
    /// Shared-context transition execution. Runs the rule's actions (threading the ambient
    /// <paramref name="db"/> into DB-writing handlers), advances the stage, and provisions the new
    /// stage's tasks — all on <paramref name="db"/>. Throws on any action/advance failure so the
    /// caller's transaction rolls back (atomic). Parent notification for a completed sub-workflow runs
    /// on its own context (a separate instance's advance).
    /// </summary>
    private async ValueTask<StageCompletionResult> ExecuteTransitionSharedAsync(
        SiNetSQLDbContext db,
        WorkflowTransitionRule rule,
        int instanceId,
        int userId,
        CancellationToken ct)
    {
        var actionResults = await _actionExecutor
            .ExecuteActionsAsync(rule, instanceId, userId, ambientDb: db, ct)
            .ConfigureAwait(false);

        var failed = actionResults.FirstOrDefault(r => !r.Success);
        if (failed is not null)
        {
            throw new InvalidOperationException(
                $"Transition action {failed.ActionType} failed during atomic auto-advance (Instance={instanceId}, Rule={rule.Id}): {failed.Message}");
        }

        await PreflightAdvanceAsync(instanceId, rule.ToStageId, ct).ConfigureAwait(false);

        var instance = await _engine.AdvanceStageAsync(
            db, instanceId, rule.ToStageId, userId,
            $"מעבר אוטומטי — {rule.Name ?? rule.Label ?? "ללא שם"}", ct).ConfigureAwait(false);

        if (instance.Status == WorkflowStatus.Active && instance.CurrentStageId.HasValue)
        {
            await _provisioning.CreateStageTasksAsync(db, instance.Id, instance.CurrentStageId.Value, userId, ct)
                .ConfigureAwait(false);
        }

        if (instance.Status == WorkflowStatus.Completed && instance.ParentWorkflowInstanceId.HasValue)
        {
            await NotifyParentOfSubWorkflowCompletionAsync(instance, succeeded: true, userId, ct).ConfigureAwait(false);
        }

        Trace.TraceInformation($"[Orchestrator] (atomic) Auto-advanced workflow {instanceId} to stage {rule.ToStageId}.");

        return new StageCompletionResult(
            instanceId, rule.FromStageId, StageCompletionAction.AutoAdvanced, AdvancedInstance: instance.ToDto());
    }

    public async ValueTask<StageCompletionResult> ExecuteTransitionAsync(
        WorkflowTransitionRule rule,
        int instanceId,
        int userId,
        CancellationToken ct)
    {
        try
        {
            var actionResults = await _actionExecutor.ExecuteActionsAsync(rule, instanceId, userId, ct).ConfigureAwait(false);

            foreach (var ar in actionResults)
            {
                Trace.TraceInformation($"[Orchestrator] Action {ar.ActionType}: {(ar.Success ? "ok" : "fail")} {ar.Message}");
            }

            var result = await AdvanceWithTasksAsync(
                instanceId, rule.ToStageId, userId,
                $"מעבר אוטומטי — {rule.Name ?? rule.Label ?? "ללא שם"}", ct).ConfigureAwait(false);

            Trace.TraceInformation($"[Orchestrator] Auto-advanced workflow {instanceId} to stage {rule.ToStageId}.");

            return new StageCompletionResult(
                instanceId, rule.FromStageId, StageCompletionAction.AutoAdvanced, AdvancedInstance: result.Instance);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[Orchestrator] Transition execution failed (Instance={instanceId}, RuleId={rule.Id}, TargetStageId={rule.ToStageId}): {ex}");
            return new StageCompletionResult(instanceId, rule.FromStageId, StageCompletionAction.AutoAdvanceFailed);
        }
    }

    public async ValueTask<StageCompletionResult?> EvaluateManualTransitionsAsync(
        int instanceId, int userId, CancellationToken ct)
    {
        var evaluated = await _evaluator.EvaluateAsync(
            instanceId, WorkflowTransitionTriggerType.Manual, context: null, ct).ConfigureAwait(false);

        if (evaluated.Count == 0) return null;

        var best = evaluated[0];
        return await ExecuteTransitionAsync(best.Rule, instanceId, userId, ct).ConfigureAwait(false);
    }

    private static StageCompletionResultDto? MapToDto(StageCompletionResult? result) =>
        result is null
            ? null
            : new StageCompletionResultDto(
                result.InstanceId,
                result.CompletedStageId,
                MapToDto(result.Action),
                result.AdvancedInstance,
                result.TargetStageId,
                result.TransitionRuleId);

    private static StageCompletionActionDto MapToDto(StageCompletionAction action) => action switch
    {
        StageCompletionAction.AutoAdvanced => StageCompletionActionDto.AutoAdvanced,
        StageCompletionAction.ManualAdvanceRequired => StageCompletionActionDto.ManualAdvanceRequired,
        StageCompletionAction.AutoAdvanceFailed => StageCompletionActionDto.AutoAdvanceFailed,
        StageCompletionAction.ConfirmationRequired => StageCompletionActionDto.ConfirmationRequired,
        _ => StageCompletionActionDto.ManualAdvanceRequired,
    };

    // ── Preflight (validate before creating / advancing) ───────────────────

    private async ValueTask PreflightStartAsync(int definitionId, CancellationToken ct, string? initialStageCode = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var definition = await db.WorkflowDefinitions
            .AsNoTracking()
            .Include(d => d.Stages)
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.IsActive, ct)
            .ConfigureAwait(false)
            ?? throw new WorkflowStartPreflightException(
                $"לא ניתן לפתוח את התהליך: הגדרת תהליך {definitionId} לא נמצאה או אינה פעילה.");

        var initialStage = (string.IsNullOrEmpty(initialStageCode)
            ? definition.Stages.Where(s => s.IsInitial).OrderBy(s => s.SortOrder).FirstOrDefault()
            : definition.Stages.FirstOrDefault(s => s.Code == initialStageCode))
            ?? throw new WorkflowStartPreflightException(
                $"לא ניתן לפתוח את התהליך '{definition.Code}': לא הוגדר שלב התחלה מתאים.");

        var firstStage = initialStage;
        if (string.Equals(initialStage.NodeType, "Start", StringComparison.OrdinalIgnoreCase))
        {
            var nextRule = await db.WorkflowTransitionRules
                .AsNoTracking()
                .Where(r => r.WorkflowDefinitionId == definitionId && r.FromStageId == initialStage.Id)
                .OrderBy(r => r.Priority)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (nextRule is null)
                throw new WorkflowStartPreflightException(
                    $"לא ניתן לפתוח את התהליך '{definition.Code}': אין מעבר יוצא מנקודת ההתחלה.");

            firstStage = definition.Stages.FirstOrDefault(s => s.Id == nextRule.ToStageId)
                ?? throw new WorkflowStartPreflightException(
                    $"לא ניתן לפתוח את התהליך '{definition.Code}': שלב היעד מנקודת ההתחלה לא נמצא.");
        }

        if (firstStage.IsFinal
            || string.Equals(firstStage.NodeType, "End", StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstStage.NodeType, "Start", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var stageTasks = await db.WorkflowStageTasks
            .AsNoTracking()
            .Include(st => st.TaskType)
            .Where(st => st.StageDefinitionId == firstStage.Id && st.IsActive)
            .OrderBy(st => st.SortOrder)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (stageTasks.Count > 0)
        {
            UserGroup? group = firstStage.AssignedGroupId.HasValue
                ? await WorkflowStageTaskProvisioningService.LoadGroupWithActiveMembersAsync(db, firstStage.AssignedGroupId.Value, ct).ConfigureAwait(false)
                : null;

            foreach (var template in stageTasks)
            {
                if (template.TaskTypeId <= 0)
                    throw new WorkflowStartPreflightException(
                        $"לא ניתן לפתוח את התהליך כי לתבנית משימה בשלב הראשון ({firstStage.Name}) חסר סוג משימה תקין.");

                if (template.DefaultAssigneeId.HasValue) continue;

                var (resolvedId, _) = WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(group);
                if (!resolvedId.HasValue)
                {
                    var groupLabel = group?.Name ?? "(לא הוגדרה קבוצה)";
                    var taskLabel = template.TaskType?.Name ?? $"#{template.TaskTypeId}";
                    throw new WorkflowStartPreflightException(
                        $"לא ניתן לפתוח את התהליך כי חסרה הקצאת משתמש ברירת מחדל לשלב הראשון: " +
                        $"{firstStage.Name} / קבוצה: {groupLabel} (משימה: {taskLabel}). " +
                        $"יש להגדיר משתמש ברירת מחדל או חברות פעילה בקבוצה לפני פתיחת Workflow.");
                }
            }

            return;
        }

        if (!firstStage.AssignedGroupId.HasValue)
        {
            throw new WorkflowStartPreflightException(
                $"לא ניתן לפתוח את התהליך כי לשלב הראשון '{firstStage.Name}' לא הוגדרו תבניות משימה ולא הוקצתה קבוצה. " +
                $"יש להגדיר תבנית משימה או קבוצה אחראית לפני פתיחת Workflow.");
        }

        var stageGroup = await WorkflowStageTaskProvisioningService.LoadGroupWithActiveMembersAsync(db, firstStage.AssignedGroupId.Value, ct).ConfigureAwait(false)
            ?? throw new WorkflowStartPreflightException(
                $"לא ניתן לפתוח את התהליך כי הקבוצה האחראית לשלב '{firstStage.Name}' לא נמצאה.");

        var (resolvedGroupAssigneeId, _) = WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(stageGroup);
        if (!resolvedGroupAssigneeId.HasValue)
        {
            throw new WorkflowStartPreflightException(
                $"לא ניתן לפתוח את התהליך כי חסרה הקצאת משתמש ברירת מחדל לשלב הראשון: " +
                $"{firstStage.Name} / קבוצה: {stageGroup.Name}. " +
                $"יש להגדיר משתמש ברירת מחדל או חברות פעילה בקבוצה לפני פתיחת Workflow.");
        }
    }

    public async ValueTask PreflightAdvanceAsync(int instanceId, int targetStageId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var targetStage = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == targetStageId, ct)
            .ConfigureAwait(false)
            ?? throw new WorkflowAdvancePreflightException(
                $"לא ניתן להתקדם לשלב: שלב היעד {targetStageId} לא נמצא.");

        if (targetStage.IsFinal
            || string.Equals(targetStage.NodeType, "End", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var stageTasks = await db.WorkflowStageTasks
            .AsNoTracking()
            .Include(st => st.TaskType)
            .Where(st => st.StageDefinitionId == targetStageId && st.IsActive)
            .OrderBy(st => st.SortOrder)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (stageTasks.Count > 0)
        {
            UserGroup? group = targetStage.AssignedGroupId.HasValue
                ? await WorkflowStageTaskProvisioningService.LoadGroupWithActiveMembersAsync(db, targetStage.AssignedGroupId.Value, ct).ConfigureAwait(false)
                : null;

            foreach (var template in stageTasks)
            {
                if (template.TaskTypeId <= 0)
                    throw new WorkflowAdvancePreflightException(
                        $"לא ניתן להתקדם לשלב הבא כי לתבנית משימה בשלב היעד ({targetStage.Name}) חסר סוג משימה תקין.");

                if (template.DefaultAssigneeId.HasValue) continue;

                var (resolvedId, _) = WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(group);
                if (!resolvedId.HasValue)
                {
                    var groupLabel = group?.Name ?? "(לא הוגדרה קבוצה)";
                    var taskLabel = template.TaskType?.Name ?? $"#{template.TaskTypeId}";
                    throw new WorkflowAdvancePreflightException(
                        $"לא ניתן להתקדם לשלב הבא כי חסרה הקצאת משתמש למשימה בשלב היעד: " +
                        $"{targetStage.Name} / קבוצה: {groupLabel} (משימה: {taskLabel}). " +
                        $"יש להגדיר משתמש ברירת מחדל או חברות פעילה בקבוצה.");
                }
            }

            return;
        }

        if (targetStage.AssignedGroupId.HasValue)
        {
            var stageGroup = await WorkflowStageTaskProvisioningService.LoadGroupWithActiveMembersAsync(db, targetStage.AssignedGroupId.Value, ct).ConfigureAwait(false)
                ?? throw new WorkflowAdvancePreflightException(
                    $"לא ניתן להתקדם לשלב הבא כי הקבוצה האחראית לשלב '{targetStage.Name}' לא נמצאה.");

            var (resolvedGroupAssigneeId, _) = WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(stageGroup);
            if (!resolvedGroupAssigneeId.HasValue)
            {
                throw new WorkflowAdvancePreflightException(
                    $"לא ניתן להתקדם לשלב הבא כי חסרה הקצאת משתמש לשלב היעד: " +
                    $"{targetStage.Name} / קבוצה: {stageGroup.Name}. " +
                    $"יש להגדיר משתמש ברירת מחדל או חברות פעילה בקבוצה.");
            }
        }
    }
}

/// <summary>
/// Thrown when a workflow's first real stage cannot produce a valid opening task. No
/// <see cref="WorkflowInstance"/> is created. The message is user-facing.
/// </summary>
public sealed class WorkflowStartPreflightException : InvalidOperationException
{
    public WorkflowStartPreflightException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a target stage cannot produce a valid opening task. The stage is not advanced.
/// The message is user-facing.
/// </summary>
public sealed class WorkflowAdvancePreflightException : InvalidOperationException
{
    public WorkflowAdvancePreflightException(string message) : base(message) { }
}

internal enum StageCompletionAction
{
    AutoAdvanced,
    ManualAdvanceRequired,
    AutoAdvanceFailed,
    ConfirmationRequired,
}

internal sealed record StageCompletionResult(
    int InstanceId,
    int CompletedStageId,
    StageCompletionAction Action,
    WorkflowInstanceDto? AdvancedInstance = null,
    int? TargetStageId = null,
    int? TransitionRuleId = null);
