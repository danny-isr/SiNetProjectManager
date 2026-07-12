using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Read-only EF implementation of <see cref="IWorkflowClosedViewerQueryService"/>.
/// Never calls SaveChanges.
/// </summary>
public sealed class SqlWorkflowClosedViewerQueryService : IWorkflowClosedViewerQueryService
{
    private static readonly HashSet<string> KnownNodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stage", "Decision", "Fork", "Join", "Start", "End", "SubWorkflow",
    };

    private static readonly HashSet<string> SystemWorkflowCodes = new(StringComparer.Ordinal)
    {
        WorkflowCodes.PlanningWorkflow,
        WorkflowCodes.Review,
        WorkflowCodes.MaterialIntake,
        WorkflowCodes.Proposal,
        WorkflowCodes.Opinion,
    };

    private static readonly HashSet<string> SystemStageCodes = CollectStageCodes();
    private static readonly HashSet<string> ProjectStatusCodeSet = CollectStringConstants(typeof(ProjectStatusCodes));
    private static readonly HashSet<string> TaskResultCodeSet = CollectStringConstants(typeof(TaskResultCodes));

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlWorkflowClosedViewerQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowDefinitionGraphDto>> GetDefinitionGraphsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var definitions = await db.WorkflowDefinitions
            .AsNoTracking()
            .Include(d => d.Stages.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.StageTasks.OrderBy(st => st.SortOrder))
                    .ThenInclude(st => st.TaskType)
            .Include(d => d.Stages)
                .ThenInclude(s => s.StageTasks)
                    .ThenInclude(st => st.DefaultAssignee)
            .Include(d => d.Stages)
                .ThenInclude(s => s.AssignedGroup)
            .Include(d => d.Stages)
                .ThenInclude(s => s.SubWorkflowDefinition)
            .Include(d => d.TransitionRules)
                .ThenInclude(r => r.Actions.OrderBy(a => a.SortOrder))
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskResultNames = await db.TaskResultDefinitions
            .AsNoTracking()
            .Select(r => new { r.Code, r.Name })
            .ToDictionaryAsync(r => r.Code, r => r.Name, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        return definitions.Select(d => MapDefinition(d, taskResultNames)).ToList();
    }

    /// <inheritdoc />
    public Task<WorkflowClosedWorldCatalogDto> GetCatalogsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var catalog = new WorkflowClosedWorldCatalogDto(
            NodeTypes: KnownNodeTypes.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ActionTypes: Enum.GetNames<WorkflowTransitionActionType>().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            TriggerTypes: Enum.GetNames<WorkflowTransitionTriggerType>().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ConditionTypes: Enum.GetNames<WorkflowTransitionConditionType>().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            EvaluationModes: Enum.GetNames<WorkflowEvaluationMode>().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ProjectStatusCodes: ProjectStatusCodeSet.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            TaskResultCodes: TaskResultCodeSet.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            SystemWorkflowCodes: SystemWorkflowCodes.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            SystemStageCodes: SystemStageCodes.OrderBy(x => x, StringComparer.Ordinal).ToList());

        return Task.FromResult(catalog);
    }

    private static WorkflowDefinitionGraphDto MapDefinition(
        WorkflowDefinition def,
        IReadOnlyDictionary<string, string> taskResultNames)
    {
        var stages = def.Stages.OrderBy(s => s.SortOrder).ToList();
        var stageById = stages.ToDictionary(s => s.Id);

        var stageDtos = stages.Select(s => new WorkflowStageGraphDto(
            s.Id,
            s.Code,
            s.Name,
            s.Description,
            s.SortOrder,
            s.IsInitial,
            s.IsFinal,
            s.NodeType,
            KnownNodeTypes.Contains(s.NodeType),
            SystemStageCodes.Contains(s.Code),
            s.AssignedGroup?.Name,
            s.AssignedGroup?.Code,
            s.SubWorkflowDefinition?.Name,
            s.SubWorkflowDefinition?.Code,
            s.CanvasX,
            s.CanvasY,
            s.StageTasks.OrderBy(t => t.SortOrder).Select(st => MapStageTask(st, taskResultNames)).ToList())).ToList();

        var transitionDtos = def.TransitionRules
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .Select(r => MapTransition(r, stageById, taskResultNames))
            .ToList();

        return new WorkflowDefinitionGraphDto(
            def.Id,
            def.Code,
            def.Name,
            def.Description,
            def.IsActive,
            SystemWorkflowCodes.Contains(def.Code),
            stageDtos,
            transitionDtos);
    }

    private static WorkflowTransitionGraphDto MapTransition(
        WorkflowTransitionRule rule,
        IReadOnlyDictionary<int, WorkflowStageDefinition> stageById,
        IReadOnlyDictionary<string, string> taskResultNames)
    {
        stageById.TryGetValue(rule.FromStageId, out var from);
        stageById.TryGetValue(rule.ToStageId, out var to);

        var actions = rule.Actions
            .OrderBy(a => a.SortOrder)
            .Select(a =>
            {
                var configStatus = TryReadJsonString(a.ConfigJson, "ProjectStatusCode")
                    ?? TryReadJsonString(a.ConfigJson, "StatusCode");
                var configResult = TryReadJsonString(a.ConfigJson, "TaskResultCode")
                    ?? TryReadJsonString(a.ConfigJson, "ResultCode");
                taskResultNames.TryGetValue(configResult ?? string.Empty, out var configResultName);
                return new WorkflowTransitionActionGraphDto(
                    a.ActionType.ToString(),
                    Enum.IsDefined(a.ActionType),
                    a.ActionCode,
                    a.ConfigJson,
                    configStatus,
                    configStatus is null || ProjectStatusCodeSet.Contains(configStatus),
                    configResult,
                    string.IsNullOrEmpty(configResult) ? null : configResultName,
                    configResult is null || TaskResultCodeSet.Contains(configResult),
                    a.SortOrder);
            })
            .ToList();

        var conditionResult = TryReadJsonString(rule.ConditionJson, "TaskResultCode")
            ?? TryReadJsonString(rule.ConditionJson, "ResultCode");
        taskResultNames.TryGetValue(conditionResult ?? string.Empty, out var conditionResultName);

        return new WorkflowTransitionGraphDto(
            rule.Id,
            rule.Name ?? rule.Label,
            rule.FromStageId,
            rule.ToStageId,
            from?.Name ?? $"#{rule.FromStageId}",
            to?.Name ?? $"#{rule.ToStageId}",
            rule.TriggerType.ToString(),
            Enum.IsDefined(rule.TriggerType),
            rule.ConditionType.ToString(),
            Enum.IsDefined(rule.ConditionType),
            rule.EvaluationMode.ToString(),
            Enum.IsDefined(rule.EvaluationMode),
            rule.Priority,
            rule.ConditionJson,
            conditionResult,
            string.IsNullOrEmpty(conditionResult) ? null : conditionResultName,
            conditionResult is null || TaskResultCodeSet.Contains(conditionResult),
            actions);
    }

    private static WorkflowStageTaskGraphDto MapStageTask(
        WorkflowStageTask st,
        IReadOnlyDictionary<string, string> taskResultNames)
    {
        var code = st.TaskType?.Code ?? string.Empty;
        var interaction = string.IsNullOrEmpty(code) ? null : ReviewTaskInteractionRegistry.TryGet(code);
        var allowedCodes = interaction?.AllowedTaskResultCodes?.ToList() ?? new List<string>();
        var allowed = allowedCodes
            .Select(c =>
            {
                taskResultNames.TryGetValue(c, out var name);
                return new WorkflowLabeledCodeDto(c, name);
            })
            .ToList();
        return new WorkflowStageTaskGraphDto(
            st.Id,
            st.StageDefinitionId,
            st.SortOrder,
            st.IsRequired,
            st.Notes,
            st.TaskType?.Name ?? "(ללא סוג)",
            code,
            st.DefaultAssignee?.Name,
            interaction is not null,
            interaction?.OpenMode.ToString(),
            interaction?.ComponentKey,
            allowed);
    }

    private static string? TryReadJsonString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // malformed legacy payloads — surfaced via orphan flags when needed
        }

        return null;
    }

    private static HashSet<string> CollectStageCodes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in new[]
                 {
                     typeof(PlanningStageCodes),
                     typeof(ReviewStageCodes),
                     typeof(MaterialStageCodes),
                     typeof(ProposalStageCodes),
                     typeof(OpinionStageCodes),
                 })
        {
            foreach (var code in CollectStringConstants(type))
            {
                set.Add(code);
            }
        }

        return set;
    }

    private static HashSet<string> CollectStringConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);
}
