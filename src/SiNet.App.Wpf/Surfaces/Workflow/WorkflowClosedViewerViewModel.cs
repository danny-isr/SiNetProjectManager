using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// Native closed-world workflow viewer — catalog-bound, dry-run only, never persists.
/// </summary>
public sealed class WorkflowClosedViewerViewModel : ObservableObject
{
    private readonly IWorkflowClosedViewerQueryService _query;
    private WorkflowClosedWorldCatalogDto _catalog = EmptyCatalog();
    private WorkflowViewerNode? _selectedNode;
    private string _statusMessage = "טוען…";
    private string? _orphanWarnings;
    private string _dryRunActionType = "CreateStageTasks";
    private string? _dryRunTaskResultCode;
    private string? _dryRunProjectStatusCode;
    private string? _dryRunNodeType = "Stage";
    private HashSet<string> _systemWorkflowCodes = new(StringComparer.Ordinal);
    private HashSet<string> _systemStageCodes = new(StringComparer.Ordinal);
    private HashSet<string> _projectStatusCodes = new(StringComparer.Ordinal);
    private HashSet<string> _taskResultCodes = new(StringComparer.Ordinal);
    private HashSet<string> _knownNodeTypes = new(StringComparer.OrdinalIgnoreCase);

    public WorkflowClosedViewerViewModel()
        : this(new DesignWorkflowClosedViewerQueryService())
    {
    }

    public WorkflowClosedViewerViewModel(IWorkflowClosedViewerQueryService query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
        ApplyCatalog(_catalog);
    }

    public ObservableCollection<WorkflowViewerNode> Roots { get; } = new();

