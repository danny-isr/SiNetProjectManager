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
    private readonly IWorkflowClosedViewerQueryService _query;
    private IReadOnlyList<WorkflowDefinitionGraphDto> _graphs = Array.Empty<WorkflowDefinitionGraphDto>();
    private WorkflowDefinitionGraphDto? _selectedDefinition;
    private string _statusMessage = "טוען…";

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
        "צפייה ויזואלית בלבד. מקביליות = מופעים נפרדים (מכסת תת-תהליך בהגדרות מערכת). עריכה מקטלוגים — בשלב הבא.";

    public ICommand RefreshCommand { get; }

    public ObservableCollection<WorkflowDefinitionPickerItem> Definitions { get; } = new();

    public ObservableCollection<WorkflowCanvasNodeVm> Nodes { get; } = new();

    public ObservableCollection<WorkflowCanvasEdgeVm> Edges { get; } = new();

    public double CanvasWidth { get; private set; } = 1200;

    public double CanvasHeight { get; private set; } = 800;

    private WorkflowDefinitionPickerItem? _selectedPickerItem;

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
                x = 40 + (i % 4) * 220;
                y = 40 + (i / 4) * 140;
            }

            positions[s.Id] = new Point(x, y);
            Nodes.Add(new WorkflowCanvasNodeVm(
                s.Id,
                s.Name,
                s.Code,
                s.NodeType,
                x,
                y,
                ResolveBrush(s.NodeType),
                ResolveShape(s.NodeType)));
        }

        foreach (var t in _selectedDefinition.Transitions)
        {
            if (!positions.TryGetValue(t.FromStageId, out var from)
                || !positions.TryGetValue(t.ToStageId, out var to))
            {
                continue;
            }

            Edges.Add(new WorkflowCanvasEdgeVm(
                from.X + 70,
                from.Y + 28,
                to.X + 70,
                to.Y + 28,
                t.TriggerType));
        }

        CanvasWidth = Math.Max(800, Nodes.Count == 0 ? 800 : Nodes.Max(n => n.X) + 200);
        CanvasHeight = Math.Max(500, Nodes.Count == 0 ? 500 : Nodes.Max(n => n.Y) + 160);
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private static Brush ResolveBrush(string nodeType) => nodeType switch
    {
        "Start" => new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)),
        "End" => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
        "Decision" => new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00)),
        "SubWorkflow" => new SolidColorBrush(Color.FromRgb(0x6D, 0x4C, 0x41)),
        "Fork" => new SolidColorBrush(Color.FromRgb(0x8E, 0x24, 0xAA)),
        "Join" => new SolidColorBrush(Color.FromRgb(0x00, 0x89, 0x7B)),
        _ => new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
    };

    private static WorkflowCanvasNodeShape ResolveShape(string nodeType) => nodeType switch
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
        int id, string name, string code, string nodeType, double x, double y, Brush fill, WorkflowCanvasNodeShape shape)
    {
        Id = id;
        Name = name;
        Code = code;
        NodeType = nodeType;
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
    public string NodeType { get; }
    public double X { get; }
    public double Y { get; }
    public Brush Fill { get; }
    public WorkflowCanvasNodeShape Shape { get; }
    public bool IsEllipse { get; }
    public bool IsDiamond { get; }
    public bool IsRoundedRect { get; }
    public string Caption => $"{Name}\n({NodeType})";
}

public sealed class WorkflowCanvasEdgeVm
{
    public WorkflowCanvasEdgeVm(double x1, double y1, double x2, double y2, string label)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Label = label;
        LabelX = (x1 + x2) / 2;
        LabelY = (y1 + y2) / 2 - 10;
    }

    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double LabelX { get; }
    public double LabelY { get; }
    public string Label { get; }
}
