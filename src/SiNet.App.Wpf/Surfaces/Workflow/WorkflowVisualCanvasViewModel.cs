using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// Visual canvas for workflow definitions — layout drag + inspect (New System).
/// </summary>
public sealed class WorkflowVisualCanvasViewModel : ObservableObject
{
    private const double NodeWidth = 140;
    private const double NodeHeight = 56;
    private const double HorizontalGap = 220;
    private const double RowY = 120;

    private static readonly Brush EdgeStroke = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
    private static readonly Brush EdgeSelectedStroke = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));

    private readonly IWorkflowClosedViewerQueryService _query;
    private readonly IWorkflowCanvasLayoutService _layout;
    private IReadOnlyList<WorkflowDefinitionGraphDto> _graphs = Array.Empty<WorkflowDefinitionGraphDto>();
    private WorkflowDefinitionGraphDto? _selectedDefinition;
    private string _statusMessage = "טוען…";
    private WorkflowDefinitionPickerItem? _selectedPickerItem;
    private readonly AsyncRelayCommand _saveLayoutCommand;
    private bool _isDirty;
    private bool _isSaving;
    private WorkflowCanvasInspectKind _inspectKind = WorkflowCanvasInspectKind.None;
    private WorkflowCanvasStageInspectVm? _selectedStage;
    private WorkflowCanvasTransitionInspectVm? _selectedTransition;
    private int? _selectedStageId;
    private int? _selectedTransitionId;

    public WorkflowVisualCanvasViewModel()
        : this(new DesignWorkflowClosedViewerQueryService(), new NullWorkflowCanvasLayoutService())
    {
    }

    public WorkflowVisualCanvasViewModel(
        IWorkflowClosedViewerQueryService query,
        IWorkflowCanvasLayoutService layout)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
        _saveLayoutCommand = new AsyncRelayCommand(SaveLayoutAsync, () => IsDirty && !_isSaving && _selectedDefinition is not null);
        SaveLayoutCommand = _saveLayoutCommand;
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection());
    }

    public string Title => "תהליכים — קנבס (צפייה + סידור)";

    public string BannerText =>
        "לחץ על שלב או חץ לפירוט (משימות נדרשות / תנאי מעבר). גרור שלבים לסידור · שמור פריסה → CanvasX/Y. " +
        "לולאה עצמית = נשארים באותו שלב (לא חץ דו-כיווני).";

    public ICommand RefreshCommand { get; }

    public ICommand SaveLayoutCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public ObservableCollection<WorkflowDefinitionPickerItem> Definitions { get; } = new();

    public ObservableCollection<WorkflowCanvasNodeVm> Nodes { get; } = new();

    public ObservableCollection<WorkflowCanvasEdgeVm> Edges { get; } = new();

    public double CanvasWidth { get; private set; } = 1200;

    public double CanvasHeight { get; private set; } = 800;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
            {
                _saveLayoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => _inspectKind != WorkflowCanvasInspectKind.None;

    public bool IsStageSelected => _inspectKind == WorkflowCanvasInspectKind.Stage;

    public bool IsTransitionSelected => _inspectKind == WorkflowCanvasInspectKind.Transition;

    public bool ShowEmptyInspectHint => _inspectKind == WorkflowCanvasInspectKind.None;

    public WorkflowCanvasStageInspectVm? SelectedStage
    {
        get => _selectedStage;
        private set => SetField(ref _selectedStage, value);
    }

    public WorkflowCanvasTransitionInspectVm? SelectedTransition
    {
        get => _selectedTransition;
        private set => SetField(ref _selectedTransition, value);
    }

    public WorkflowDefinitionPickerItem? SelectedPickerItem
    {
        get => _selectedPickerItem;
        set
        {
            if (IsDirty && !ReferenceEquals(_selectedPickerItem, value))
            {
                IsDirty = false;
            }

            if (SetField(ref _selectedPickerItem, value))
            {
                _selectedDefinition = value?.Graph;
                ClearSelection();
                RebuildCanvas();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsDirty)
        {
            StatusMessage = "יש שינויי פריסה שלא נשמרו — נטען מחדש מה-DB (השינויים יאבדו).";
        }

        StatusMessage = "טוען תהליכים…";
        try
        {
            var previousCode = _selectedDefinition?.Code;
            _graphs = await _query.GetDefinitionGraphsAsync(cancellationToken).ConfigureAwait(true);
            Definitions.Clear();
            foreach (var g in _graphs)
            {
                Definitions.Add(new WorkflowDefinitionPickerItem(g));
            }

            SelectedPickerItem = Definitions.FirstOrDefault(d => d.Graph.Code == previousCode)
                ?? Definitions.FirstOrDefault();
            IsDirty = false;
            StatusMessage = $"נטענו {_graphs.Count} תהליכים · לחץ לפירוט · גרור · שמור פריסה";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
        }
    }

    public void MoveNode(WorkflowCanvasNodeVm node, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.X = Math.Max(0, x);
        node.Y = Math.Max(0, y);
        IsDirty = true;
        RebuildEdgesOnly();
        UpdateCanvasExtent();
        StatusMessage = "פריסה שונתה — לחץ «שמור פריסה» כדי לשמור ל-DB.";
        _saveLayoutCommand.RaiseCanExecuteChanged();
    }

    public void SelectStage(int stageId)
    {
        if (_selectedDefinition is null)
        {
            return;
        }

        var stage = _selectedDefinition.Stages.FirstOrDefault(s => s.Id == stageId);
        if (stage is null)
        {
            return;
        }

        _selectedStageId = stageId;
        _selectedTransitionId = null;
        _inspectKind = WorkflowCanvasInspectKind.Stage;

        var outgoing = _selectedDefinition.Transitions
            .Where(t => t.FromStageId == stageId)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.Id)
            .Select(t => WorkflowCanvasOutgoingTransitionVm.From(t))
            .ToList();

        SelectedStage = WorkflowCanvasStageInspectVm.From(stage, outgoing);
        SelectedTransition = null;
        ApplySelectionHighlight();
        NotifyInspectChanged();
        StatusMessage = $"נבחר שלב: {stage.Name} ({stage.Code})";
    }

    public void SelectTransition(int transitionId)
    {
        if (_selectedDefinition is null)
        {
            return;
        }

        var transition = _selectedDefinition.Transitions.FirstOrDefault(t => t.Id == transitionId);
        if (transition is null)
        {
            return;
        }

        _selectedTransitionId = transitionId;
        _selectedStageId = null;
        _inspectKind = WorkflowCanvasInspectKind.Transition;

        var fromStage = _selectedDefinition.Stages.FirstOrDefault(s => s.Id == transition.FromStageId);
        SelectedTransition = WorkflowCanvasTransitionInspectVm.From(transition, fromStage);
        SelectedStage = null;
        ApplySelectionHighlight();
        NotifyInspectChanged();
        StatusMessage = transition.FromStageId == transition.ToStageId
            ? $"לולאה עצמית ב־{transition.FromStageName}: נשארים בשלב"
            : $"נבחר מעבר: {transition.FromStageName} → {transition.ToStageName}";
    }

    public void ClearSelection()
    {
        _selectedStageId = null;
        _selectedTransitionId = null;
        _inspectKind = WorkflowCanvasInspectKind.None;
        SelectedStage = null;
        SelectedTransition = null;
        ApplySelectionHighlight();
        NotifyInspectChanged();
    }

    private void NotifyInspectChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsStageSelected));
        OnPropertyChanged(nameof(IsTransitionSelected));
        OnPropertyChanged(nameof(ShowEmptyInspectHint));
    }

    private void ApplySelectionHighlight()
    {
        foreach (var node in Nodes)
        {
            node.IsSelected = _selectedStageId == node.Id;
        }

        foreach (var edge in Edges)
        {
            edge.IsSelected = _selectedTransitionId == edge.TransitionId;
        }
    }

    private async Task SaveLayoutAsync()
    {
        if (_selectedDefinition is null || Nodes.Count == 0)
        {
            return;
        }

        _isSaving = true;
        _saveLayoutCommand.RaiseCanExecuteChanged();
        StatusMessage = "שומר פריסה…";
        try
        {
            var positions = Nodes
                .Select(n => new WorkflowStageCanvasPositionDto(n.Id, n.X, n.Y))
                .ToList();

            await _layout.SaveStageCanvasPositionsAsync(
                _selectedDefinition.Id, positions, CancellationToken.None).ConfigureAwait(true);

            IsDirty = false;
            StatusMessage = "פריסה נשמרה (CanvasX/Y). עדכון ל-seed — בשלב הבא כשהסידור יציב.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שמירת פריסה נכשלה: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
            _saveLayoutCommand.RaiseCanExecuteChanged();
        }
    }

    private void RebuildCanvas()
    {
        Nodes.Clear();
        Edges.Clear();
        if (_selectedDefinition is null)
        {
            UpdateCanvasExtent();
            return;
        }

        var stages = _selectedDefinition.Stages.OrderBy(s => s.SortOrder).ToList();
        var useStored = stages.Any(s => Math.Abs(s.CanvasX) > 0.1 || Math.Abs(s.CanvasY) > 0.1);

        for (var i = 0; i < stages.Count; i++)
        {
            var s = stages[i];
            double x;
            double y;
            if (useStored)
            {
                x = s.CanvasX;
                y = s.CanvasY;
            }
            else
            {
                x = 40 + i * HorizontalGap;
                y = RowY;
            }

            var visualKind = ResolveVisualKind(s);
            Nodes.Add(new WorkflowCanvasNodeVm(
                s.Id,
                s.Name,
                s.Code,
                visualKind,
                x,
                y,
                ResolveBrush(visualKind),
                ResolveShape(visualKind)));
        }

        RebuildEdgesOnly();
        UpdateCanvasExtent();
        ApplySelectionHighlight();
    }

    private void RebuildEdgesOnly()
    {
        Edges.Clear();
        if (_selectedDefinition is null)
        {
            return;
        }

        var positions = Nodes.ToDictionary(n => n.Id, n => new Point(n.X, n.Y));
        var transitions = _selectedDefinition.Transitions.ToList();

        var reverseKeys = new HashSet<(int, int)>();
        foreach (var t in transitions)
        {
            if (transitions.Any(o => o.FromStageId == t.ToStageId && o.ToStageId == t.FromStageId
                                     && t.FromStageId != t.ToStageId))
            {
                reverseKeys.Add((Math.Min(t.FromStageId, t.ToStageId), Math.Max(t.FromStageId, t.ToStageId)));
            }
        }

        foreach (var t in transitions)
        {
            if (!positions.TryGetValue(t.FromStageId, out var from)
                || !positions.TryGetValue(t.ToStageId, out var to))
            {
                continue;
            }

            double lateral = 0;
            if (t.FromStageId != t.ToStageId
                && reverseKeys.Contains((Math.Min(t.FromStageId, t.ToStageId), Math.Max(t.FromStageId, t.ToStageId))))
            {
                lateral = t.FromStageId < t.ToStageId ? -14 : 14;
            }

            var label = FormatEdgeLabel(t);
            Edges.Add(WorkflowCanvasEdgeVm.Create(
                t.Id,
                t.FromStageId,
                t.ToStageId,
                from,
                to,
                NodeWidth,
                NodeHeight,
                label,
                lateral,
                EdgeStroke,
                EdgeSelectedStroke));
        }

        ApplySelectionHighlight();
    }

    private static string FormatEdgeLabel(WorkflowTransitionGraphDto t)
    {
        if (!string.IsNullOrWhiteSpace(t.ConditionTaskResultCode))
        {
            return $"{t.TriggerType} · {t.ConditionTaskResultCode}";
        }

        return t.TriggerType;
    }

    private void UpdateCanvasExtent()
    {
        CanvasWidth = Math.Max(800, Nodes.Count == 0 ? 800 : Nodes.Max(n => n.X) + NodeWidth + 80);
        CanvasHeight = Math.Max(420, Nodes.Count == 0 ? 420 : Nodes.Max(n => n.Y) + NodeHeight + 160);
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private static string ResolveVisualKind(WorkflowStageGraphDto stage)
    {
        if (string.Equals(stage.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
        {
            return "SubWorkflow";
        }

        if (stage.IsInitial)
        {
            return "Start";
        }

        if (stage.IsFinal)
        {
            return "End";
        }

        return string.IsNullOrWhiteSpace(stage.NodeType) ? "Stage" : stage.NodeType;
    }

    private static Brush ResolveBrush(string visualKind) => visualKind switch
    {
        "Start" => new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)),
        "End" => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
        "Decision" => new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00)),
        "SubWorkflow" => new SolidColorBrush(Color.FromRgb(0x6D, 0x4C, 0x41)),
        "Fork" => new SolidColorBrush(Color.FromRgb(0x8E, 0x24, 0xAA)),
        "Join" => new SolidColorBrush(Color.FromRgb(0x00, 0x89, 0x7B)),
        _ => new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
    };

    private static WorkflowCanvasNodeShape ResolveShape(string visualKind) => visualKind switch
    {
        "Start" or "End" => WorkflowCanvasNodeShape.Ellipse,
        "Decision" => WorkflowCanvasNodeShape.Diamond,
        _ => WorkflowCanvasNodeShape.RoundedRect,
    };
}

