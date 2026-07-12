using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// Read-only visual canvas for workflow definitions (New System V1).
/// </summary>
public sealed class WorkflowVisualCanvasViewModel : ObservableObject
{
    private const double NodeWidth = 140;
    private const double NodeHeight = 56;
    private const double HorizontalGap = 220;
    private const double RowY = 120;

    private readonly IWorkflowClosedViewerQueryService _query;
    private IReadOnlyList<WorkflowDefinitionGraphDto> _graphs = Array.Empty<WorkflowDefinitionGraphDto>();
    private WorkflowDefinitionGraphDto? _selectedDefinition;
    private string _statusMessage = "טוען…";
    private WorkflowDefinitionPickerItem? _selectedPickerItem;

    public WorkflowVisualCanvasViewModel()
        : this(new DesignWorkflowClosedViewerQueryService())
    {
    }

    public WorkflowVisualCanvasViewModel(IWorkflowClosedViewerQueryService query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
    }

    public string Title => "תהליכים — קנבס (צפייה)";

    public string BannerText =>
        "צפייה ויזואלית בלבד. התחלה/סיום מסומנים לפי IsInitial/IsFinal. מקביליות = מופעים נפרדים (מכסה בהגדרות).";

    public ICommand RefreshCommand { get; }

    public ObservableCollection<WorkflowDefinitionPickerItem> Definitions { get; } = new();

    public ObservableCollection<WorkflowCanvasNodeVm> Nodes { get; } = new();

    public ObservableCollection<WorkflowCanvasEdgeVm> Edges { get; } = new();

    public double CanvasWidth { get; private set; } = 1200;

    public double CanvasHeight { get; private set; } = 800;

    public WorkflowDefinitionPickerItem? SelectedPickerItem
    {
        get => _selectedPickerItem;
        set
        {
            if (SetField(ref _selectedPickerItem, value))
            {
                _selectedDefinition = value?.Graph;
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
        StatusMessage = "טוען תהליכים…";
        try
        {
            _graphs = await _query.GetDefinitionGraphsAsync(cancellationToken).ConfigureAwait(true);
            Definitions.Clear();
            foreach (var g in _graphs)
            {
                Definitions.Add(new WorkflowDefinitionPickerItem(g));
            }

            SelectedPickerItem = Definitions.FirstOrDefault();
            StatusMessage = $"נטענו {_graphs.Count} תהליכים · קנבס לקריאה בלבד";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
        }
    }

    private void RebuildCanvas()
    {
        Nodes.Clear();
        Edges.Clear();
        if (_selectedDefinition is null)
        {
            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
            return;
        }

        var stages = _selectedDefinition.Stages.OrderBy(s => s.SortOrder).ToList();
        var positions = new Dictionary<int, Point>();
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
                // Horizontal chain by SortOrder (not a 4-column grid).
                x = 40 + i * HorizontalGap;
                y = RowY;
            }

            positions[s.Id] = new Point(x, y);
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

        foreach (var t in _selectedDefinition.Transitions)
        {
            if (!positions.TryGetValue(t.FromStageId, out var from)
                || !positions.TryGetValue(t.ToStageId, out var to))
            {
                continue;
            }

            Edges.Add(WorkflowCanvasEdgeVm.CreateBetweenNodes(
                from, to, NodeWidth, NodeHeight, t.TriggerType));
        }

        CanvasWidth = Math.Max(800, Nodes.Count == 0 ? 800 : Nodes.Max(n => n.X) + NodeWidth + 80);
        CanvasHeight = Math.Max(420, Nodes.Count == 0 ? 420 : Nodes.Max(n => n.Y) + NodeHeight + 120);
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    /// <summary>
    /// Prefer SubWorkflow NodeType; otherwise paint Initial/Final as Start/End for legend clarity.
    /// </summary>
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

public sealed class WorkflowCanvasNodeVm
{
    public WorkflowCanvasNodeVm(
        int id, string name, string code, string visualKind, double x, double y, Brush fill, WorkflowCanvasNodeShape shape)
    {
        Id = id;
        Name = name;
        Code = code;
        VisualKind = visualKind;
        X = x;
        Y = y;
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
    public double X { get; }
    public double Y { get; }
    public Brush Fill { get; }
    public WorkflowCanvasNodeShape Shape { get; }
    public bool IsEllipse { get; }
    public bool IsDiamond { get; }
    public bool IsRoundedRect { get; }
    public string Caption => $"{Name}\n({VisualKind})";
}

public sealed class WorkflowCanvasEdgeVm
{
    private WorkflowCanvasEdgeVm(
        double x1, double y1, double x2, double y2, string label, PointCollection arrowPoints)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Label = label;
        LabelX = (x1 + x2) / 2 - 20;
        LabelY = (y1 + y2) / 2 - 14;
        ArrowPoints = arrowPoints;
    }

    public static WorkflowCanvasEdgeVm CreateBetweenNodes(
        Point fromTopLeft, Point toTopLeft, double nodeWidth, double nodeHeight, string label)
    {
        // Attach to node edges so the line is not buried under the rectangles.
        var start = new Point(fromTopLeft.X + nodeWidth, fromTopLeft.Y + nodeHeight / 2);
        var end = new Point(toTopLeft.X, toTopLeft.Y + nodeHeight / 2);

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
        {
            len = 1;
        }

        var ux = dx / len;
        var uy = dy / len;

        // Pull tip slightly off the target edge.
        var tip = new Point(end.X - ux * 2, end.Y - uy * 2);
        var baseCenter = new Point(tip.X - ux * 12, tip.Y - uy * 12);
        var px = -uy;
        var py = ux;
        var arrow = new PointCollection
        {
            tip,
            new Point(baseCenter.X + px * 6, baseCenter.Y + py * 6),
            new Point(baseCenter.X - px * 6, baseCenter.Y - py * 6),
        };

        var lineEnd = baseCenter;
        return new WorkflowCanvasEdgeVm(start.X, start.Y, lineEnd.X, lineEnd.Y, label, arrow);
    }

    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double LabelX { get; }
    public double LabelY { get; }
    public string Label { get; }
    public PointCollection ArrowPoints { get; }
}
