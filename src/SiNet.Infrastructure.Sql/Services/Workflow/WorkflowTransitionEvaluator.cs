using Microsoft.EntityFrameworkCore;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Evaluates <see cref="WorkflowTransitionRule"/> triggers and conditions to determine which
/// transitions from the current stage should fire. Called by <see cref="WorkflowTaskOrchestrator"/>
/// when an event occurs (task completed, status changed, sub-workflow finished, etc.).
/// </summary>
internal sealed class WorkflowTransitionEvaluator(IDbContextFactory<SiNetSQLDbContext> dbFactory)
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;

    /// <summary>
    /// Evaluates all outgoing transitions from the current stage and returns those whose trigger
    /// and condition are satisfied, ordered by priority.
    /// </summary>
    public async ValueTask<List<EvaluatedTransition>> EvaluateAsync(
        int instanceId,
        WorkflowTransitionTriggerType triggerEvent,
        TransitionEvaluationContext? context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await EvaluateAsync(db, instanceId, triggerEvent, context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared-context overload used by the atomic task-close + auto-advance path. Reads against the
    /// caller-provided <paramref name="db"/> so evaluation participates in the caller's transaction and
    /// sees its not-yet-committed writes (e.g. the just-closed task).
    /// </summary>
    public async ValueTask<List<EvaluatedTransition>> EvaluateAsync(
        SiNetSQLDbContext db,
        int instanceId,
        WorkflowTransitionTriggerType triggerEvent,
        TransitionEvaluationContext? context,
        CancellationToken ct)
    {
        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false);

        if (instance is null || instance.Status != WorkflowStatus.Active || instance.CurrentStageId is null)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Evaluator.Gate",
                $"instance={instanceId} trigger={triggerEvent} SKIPPED (status={instance?.Status.ToString() ?? "null"} stage={instance?.CurrentStageId?.ToString() ?? "null"})");
            return [];
        }

        var rules = await db.WorkflowTransitionRules
            .Include(r => r.Actions)
            .AsNoTracking()
            .Where(r => r.WorkflowDefinitionId == instance.WorkflowDefinitionId
                     && r.FromStageId == instance.CurrentStageId.Value)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rules.Count == 0) return [];

        var results = new List<EvaluatedTransition>();

        foreach (var rule in rules)
        {
            if (!TriggerMatches(rule.TriggerType, triggerEvent))
                continue;

            var conditionMet = await EvaluateConditionAsync(db, rule, instance, context, ct)
                .ConfigureAwait(false);

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Evaluator.Rule",
                $"instance={instanceId} trigger={triggerEvent} rule={rule.Id} (stage {rule.FromStageId}→{rule.ToStageId}) cond={rule.ConditionType} json={rule.ConditionJson ?? "(none)"} met={conditionMet}");

            if (!conditionMet)
                continue;

            results.Add(new EvaluatedTransition(rule, rule.EvaluationMode));
        }

        return results;
    }

    private static bool TriggerMatches(
        WorkflowTransitionTriggerType ruleTrigger,
        WorkflowTransitionTriggerType actualEvent)
    {
        if (ruleTrigger == WorkflowTransitionTriggerType.Manual)
            return actualEvent == WorkflowTransitionTriggerType.Manual;

        return ruleTrigger == actualEvent;
    }

    private static async ValueTask<bool> EvaluateConditionAsync(
        SiNetSQLDbContext db,
        WorkflowTransitionRule rule,
        WorkflowInstance instance,
        TransitionEvaluationContext? context,
        CancellationToken ct)
    {
        return rule.ConditionType switch
        {
            WorkflowTransitionConditionType.Always => true,

            WorkflowTransitionConditionType.AllTasksComplete =>
                await AreAllRequiredTasksCompleteAsync(db, instance, ct).ConfigureAwait(false),

            WorkflowTransitionConditionType.TaskStatusEquals =>
                EvaluateTaskStatus(context, rule.ConditionJson, equals: true),

            WorkflowTransitionConditionType.TaskStatusNotEquals =>
                EvaluateTaskStatus(context, rule.ConditionJson, equals: false),

            WorkflowTransitionConditionType.TaskResultEquals =>
                EvaluateTaskResult(context, rule.ConditionJson),

            WorkflowTransitionConditionType.SubWorkflowSucceeded =>
                context?.SubWorkflowSucceeded == true,

            WorkflowTransitionConditionType.SubWorkflowFailed =>
                context?.SubWorkflowSucceeded == false,

            WorkflowTransitionConditionType.ActionCompleted =>
                EvaluateActionCompleted(context, rule.ConditionJson),

            _ => false,
        };
    }

    /// <summary>
    /// Evaluates an ActionCompleted condition. ConditionJson: { "ActionCode": "...", "Outcome": "..." }.
    /// The context must carry a completed action outcome. <c>ActionCode</c> is required; <c>Outcome</c> optional.
    /// </summary>
    private static bool EvaluateActionCompleted(
        TransitionEvaluationContext? context,
        string? conditionJson)
    {
        if (context is null || string.IsNullOrWhiteSpace(conditionJson))
            return false;

        if (string.IsNullOrWhiteSpace(context.ActionOutcome))
            return false;

        if (string.IsNullOrWhiteSpace(context.ActionCode))
            return false;

        var expectedCode = ExtractStringFromJson(conditionJson, "ActionCode");
        if (string.IsNullOrWhiteSpace(expectedCode))
            return false;

        if (!string.Equals(context.ActionCode, expectedCode, StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedOutcome = ExtractStringFromJson(conditionJson, "Outcome");
        if (string.IsNullOrWhiteSpace(expectedOutcome))
            return true;

        return string.Equals(context.ActionOutcome, expectedOutcome, StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask<bool> AreAllRequiredTasksCompleteAsync(
        SiNetSQLDbContext db,
        WorkflowInstance instance,
        CancellationToken ct)
    {
        if (instance.CurrentStageId is null) return false;
        return await WorkflowStageQueries.AreAllRequiredTasksCompleteAsync(
            db, instance.Id, instance.CurrentStageId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Evaluates TaskStatusEquals / TaskStatusNotEquals. ConditionJson: {"TaskTypeId":5,"StatusId":3}.</summary>
    private static bool EvaluateTaskStatus(
        TransitionEvaluationContext? context,
        string? conditionJson,
        bool equals)
    {
        if (context is null || string.IsNullOrWhiteSpace(conditionJson))
            return false;

        if (context.ChangedTaskTypeId is null || context.ChangedTaskStatusId is null)
            return false;

        try
        {
            var json = conditionJson.Trim();
            var taskTypeId = ExtractIntFromJson(json, "TaskTypeId");
            var statusId = ExtractIntFromJson(json, "StatusId");

            if (taskTypeId is null || statusId is null) return false;

            var taskTypeMatches = context.ChangedTaskTypeId == taskTypeId;
            var statusMatches = context.ChangedTaskStatusId == statusId;

            return equals
                ? taskTypeMatches && statusMatches
                : taskTypeMatches && !statusMatches;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Evaluates TaskResultEquals. ConditionJson: {"TaskResultCode":"QuoteSent"}.</summary>
    private static bool EvaluateTaskResult(
        TransitionEvaluationContext? context,
        string? conditionJson)
    {
        if (context is null || string.IsNullOrWhiteSpace(conditionJson))
            return false;

        if (string.IsNullOrWhiteSpace(context.ChangedTaskResultCode))
            return false;

        var expected = ExtractStringFromJson(conditionJson, "TaskResultCode");
        if (string.IsNullOrWhiteSpace(expected)) return false;

        return string.Equals(context.ChangedTaskResultCode, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static int? ExtractIntFromJson(string json, string key)
    {
        var keyPattern = $"\"{key}\"";
        var idx = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var colonIdx = json.IndexOf(':', idx + keyPattern.Length);
        if (colonIdx < 0) return null;

        var start = colonIdx + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;

        var end = start;
        while (end < json.Length && char.IsDigit(json[end])) end++;

        if (end == start) return null;
        return int.TryParse(json.AsSpan(start, end - start), out var val) ? val : null;
    }

    private static string? ExtractStringFromJson(string json, string key)
    {
        var keyPattern = $"\"{key}\"";
        var idx = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var colonIdx = json.IndexOf(':', idx + keyPattern.Length);
        if (colonIdx < 0) return null;

        var openQuote = json.IndexOf('"', colonIdx + 1);
        if (openQuote < 0) return null;

        var closeQuote = json.IndexOf('"', openQuote + 1);
        if (closeQuote <= openQuote) return null;

        return json.Substring(openQuote + 1, closeQuote - openQuote - 1);
    }
}

/// <summary>Context passed to the evaluator describing the triggering event.</summary>
internal sealed record TransitionEvaluationContext
{
    /// <summary>The TaskTypeId of the task that changed (for TaskStatusChanged triggers).</summary>
    public int? ChangedTaskTypeId { get; init; }

    /// <summary>The new StatusId of the task that changed.</summary>
    public int? ChangedTaskStatusId { get; init; }

    /// <summary>The business <c>TaskResultDefinition.Code</c> recorded on the task that changed.</summary>
    public string? ChangedTaskResultCode { get; init; }

    /// <summary>Whether a linked sub-workflow succeeded (for SubWorkflow triggers).</summary>
    public bool? SubWorkflowSucceeded { get; init; }

    /// <summary>The ID of the completed sub-workflow instance.</summary>
    public int? CompletedSubWorkflowInstanceId { get; init; }

    /// <summary>The action code of an action that completed (for ActionCompleted triggers).</summary>
    public string? ActionCode { get; init; }

    /// <summary>The outcome of the completed action (e.g. <c>Succeeded</c>).</summary>
    public string? ActionOutcome { get; init; }
}

/// <summary>A transition rule that passed trigger + condition evaluation.</summary>
internal sealed record EvaluatedTransition(
    WorkflowTransitionRule Rule,
    WorkflowEvaluationMode EvaluationMode);