public enum WorkflowCanvasInspectKind
{
    None,
    Stage,
    Transition,
}

public sealed record WorkflowDefinitionPickerItem(WorkflowDefinitionGraphDto Graph)
{
    public string Display => $"{Graph.Name} [{Graph.Code}]";
    public override string ToString() => Display;
}

public enum WorkflowCanvasNodeShape
{
    RoundedRect,
    Ellipse,
    Diamond,
}

public sealed class WorkflowCanvasNodeVm : ObservableObject
{
    private double _x;
    private double _y;
    private bool _isSelected;

    public WorkflowCanvasNodeVm(
        int id, string name, string code, string visualKind, double x, double y, Brush fill, WorkflowCanvasNodeShape shape)
    {
        Id = id;
        Name = name;
        Code = code;
        VisualKind = visualKind;
        _x = x;
        _y = y;
        Fill = fill;
        Shape = shape;
        IsEllipse = shape == WorkflowCanvasNodeShape.Ellipse;
        IsDiamond = shape == WorkflowCanvasNodeShape.Diamond;
        IsRoundedRect = shape == WorkflowCanvasNodeShape.RoundedRect;
    }

    public int Id { get; }
    public string Name { get; }
    public string Code { get; }
    public string VisualKind { get; }

    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(StrokeThickness));
                OnPropertyChanged(nameof(BorderThicknessValue));
            }
        }
    }

    public Brush Fill { get; }
    public WorkflowCanvasNodeShape Shape { get; }
    public bool IsEllipse { get; }
    public bool IsDiamond { get; }
    public bool IsRoundedRect { get; }
    public string Caption => $"{Name}\n({VisualKind})";
    public Brush StrokeBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6F, 0x00))
        : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    public double StrokeThickness => IsSelected ? 3.5 : 1;
    public Thickness BorderThicknessValue => new(IsSelected ? 3.5 : 1);
}

