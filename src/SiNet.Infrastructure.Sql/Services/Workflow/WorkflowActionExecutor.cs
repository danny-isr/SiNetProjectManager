using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Actions;
using SiNet.Infrastructure.Sql.Services.Actions;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Executes the <see cref="WorkflowTransitionAction"/> list attached to a transition rule after
/// the transition fires. Native re-home of the legacy <c>WorkflowActionExecutor</c>: instead of
/// the legacy <c>IProcessActionDispatcher</c>, it maps each action to its code via
/// <see cref="WorkflowTransitionActionCodeMapper"/> and dispatches through the native
/// <see cref="IProcessActionService"/>. There is no silent fallback: a missing handler
/// (<see cref="ActionExecutionStatus.NotSupported"/>) or a failure is surfaced as
/// <c>Success=false</c>; <see cref="ActionExecutionStatus.NoOp"/> is a documented success.
/// </summary>
internal sealed class WorkflowActionExecutor(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IProcessActionService processActions,
    IAppLogger? logger = null)
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;
    private readonly IProcessActionService _processActions = processActions;
    private readonly IAppLogger? _logger = logger;

    /// <summary>
    /// Executes all actions defined on the given transition rule, in <see cref="WorkflowTransitionAction.SortOrder"/>.
    /// </summary>
    public ValueTask<List<ActionExecutionResult>> ExecuteActionsAsync(
        WorkflowTransitionRule rule,
        int instanceId,
        int userId,
        CancellationToken ct)
        => ExecuteActionsAsync(rule, instanceId, userId, ambientDb: null, ct);

    /// <summary>
    /// Shared-context overload. When <paramref name="ambientDb"/> is supplied, it is threaded to each
    /// DB-writing handler (via <see cref="WorkflowActionHelpers.AmbientDbContextKey"/>) so the actions
    /// enlist in the caller's atomic task-close + auto-advance transaction rather than opening their own
    /// context.
    /// </summary>
    public async ValueTask<List<ActionExecutionResult>> ExecuteActionsAsync(
        WorkflowTransitionRule rule,
        int instanceId,
        int userId,
        SiNetSQLDbContext? ambientDb,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var results = new List<ActionExecutionResult>();

        var actions = rule.Actions
            .OrderBy(a => a.SortOrder)
            .ToList();

        if (actions.Count == 0) return results;

        int? projectId = null;
        try
        {
            if (ambientDb is not null)
            {
                projectId = await ambientDb.WorkflowInstances
                    .AsNoTracking()
                    .Where(i => i.Id == instanceId && i.IsProjectBound)
                    .Select(i => (int?)i.ProjectId)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                projectId = await db.WorkflowInstances
                    .AsNoTracking()
                    .Where(i => i.Id == instanceId && i.IsProjectBound)
                    .Select(i => (int?)i.ProjectId)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(
                $"[WorkflowAction] outcome=Failed kind=ResolveProjectId instance={instanceId} detail={ex.Message}",
                ex);
        }

        foreach (var action in actions)
        {
            ActionExecutionResult result;
            try
            {
                var command = BuildCommand(action, rule, instanceId, userId, projectId, ambientDb);
                var dispatchResult = await _processActions.DispatchAsync(command, ct).ConfigureAwait(false);
                result = TranslateResult(action, dispatchResult);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[WorkflowAction] outcome=Failed instance={instanceId} action={action.Id} type={action.ActionType} detail={ex.Message}",
                    ex);
                result = new ActionExecutionResult(action.ActionType, Success: false, Message: ex.Message);
            }

            results.Add(result);
        }

        return results;
    }

    private static ActionExecutionCommand BuildCommand(
        WorkflowTransitionAction action,
        WorkflowTransitionRule rule,
        int instanceId,
        int userId,
        int? projectId,
        SiNetSQLDbContext? ambientDb)
    {
        var actionCode = WorkflowTransitionActionCodeMapper.MapFromWorkflowTransitionActionType(action.ActionType);

        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ActionExecutionDataKeys.ConfigJson] = action.ConfigJson,
            [ActionExecutionDataKeys.FromStageId] = rule.FromStageId,
            [ActionExecutionDataKeys.ToStageId] = rule.ToStageId,
        };

        if (ambientDb is not null)
            data[WorkflowActionHelpers.AmbientDbContextKey] = ambientDb;

        return new ActionExecutionCommand(
            ActionCode: actionCode,
            ProjectId: projectId,
            WorkflowInstanceId: instanceId,
            TaskId: null,
            UserId: userId,
            Data: data);
    }

    private static ActionExecutionResult TranslateResult(
        WorkflowTransitionAction action,
        ActionExecutionResultDto result)
    {
        return result.Status switch
        {
            ActionExecutionStatus.Completed =>
                new ActionExecutionResult(action.ActionType, Success: true, Message: result.Message ?? "הפעולה הושלמה"),

            ActionExecutionStatus.NoOp =>
                new ActionExecutionResult(action.ActionType, Success: true, Message: result.Message ?? "ללא פעולה"),

            ActionExecutionStatus.NotSupported =>
                new ActionExecutionResult(action.ActionType, Success: false,
                    Message: result.Message ?? $"לא נרשם handler לפעולה {action.ActionType}"),

            ActionExecutionStatus.Failed =>
                new ActionExecutionResult(action.ActionType, Success: false, Message: result.Message ?? "הפעולה נכשלה"),

            _ =>
                new ActionExecutionResult(action.ActionType, Success: false,
                    Message: $"סטטוס לא נתמך עבור פעולת מעבר: {result.Status} ({result.Message})"),
        };
    }
}

/// <summary>Result of executing a single transition action.</summary>
internal sealed record ActionExecutionResult(
    WorkflowTransitionActionType ActionType,
    bool Success,
    string Message);