    public IReadOnlyList<string> ActionTypeCatalog { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> TriggerTypeCatalog { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> ConditionTypeCatalog { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> EvaluationModeCatalog { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> NodeTypeCatalog { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> ProjectStatusCatalog { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> TaskResultCatalog { get; private set; } = Array.Empty<string>();

    public ObservableCollection<string> DryRunAllowedTaskResults { get; } = new();

    public ICommand RefreshCommand { get; }

    public string Title => "צפייה בתהליכים (סגור)";

    public string BannerText => "מצב צפייה — שינויים לא נשמרים. כל הערכים נבחרים מרשימות סגורות בלבד.";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string? OrphanWarnings
    {
        get => _orphanWarnings;
        private set
        {
            if (SetField(ref _orphanWarnings, value))
            {
                OnPropertyChanged(nameof(HasOrphanWarnings));
            }
        }
    }

    public bool HasOrphanWarnings => !string.IsNullOrWhiteSpace(OrphanWarnings);

    public WorkflowViewerNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
            {
                return;
            }

            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailTitle));
            OnPropertyChanged(nameof(IsWorkflowSelected));
            OnPropertyChanged(nameof(IsStageSelected));
            OnPropertyChanged(nameof(IsTransitionSelected));
            OnPropertyChanged(nameof(IsStageTaskSelected));
            OnPropertyChanged(nameof(SelectedWorkflow));
            OnPropertyChanged(nameof(SelectedStage));
            OnPropertyChanged(nameof(SelectedTransition));
            OnPropertyChanged(nameof(SelectedStageTask));
            UpdateOrphanWarnings();
            SyncDryRunFromSelection();
        }
    }

    public string DetailTitle => SelectedNode?.DetailTitle ?? "בחר פריט מהעץ…";

    public bool IsWorkflowSelected => SelectedNode is WorkflowDefViewerNode;
    public bool IsStageSelected => SelectedNode is WorkflowStageViewerNode;
    public bool IsTransitionSelected => SelectedNode is WorkflowTransitionViewerNode;
    public bool IsStageTaskSelected => SelectedNode is WorkflowStageTaskViewerNode;

    public WorkflowDefViewerNode? SelectedWorkflow => SelectedNode as WorkflowDefViewerNode;
    public WorkflowStageViewerNode? SelectedStage => SelectedNode as WorkflowStageViewerNode;
    public WorkflowTransitionViewerNode? SelectedTransition => SelectedNode as WorkflowTransitionViewerNode;
    public WorkflowStageTaskViewerNode? SelectedStageTask => SelectedNode as WorkflowStageTaskViewerNode;

    public string DryRunActionType
    {
        get => _dryRunActionType;
        set
        {
            if (!ActionTypeCatalog.Contains(value))
            {
                return;
            }

            if (SetField(ref _dryRunActionType, value))
            {
                StatusMessage = $"תצוגה מקדימה: ActionType={value} (לא נשמר)";
            }
        }
    }

    public string? DryRunTaskResultCode
    {
        get => _dryRunTaskResultCode;
        set
        {
            var allowed = DryRunAllowedTaskResults.Count > 0
                ? DryRunAllowedTaskResults
                : TaskResultCatalog;
            if (value is not null && !allowed.Contains(value))
            {
                return;
            }

            if (SetField(ref _dryRunTaskResultCode, value))
            {
                StatusMessage = value is null
                    ? "תצוגה מקדימה: TaskResult נוקה (לא נשמר)"
                    : $"תצוגה מקדימה: TaskResult={value} (לא נשמר)";
            }
        }
    }

    public string? DryRunProjectStatusCode
    {
        get => _dryRunProjectStatusCode;
        set
        {
            if (value is not null && !_projectStatusCodes.Contains(value))
            {
                return;
            }

            if (SetField(ref _dryRunProjectStatusCode, value))
            {
                StatusMessage = value is null
                    ? "תצוגה מקדימה: ProjectStatus נוקה (לא נשמר)"
                    : $"תצוגה מקדימה: ProjectStatus={value} (לא נשמר)";
            }
        }
    }

    public string? DryRunNodeType
    {
        get => _dryRunNodeType;
        set
        {
            if (value is not null && !_knownNodeTypes.Contains(value))
            {
                return;
            }

            if (SetField(ref _dryRunNodeType, value))
            {
                StatusMessage = $"תצוגה מקדימה: NodeType={value} (לא נשמר)";
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = "טוען תהליכים…";
        try
        {
            _catalog = await _query.GetCatalogsAsync(cancellationToken).ConfigureAwait(true);
            ApplyCatalog(_catalog);

            var graphs = await _query.GetDefinitionGraphsAsync(cancellationToken).ConfigureAwait(true);

            Roots.Clear();
            foreach (var def in graphs)
            {
                Roots.Add(BuildWorkflowNode(def));
            }

            SelectedNode = Roots.FirstOrDefault();
            StatusMessage = $"נטענו {graphs.Count} תהליכים · צפייה בלבד · ללא שמירה";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
        }
    }

    private void ApplyCatalog(WorkflowClosedWorldCatalogDto catalog)
    {
        ActionTypeCatalog = catalog.ActionTypes;
        TriggerTypeCatalog = catalog.TriggerTypes;
        ConditionTypeCatalog = catalog.ConditionTypes;
        EvaluationModeCatalog = catalog.EvaluationModes;
        NodeTypeCatalog = catalog.NodeTypes;
        ProjectStatusCatalog = catalog.ProjectStatusCodes;
        TaskResultCatalog = catalog.TaskResultCodes;
        _systemWorkflowCodes = catalog.SystemWorkflowCodes.ToHashSet(StringComparer.Ordinal);
        _systemStageCodes = catalog.SystemStageCodes.ToHashSet(StringComparer.Ordinal);
        _projectStatusCodes = catalog.ProjectStatusCodes.ToHashSet(StringComparer.Ordinal);
        _taskResultCodes = catalog.TaskResultCodes.ToHashSet(StringComparer.Ordinal);
        _knownNodeTypes = catalog.NodeTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        OnPropertyChanged(nameof(ActionTypeCatalog));
        OnPropertyChanged(nameof(TriggerTypeCatalog));
        OnPropertyChanged(nameof(ConditionTypeCatalog));
        OnPropertyChanged(nameof(EvaluationModeCatalog));
        OnPropertyChanged(nameof(NodeTypeCatalog));
        OnPropertyChanged(nameof(ProjectStatusCatalog));
        OnPropertyChanged(nameof(TaskResultCatalog));
    }

    private WorkflowDefViewerNode BuildWorkflowNode(WorkflowDefinitionGraphDto def)
    {
        var stages = def.Stages.OrderBy(s => s.SortOrder).ToList();
        var node = new WorkflowDefViewerNode(
            def.Id,
            def.Code,
            def.Name,
            def.Description,
            def.IsActive,
            def.IsSystem || _systemWorkflowCodes.Contains(def.Code),
            stages.Count,
            def.Transitions.Count,
            stages.Sum(s => s.StageTasks.Count));

        foreach (var stage in stages)
        {
            node.Children.Add(BuildStageNode(def, stage, stages));
        }

        return node;
    }

    private WorkflowStageViewerNode BuildStageNode(
        WorkflowDefinitionGraphDto def,
        WorkflowStageGraphDto stage,
        IReadOnlyList<WorkflowStageGraphDto> allStages)
    {
        var stageNode = new WorkflowStageViewerNode(
            stage.Id,
            stage.Code,
            stage.Name,
            stage.Description,
            stage.SortOrder,
            stage.IsInitial,
            stage.IsFinal,
            stage.NodeType,
            stage.NodeTypeKnown || _knownNodeTypes.Contains(stage.NodeType),
            stage.IsSystem || _systemStageCodes.Contains(stage.Code),
            stage.AssignedGroupName,
            stage.AssignedGroupCode,
            stage.SubWorkflowName,
            stage.SubWorkflowCode);

        var outgoing = def.Transitions.Where(r => r.FromStageId == stage.Id).ToList();
        var forward = outgoing
            .Where(r => allStages.FirstOrDefault(s => s.Id == r.ToStageId) is { } to && to.SortOrder > stage.SortOrder)
            .ToList();
        var backward = outgoing
            .Where(r => allStages.FirstOrDefault(s => s.Id == r.ToStageId) is { } to && to.SortOrder < stage.SortOrder)
            .ToList();

        var fwdGroup = new WorkflowTransGroupViewerNode(true, forward.Count);
        foreach (var rule in forward)
        {
            fwdGroup.Children.Add(BuildTransitionNode(rule));
        }

        var bwdGroup = new WorkflowTransGroupViewerNode(false, backward.Count);
        foreach (var rule in backward)
        {
            bwdGroup.Children.Add(BuildTransitionNode(rule));
        }

        var taskGroup = new WorkflowTaskGroupViewerNode(stage.StageTasks.Count);
        foreach (var st in stage.StageTasks.OrderBy(t => t.SortOrder))
        {
            taskGroup.Children.Add(BuildStageTaskNode(st));
        }

        stageNode.Children.Add(fwdGroup);
        stageNode.Children.Add(bwdGroup);
        stageNode.Children.Add(taskGroup);
        return stageNode;
    }

    private static WorkflowTransitionViewerNode BuildTransitionNode(WorkflowTransitionGraphDto rule)
    {
        var actions = rule.Actions
            .Select(a => new WorkflowActionViewerItem(
                a.ActionType,
                a.ActionTypeKnown,
                a.ActionCode,
                a.ConfigJson,
                a.ConfigProjectStatusCode,
                a.ConfigProjectStatusOk,
                a.ConfigTaskResultCode,
                a.ConfigTaskResultOk,
                a.SortOrder))
            .ToList();

        return new WorkflowTransitionViewerNode(
            rule.Id,
            rule.Name,
            rule.FromStageName,
            rule.ToStageName,
            rule.TriggerType,
            rule.TriggerTypeKnown,
            rule.ConditionType,
            rule.ConditionTypeKnown,
            rule.EvaluationMode,
            rule.EvaluationModeKnown,
            rule.Priority,
            rule.ConditionJson,
            rule.ConditionTaskResultCode,
            rule.ConditionTaskResultOk,
            actions);
    }

    private static WorkflowStageTaskViewerNode BuildStageTaskNode(WorkflowStageTaskGraphDto st) =>
        new(
            st.Id,
            st.TaskTypeName,
            st.TaskTypeCode,
            st.AssigneeDisplay,
            st.IsRequired,
            st.SortOrder,
            st.Notes,
            st.HasInteraction,
            st.OpenMode,
            st.ComponentKey,
            st.AllowedTaskResultCodes);

    private void SyncDryRunFromSelection()
    {
        DryRunAllowedTaskResults.Clear();
        if (SelectedStageTask is { AllowedTaskResultCodes.Count: > 0 } task)
        {
            foreach (var code in task.AllowedTaskResultCodes)
            {
                DryRunAllowedTaskResults.Add(code);
            }
        }
        else
        {
            foreach (var code in TaskResultCatalog)
            {
                DryRunAllowedTaskResults.Add(code);
            }
        }

        if (SelectedTransition?.Actions.FirstOrDefault() is { } firstAction)
        {
            if (firstAction.ActionTypeKnown && ActionTypeCatalog.Contains(firstAction.ActionType))
            {
                DryRunActionType = firstAction.ActionType;
            }

            if (firstAction.ConfigTaskResultCode is not null
                && DryRunAllowedTaskResults.Contains(firstAction.ConfigTaskResultCode))
            {
                DryRunTaskResultCode = firstAction.ConfigTaskResultCode;
            }

            if (firstAction.ConfigProjectStatusCode is not null
                && _projectStatusCodes.Contains(firstAction.ConfigProjectStatusCode))
            {
                DryRunProjectStatusCode = firstAction.ConfigProjectStatusCode;
            }
        }

        if (SelectedStage is { } stage && _knownNodeTypes.Contains(stage.NodeType))
        {
            DryRunNodeType = stage.NodeType;
        }
    }

    private void UpdateOrphanWarnings()
    {
        var warnings = new List<string>();
        switch (SelectedNode)
        {
            case WorkflowStageViewerNode stage when !stage.NodeTypeKnown:
                warnings.Add($"NodeType '{stage.NodeType}' אינו בקטלוג הסגור.");
                break;
            case WorkflowTransitionViewerNode tr:
                if (!tr.TriggerTypeKnown)
                {
                    warnings.Add("TriggerType לא מוכר בקטלוג.");
                }

                if (!tr.ConditionTypeKnown)
                {
                    warnings.Add("ConditionType לא מוכר בקטלוג.");
                }

                if (!tr.ConditionTaskResultOk && tr.ConditionTaskResultCode is not null)
                {
                    warnings.Add($"ConditionJson TaskResult '{tr.ConditionTaskResultCode}' מחוץ לקטלוג.");
                }

                foreach (var a in tr.Actions)
                {
                    if (!a.ActionTypeKnown)
                    {
                        warnings.Add($"ActionType לא מוכר (SortOrder={a.SortOrder}).");
                    }

                    if (!a.ConfigProjectStatusOk && a.ConfigProjectStatusCode is not null)
                    {
                        warnings.Add($"ConfigJson ProjectStatus '{a.ConfigProjectStatusCode}' מחוץ לקטלוג.");
                    }

                    if (!a.ConfigTaskResultOk && a.ConfigTaskResultCode is not null)
                    {
                        warnings.Add($"ConfigJson TaskResult '{a.ConfigTaskResultCode}' מחוץ לקטלוג.");
                    }
                }

                break;
        }

        OrphanWarnings = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
    }

    private static WorkflowClosedWorldCatalogDto EmptyCatalog() =>
        new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
}

/// <summary>Design-time / parameterless stub — empty graphs.</summary>
internal sealed class DesignWorkflowClosedViewerQueryService : IWorkflowClosedViewerQueryService
{
    public Task<IReadOnlyList<WorkflowDefinitionGraphDto>> GetDefinitionGraphsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkflowDefinitionGraphDto>>(Array.Empty<WorkflowDefinitionGraphDto>());

    public Task<WorkflowClosedWorldCatalogDto> GetCatalogsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WorkflowClosedWorldCatalogDto(
            new[] { "Stage", "Decision", "Fork", "Join", "Start", "End", "SubWorkflow" },
            new[] { "CreateStageTasks", "ClosePreviousStageTasks", "SendNotification" },
            new[] { "Manual" },
            new[] { "Always" },
            new[] { "Manual", "Auto" },
            new[] { "Active", "Closed" },
            new[] { "Approved", "Rejected" },
            Array.Empty<string>(),
            Array.Empty<string>()));
}

// ─── Tree / detail models ───────────────────────────────────────────────────

public abstract class WorkflowViewerNode : ObservableObject
{
    protected WorkflowViewerNode(string header, string detailTitle)
    {
        Header = header;
        DetailTitle = detailTitle;
    }

    public string Header { get; }
    public string DetailTitle { get; }
    public ObservableCollection<WorkflowViewerNode> Children { get; } = new();
}

public sealed class WorkflowDefViewerNode : WorkflowViewerNode
{
    public WorkflowDefViewerNode(
        int id, string code, string name, string? description, bool isActive, bool isSystem,
        int stageCount, int transitionCount, int taskCount)
        : base(
            $"{(isActive ? "📂" : "⏸️")} {name}  [{stageCount} שלבים, {transitionCount} מעברים]",
            $"תהליך: {name}")
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
        IsActive = isActive;
        IsSystem = isSystem;
        StageCount = stageCount;
        TransitionCount = transitionCount;
        TaskCount = taskCount;
    }

    public int Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool IsActive { get; }
    public bool IsSystem { get; }
    public int StageCount { get; }
    public int TransitionCount { get; }
    public int TaskCount { get; }
}

public sealed class WorkflowStageViewerNode : WorkflowViewerNode
{
    public WorkflowStageViewerNode(
        int id, string code, string name, string? description, int sortOrder,
        bool isInitial, bool isFinal, string nodeType, bool nodeTypeKnown, bool isSystem,
        string? assignedGroupName, string? assignedGroupCode,
        string? subWorkflowName, string? subWorkflowCode)
        : base(
            $"{(isInitial ? "🟢" : isFinal ? "🔴" : "🔵")} {sortOrder}. {name}",
            $"שלב: {name}")
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
        SortOrder = sortOrder;
        IsInitial = isInitial;
        IsFinal = isFinal;
        NodeType = nodeType;
        NodeTypeKnown = nodeTypeKnown;
        IsSystem = isSystem;
        AssignedGroupName = assignedGroupName;
        AssignedGroupCode = assignedGroupCode;
        SubWorkflowName = subWorkflowName;
        SubWorkflowCode = subWorkflowCode;
    }

    public int Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string? Description { get; }
    public int SortOrder { get; }
    public bool IsInitial { get; }
    public bool IsFinal { get; }
    public string NodeType { get; }
    public bool NodeTypeKnown { get; }
    public bool IsSystem { get; }
    public string? AssignedGroupName { get; }
    public string? AssignedGroupCode { get; }
    public string? SubWorkflowName { get; }
    public string? SubWorkflowCode { get; }
}

public sealed class WorkflowTransGroupViewerNode : WorkflowViewerNode
{
    public WorkflowTransGroupViewerNode(bool isForward, int count)
        : base(
            isForward ? $"➡️ קדימה ({count})" : $"↩️ חזרה ({count})",
            isForward ? "מעברים קדימה" : "מעברים אחורה")
    {
        IsForward = isForward;
    }