public sealed class WorkflowCanvasEdgeVm : ObservableObject
{
    private bool _isSelected;
    private readonly Brush _normalStroke;
    private readonly Brush _selectedStroke;

    private WorkflowCanvasEdgeVm(
        int transitionId,
        int fromStageId,
        int toStageId,
        double x1,
        double y1,
        double x2,
        double y2,
        string label,
        PointCollection arrowPoints,
        bool isSelfLoop,
        PointCollection? loopPoints,
        Brush normalStroke,
        Brush selectedStroke)
    {
        TransitionId = transitionId;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Label = label;
        LabelX = (x1 + x2) / 2 - 28;
        LabelY = Math.Min(y1, y2) - 22;
        ArrowPoints = arrowPoints;
        IsSelfLoop = isSelfLoop;
        LoopPoints = loopPoints ?? new PointCollection();
        IsStraight = !isSelfLoop;
        _normalStroke = normalStroke;
        _selectedStroke = selectedStroke;
    }

    public static WorkflowCanvasEdgeVm Create(
        int transitionId,
        int fromStageId,
        int toStageId,
        Point fromTopLeft,
        Point toTopLeft,
        double nodeWidth,
        double nodeHeight,
        string label,
        double lateralOffset,
        Brush normalStroke,
        Brush selectedStroke)
    {
        if (fromStageId == toStageId
            || (Math.Abs(fromTopLeft.X - toTopLeft.X) < 0.5 && Math.Abs(fromTopLeft.Y - toTopLeft.Y) < 0.5))
        {
            return CreateSelfLoop(
                transitionId, fromStageId, toStageId, fromTopLeft, nodeWidth, nodeHeight, label,
                normalStroke, selectedStroke);
        }

        var start = new Point(fromTopLeft.X + nodeWidth, fromTopLeft.Y + nodeHeight / 2);
        var end = new Point(toTopLeft.X, toTopLeft.Y + nodeHeight / 2);

        var dxCenter = (toTopLeft.X + nodeWidth / 2) - (fromTopLeft.X + nodeWidth / 2);
        var dyCenter = (toTopLeft.Y + nodeHeight / 2) - (fromTopLeft.Y + nodeHeight / 2);
        if (Math.Abs(dyCenter) > Math.Abs(dxCenter) * 0.6)
        {
            if (dyCenter > 0)
            {
                start = new Point(fromTopLeft.X + nodeWidth / 2, fromTopLeft.Y + nodeHeight);
                end = new Point(toTopLeft.X + nodeWidth / 2, toTopLeft.Y);
            }
            else
            {
                start = new Point(fromTopLeft.X + nodeWidth / 2, fromTopLeft.Y);
                end = new Point(toTopLeft.X + nodeWidth / 2, toTopLeft.Y + nodeHeight);
            }
        }
        else if (dxCenter < 0)
        {
            start = new Point(fromTopLeft.X, fromTopLeft.Y + nodeHeight / 2);
            end = new Point(toTopLeft.X + nodeWidth, toTopLeft.Y + nodeHeight / 2);
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
        {
            len = 1;
        }

        var ux = dx / len;
        var uy = dy / len;
        var px = -uy;
        var py = ux;

        start = new Point(start.X + px * lateralOffset, start.Y + py * lateralOffset);
        end = new Point(end.X + px * lateralOffset, end.Y + py * lateralOffset);

        var tip = new Point(end.X - ux * 2, end.Y - uy * 2);
        var baseCenter = new Point(tip.X - ux * 12, tip.Y - uy * 12);
        var arrow = new PointCollection
        {
            tip,
            new Point(baseCenter.X + px * 6, baseCenter.Y + py * 6),
            new Point(baseCenter.X - px * 6, baseCenter.Y - py * 6),
        };

        return new WorkflowCanvasEdgeVm(
            transitionId, fromStageId, toStageId,
            start.X, start.Y, baseCenter.X, baseCenter.Y, label, arrow, false, null,
            normalStroke, selectedStroke)
        {
            LabelX = (start.X + baseCenter.X) / 2 - 28 + px * 10,
            LabelY = (start.Y + baseCenter.Y) / 2 - 18 + py * 10,
        };
    }

    private static WorkflowCanvasEdgeVm CreateSelfLoop(
        int transitionId,
        int fromStageId,
        int toStageId,
        Point topLeft,
        double nodeWidth,
        double nodeHeight,
        string label,
        Brush normalStroke,
        Brush selectedStroke)
    {
        var cx = topLeft.X + nodeWidth / 2;
        var top = topLeft.Y;
        var loop = new PointCollection
        {
            new Point(cx - 18, top),
            new Point(cx - 40, top - 48),
            new Point(cx + 40, top - 48),
            new Point(cx + 18, top),
        };

        var tip = new Point(cx + 18, top);
        var arrow = new PointCollection
        {
            tip,
            new Point(tip.X + 8, tip.Y - 12),
            new Point(tip.X - 6, tip.Y - 10),
        };

        return new WorkflowCanvasEdgeVm(
            transitionId, fromStageId, toStageId,
            cx - 18, top, cx + 18, top, label, arrow, true, loop,
            normalStroke, selectedStroke)
        {
            LabelX = cx - 40,
            LabelY = top - 66,
        };
    }

    public int TransitionId { get; }
    public int FromStageId { get; }
    public int ToStageId { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double LabelX { get; private init; }
    public double LabelY { get; private init; }
    public string Label { get; }
    public PointCollection ArrowPoints { get; }
    public bool IsSelfLoop { get; }
    public bool IsStraight { get; }
    public PointCollection LoopPoints { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(Stroke));
                OnPropertyChanged(nameof(StrokeThickness));
            }
        }
    }

    public Brush Stroke => IsSelected ? _selectedStroke : _normalStroke;
    public double StrokeThickness => IsSelected ? 3.5 : 2.5;
}

public sealed class WorkflowCanvasStageInspectVm
{
    private WorkflowCanvasStageInspectVm(
        string name,
        string code,
        string nodeType,
        bool isInitial,
        bool isFinal,
        string? assignedGroup,
        string? subWorkflow,
        string? description,
        IReadOnlyList<WorkflowCanvasTaskInspectVm> tasks,
        IReadOnlyList<WorkflowCanvasOutgoingTransitionVm> outgoing)
    {
        Name = name;
        Code = code;
        NodeType = nodeType;
        IsInitial = isInitial;
        IsFinal = isFinal;
        AssignedGroup = assignedGroup;
        SubWorkflow = subWorkflow;
        Description = description;
        Tasks = tasks;
        OutgoingTransitions = outgoing;
    }

    public static WorkflowCanvasStageInspectVm From(
        WorkflowStageGraphDto stage,
        IReadOnlyList<WorkflowCanvasOutgoingTransitionVm> outgoing) =>
        new(
            stage.Name,
            stage.Code,
            stage.NodeType,
            stage.IsInitial,
            stage.IsFinal,
            stage.AssignedGroupName is null
                ? null
                : $"{stage.AssignedGroupName} ({stage.AssignedGroupCode})",
            stage.SubWorkflowName is null
                ? null
                : $"{stage.SubWorkflowName} ({stage.SubWorkflowCode})",
            stage.Description,
            stage.StageTasks.Select(WorkflowCanvasTaskInspectVm.From).ToList(),
            outgoing);