    public bool IsForward { get; }
}

public sealed class WorkflowTaskGroupViewerNode : WorkflowViewerNode
{
    public WorkflowTaskGroupViewerNode(int count)
        : base($"📋 משימות ({count})", "משימות שלב")
    {
    }
}

public sealed class WorkflowTransitionViewerNode : WorkflowViewerNode
{
    public WorkflowTransitionViewerNode(
        int id, string? name, string fromName, string toName,
        string triggerType, bool triggerTypeKnown,
        string conditionType, bool conditionTypeKnown,
        string evaluationMode, bool evaluationModeKnown,
        int priority, string? conditionJson, string? conditionTaskResultCode, bool conditionTaskResultOk,
        IReadOnlyList<WorkflowActionViewerItem> actions)
        : base($"→ {toName}", $"מעבר: {fromName} → {toName}")
    {
        Id = id;
        Name = name;
        FromName = fromName;
        ToName = toName;
        TriggerType = triggerType;
        TriggerTypeKnown = triggerTypeKnown;
        ConditionType = conditionType;
        ConditionTypeKnown = conditionTypeKnown;
        EvaluationMode = evaluationMode;
        EvaluationModeKnown = evaluationModeKnown;
        Priority = priority;
        ConditionJson = conditionJson;
        ConditionTaskResultCode = conditionTaskResultCode;
        ConditionTaskResultOk = conditionTaskResultOk;
        Actions = actions;
    }