    public string Name { get; }
    public string Code { get; }
    public string NodeType { get; }
    public bool IsInitial { get; }
    public bool IsFinal { get; }
    public string? AssignedGroup { get; }
    public string? SubWorkflow { get; }
    public string? Description { get; }
    public IReadOnlyList<WorkflowCanvasTaskInspectVm> Tasks { get; }
    public IReadOnlyList<WorkflowCanvasOutgoingTransitionVm> OutgoingTransitions { get; }
    public bool HasTasks => Tasks.Count > 0;
    public bool HasOutgoing => OutgoingTransitions.Count > 0;
}

public sealed class WorkflowCanvasTaskInspectVm
{
    private WorkflowCanvasTaskInspectVm(
        string taskTypeName,
        string taskTypeCode,
        bool isRequired,
        string? assignee,
        string allowedResults)
    {
        TaskTypeName = taskTypeName;
        TaskTypeCode = taskTypeCode;
        IsRequired = isRequired;
        Assignee = assignee;
        AllowedResults = allowedResults;
    }

    public static WorkflowCanvasTaskInspectVm From(WorkflowStageTaskGraphDto t) =>
        new(
            t.TaskTypeName,
            t.TaskTypeCode,
            t.IsRequired,
            t.AssigneeDisplay,
            t.AllowedTaskResultCodes.Count == 0
                ? "(אין תוצאות מוגדרות)"
                : string.Join(", ", t.AllowedTaskResultCodes));

    public string TaskTypeName { get; }
    public string TaskTypeCode { get; }
    public bool IsRequired { get; }
    public string RequiredLabel => IsRequired ? "חובה" : "אופציונלי";
    public string? Assignee { get; }
    public string AllowedResults { get; }
}

public sealed class WorkflowCanvasOutgoingTransitionVm
{
    private WorkflowCanvasOutgoingTransitionVm(
        string targetLabel,
        string trigger,
        string condition,
        bool isSelfLoop)
    {
        TargetLabel = targetLabel;
        Trigger = trigger;
        Condition = condition;
        IsSelfLoop = isSelfLoop;
        LoopTag = isSelfLoop ? "לולאה" : null;
    }

    public static WorkflowCanvasOutgoingTransitionVm From(WorkflowTransitionGraphDto t)
    {
        var isLoop = t.FromStageId == t.ToStageId;
        var target = isLoop ? t.FromStageName : t.ToStageName;
        var condition = string.IsNullOrWhiteSpace(t.ConditionTaskResultCode)
            ? t.ConditionType
            : $"{t.ConditionType} = {t.ConditionTaskResultCode}";
        return new WorkflowCanvasOutgoingTransitionVm(target, t.TriggerType, condition, isLoop);
    }

    public string TargetLabel { get; }
    public string Trigger { get; }
    public string Condition { get; }
    public bool IsSelfLoop { get; }
    public string? LoopTag { get; }
    public string Display => IsSelfLoop
        ? $"↻ {TargetLabel} · {Trigger} · {Condition}"
        : $"→ {TargetLabel} · {Trigger} · {Condition}";
}