    public int Id { get; }
    public string? Name { get; }
    public string FromName { get; }
    public string ToName { get; }
    public string TriggerType { get; }
    public bool TriggerTypeKnown { get; }
    public string ConditionType { get; }
    public bool ConditionTypeKnown { get; }
    public string EvaluationMode { get; }
    public bool EvaluationModeKnown { get; }
    public int Priority { get; }
    public string? ConditionJson { get; }
    public string? ConditionTaskResultCode { get; }
    public bool ConditionTaskResultOk { get; }
    public IReadOnlyList<WorkflowActionViewerItem> Actions { get; }
}

public sealed record WorkflowActionViewerItem(
    string ActionType,
    bool ActionTypeKnown,
    string? ActionCode,
    string? ConfigJson,
    string? ConfigProjectStatusCode,
    bool ConfigProjectStatusOk,
    string? ConfigTaskResultCode,
    bool ConfigTaskResultOk,
    int SortOrder);

public sealed class WorkflowStageTaskViewerNode : WorkflowViewerNode
{
    public WorkflowStageTaskViewerNode(
        int id, string taskTypeName, string taskTypeCode, string? assigneeDisplay,
        bool isRequired, int sortOrder, string? notes,
        bool hasInteraction, string? openMode, string? componentKey,
        IReadOnlyList<string> allowedTaskResultCodes)
        : base(
            $"{(isRequired ? "📌" : "📋")} {taskTypeName}" + (string.IsNullOrEmpty(assigneeDisplay) ? "" : $" → {assigneeDisplay}"),
            $"משימה: {taskTypeName}")
    {
        Id = id;
        TaskTypeName = taskTypeName;
        TaskTypeCode = taskTypeCode;
        AssigneeDisplay = assigneeDisplay;
        IsRequired = isRequired;
        SortOrder = sortOrder;
        Notes = notes;
        HasInteraction = hasInteraction;
        OpenMode = openMode;
        ComponentKey = componentKey;
        AllowedTaskResultCodes = allowedTaskResultCodes;
    }

    public int Id { get; }
    public string TaskTypeName { get; }
    public string TaskTypeCode { get; }
    public string? AssigneeDisplay { get; }
    public bool IsRequired { get; }
    public int SortOrder { get; }
    public string? Notes { get; }
    public bool HasInteraction { get; }
    public string? OpenMode { get; }
    public string? ComponentKey { get; }
    public IReadOnlyList<string> AllowedTaskResultCodes { get; }
}