public sealed class WorkflowCanvasTransitionInspectVm
{
    private WorkflowCanvasTransitionInspectVm(
        string title,
        string fromName,
        string toName,
        bool isSelfLoop,
        string triggerType,
        string triggerExplanation,
        string conditionType,
        string? taskResult,
        string evaluationMode,
        int priority,
        IReadOnlyList<WorkflowCanvasActionInspectVm> actions,
        IReadOnlyList<WorkflowCanvasTaskInspectVm> requiredTasks)
    {
        Title = title;
        FromName = fromName;
        ToName = toName;
        IsSelfLoop = isSelfLoop;
        TriggerType = triggerType;
        TriggerExplanation = triggerExplanation;
        ConditionType = conditionType;
        TaskResult = taskResult;
        EvaluationMode = evaluationMode;
        Priority = priority;
        Actions = actions;
        RequiredTasks = requiredTasks;
    }

    public static WorkflowCanvasTransitionInspectVm From(
        WorkflowTransitionGraphDto t,
        WorkflowStageGraphDto? fromStage)
    {
        var isLoop = t.FromStageId == t.ToStageId;
        var title = isLoop
            ? "לולאה עצמית — נשארים בשלב"
            : $"{t.FromStageName} → {t.ToStageName}";

        var tasks = fromStage?.StageTasks.Select(WorkflowCanvasTaskInspectVm.From).ToList()
            ?? new List<WorkflowCanvasTaskInspectVm>();

        return new WorkflowCanvasTransitionInspectVm(
            title,
            t.FromStageName,
            t.ToStageName,
            isLoop,
            t.TriggerType,
            ExplainTrigger(t.TriggerType),
            t.ConditionType,
            t.ConditionTaskResultCode,
            t.EvaluationMode,
            t.Priority,
            t.Actions.Select(WorkflowCanvasActionInspectVm.From).ToList(),
            tasks);
    }

    private static string ExplainTrigger(string trigger) => trigger switch
    {
        "Manual" => "מופעל רק כשמשתמש מקדם את התהליך ידנית (לא ב-auto-advance).",
        "AllRequiredTasksClosed" => "מופעל אחרי שכל המשימות הנדרשות בשלב המקור נסגרו.",
        "TaskStatusChanged" => "מופעל אחרי שינוי סטטוס/תוצאת משימה (לרוב עם TaskResultEquals).",
        "SubWorkflowCompleted" => "מופעל כשתת-התהליך הילד מסתיים.",
        "ActionCompleted" => "מופעל אחרי השלמת פעולת תהליך.",
        _ => "טריגר מעבר כפי שמוגדר בכלל.",
    };

    public string Title { get; }
    public string FromName { get; }
    public string ToName { get; }
    public bool IsSelfLoop { get; }
    public string TriggerType { get; }
    public string TriggerExplanation { get; }
    public string ConditionType { get; }
    public string? TaskResult { get; }
    public string EvaluationMode { get; }
    public int Priority { get; }
    public IReadOnlyList<WorkflowCanvasActionInspectVm> Actions { get; }
    public IReadOnlyList<WorkflowCanvasTaskInspectVm> RequiredTasks { get; }
    public bool HasActions => Actions.Count > 0;
    public bool HasRequiredTasks => RequiredTasks.Count > 0;
    public string SelfLoopExplanation =>
        "זה לא חץ דו-כיווני: המעבר חוזר לאותו שלב (למשל חומר חסר) עד שתוצאה אחרת מוציאה החוצה.";
}

public sealed class WorkflowCanvasActionInspectVm
{
    private WorkflowCanvasActionInspectVm(string actionType, string? actionCode, string? projectStatus, string? taskResult)
    {
        ActionType = actionType;
        ActionCode = actionCode;
        ProjectStatus = projectStatus;
        TaskResult = taskResult;
    }

    public static WorkflowCanvasActionInspectVm From(WorkflowTransitionActionGraphDto a) =>
        new(a.ActionType, a.ActionCode, a.ConfigProjectStatusCode, a.ConfigTaskResultCode);

    public string ActionType { get; }
    public string? ActionCode { get; }
    public string? ProjectStatus { get; }
    public string? TaskResult { get; }
    public string Display
    {
        get
        {
            var parts = new List<string> { ActionType };
            if (!string.IsNullOrWhiteSpace(ActionCode))
            {
                parts.Add(ActionCode);
            }

            if (!string.IsNullOrWhiteSpace(ProjectStatus))
            {
                parts.Add($"Status={ProjectStatus}");
            }

            if (!string.IsNullOrWhiteSpace(TaskResult))
            {
                parts.Add($"Result={TaskResult}");
            }

            return string.Join(" · ", parts);
        }
    }
}
