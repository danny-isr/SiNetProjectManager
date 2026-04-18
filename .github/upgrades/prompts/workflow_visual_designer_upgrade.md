# שדרוג חלון Workflow לעורך ויזואלי (Workflow Visual Designer)

שדרוג ה-Workflow Builder הקיים מתצוגת עץ ליניארית (**רצף ישר**) לעורך ויזואלי מלא שתומך ב**הסתעפויות, תנאים, מיזוגים, לולאות חזרה**, וויזואליזציה גרפית — בדומה ל-Workflow Designer.

**עיקרון מנחה:** להישאר עם המנוע הקיים (`WorkflowEngine`, `WorkflowDefinition`, `WorkflowStageDefinition`, `WorkflowTransitionRule`, `WorkflowStageTask`) ולא לייבא Workflow Foundation. השדרוג הוא ב-**UI + מודל DB קטן** בלבד.

---

## שלב 1: הרחבת מודל הנתונים (DB Model)

הרחב את הישויות הקיימות כדי לתמוך בהסתעפויות ותנאים. **אל תשבור ישויות קיימות** — רק הוסף שדות חדשים.

### 1.1 הוספת שדות ל-`WorkflowStageDefinition`

הוסף שדות מיקום ויזואלי וסוג צומת:

```csharp
// הוסף לקובץ: SiNetSQL/Models/WorkflowStageDefinition.cs

/// <summary>Node type for visual designer rendering.</summary>
public string NodeType { get; set; } = "Stage";  
// Values: "Stage", "Decision", "Fork", "Join", "Start", "End"

/// <summary>X position on the visual canvas (pixels).</summary>
public double CanvasX { get; set; }

/// <summary>Y position on the visual canvas (pixels).</summary>
public double CanvasY { get; set; }

/// <summary>Optional color/theme for the node.</summary>
public string? Color { get; set; }
```

### 1.2 הוספת שדות ל-`WorkflowTransitionRule`

הוסף תמיכה בתנאים ותוויות על חיצי מעבר:

```csharp
// הוסף לקובץ: SiNetSQL/Models/WorkflowTransitionRule.cs

/// <summary>Condition expression (e.g. "AllTasksComplete", "Approved", "Rejected").</summary>
public string? Condition { get; set; }

/// <summary>Display label on the arrow (e.g. "כן", "לא", "מאושר").</summary>
public string? Label { get; set; }

/// <summary>Priority for evaluating transitions from the same source (lower = first).</summary>
public int Priority { get; set; }

/// <summary>Visual routing waypoints for the connector line (JSON array of {X,Y}).</summary>
public string? RoutePointsJson { get; set; }
```

### 1.3 Migration

לאחר הוספת השדות, צור migration:
```
Add-Migration AddWorkflowDesignerFields -Context SiNetSQLDbContext
```

**⚠️ אל תריץ migration אוטומטית.** ספק את הפקודה והמתן לאישור המפתח.

---

## שלב 2: מודל ViewModel לקנבס (Canvas ViewModel)

צור ViewModels חדשים שייצגו את הקנבס הויזואלי.
כל ה-ViewModels ממוקמים ב-`SiNetSQL/MVVM/` ומשתמשים בדפוס הקיים: `INotifyPropertyChanged` + `RelayCommand`.

> **קונבנציה קיימת בפרויקט:** ראה `WorkflowDashboardViewModel.cs` ו-`WorkflowInstanceViewModel.cs` כרפרנס לסגנון.

### 2.1 צור `DesignerNodeViewModel`

```
מיקום: SiNetSQL/MVVM/DesignerNodeViewModel.cs
```

מייצג צומת יחיד על הקנבס. **כל property חייב לירות `OnPropertyChanged`** כי ה-Canvas מאזין לשינויים בזמן גרירה.

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SiNetSQL.Models;

namespace SiNetSQL.MVVM;

/// <summary>
/// Represents a single node on the workflow visual designer canvas.
/// Maps to <see cref="WorkflowStageDefinition"/> in the DB.
/// </summary>
public class DesignerNodeViewModel : INotifyPropertyChanged
{
    // ═══ Identity ═══

    /// <summary>DB Id — 0 for unsaved nodes.</summary>
    private int _id;
    public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }

    private string _name = "שלב חדש";
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

    private string _code = string.Empty;
    public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }

    // ═══ Node Type ═══
    // Values: "Stage", "Decision", "Fork", "Join", "Start", "End"

    private string _nodeType = "Stage";
    public string NodeType
    {
        get => _nodeType;
        set
        {
            _nodeType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconText));
            OnPropertyChanged(nameof(NodeBrush));
            OnPropertyChanged(nameof(DefaultWidth));
            OnPropertyChanged(nameof(DefaultHeight));
            OnPropertyChanged(nameof(IsStage));
            OnPropertyChanged(nameof(IsDecision));
        }
    }

    // ═══ Canvas Position (CRITICAL for drag — must fire PropertyChanged) ═══

    private double _x;
    public double X { get => _x; set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterX)); } }

    private double _y;
    public double Y { get => _y; set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterY)); } }

    // ═══ Dimensions ═══

    public double DefaultWidth => NodeType switch
    {
        "Start" or "End" => 60,
        "Decision" => 100,
        "Fork" or "Join" => 120,
        _ => 140  // Stage
    };

    public double DefaultHeight => NodeType switch
    {
        "Start" or "End" => 60,
        "Decision" => 70,
        "Fork" or "Join" => 30,
        _ => 80  // Stage
    };

    /// <summary>Center point X (used by connectors to calculate start/end points).</summary>
    public double CenterX => X + DefaultWidth / 2;

    /// <summary>Center point Y.</summary>
    public double CenterY => Y + DefaultHeight / 2;

    // ═══ Selection ═══

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    // ═══ Stage-specific ═══

    private string? _description;
    public string? Description { get => _description; set { _description = value; OnPropertyChanged(); } }

    private bool _isInitial;
    public bool IsInitial { get => _isInitial; set { _isInitial = value; OnPropertyChanged(); } }

    private bool _isFinal;
    public bool IsFinal { get => _isFinal; set { _isFinal = value; OnPropertyChanged(); } }

    private string? _color;
    public string? Color { get => _color; set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(NodeBrush)); } }

    private ObservableCollection<WorkflowStageTask> _tasks = [];
    public ObservableCollection<WorkflowStageTask> Tasks { get => _tasks; set { _tasks = value; OnPropertyChanged(); } }

    // ═══ Decision-specific ═══

    private string? _conditionExpression;
    public string? ConditionExpression { get => _conditionExpression; set { _conditionExpression = value; OnPropertyChanged(); } }

    // ═══ Computed Display ═══

    public bool IsStage => NodeType == "Stage";
    public bool IsDecision => NodeType == "Decision";

    public string IconText => NodeType switch
    {
        "Start" => "🟢",
        "End" => "🔴",
        "Decision" => "🔷",
        "Fork" => "⑂",
        "Join" => "⊕",
        _ => "🔵"
    };

    /// <summary>Node fill brush. Uses custom Color if set, otherwise defaults by NodeType.</summary>
    public Brush NodeBrush
    {
        get
        {
            var hex = Color ?? NodeType switch
            {
                "Start" => "#4CAF50",
                "End" => "#F44336",
                "Decision" => "#FF9800",
                "Fork" => "#9C27B0",
                "Join" => "#009688",
                _ => "#2196F3"
            };
            try { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.SteelBlue; }
        }
    }

    // ═══ INotifyPropertyChanged ═══

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

### 2.2 צור `DesignerConnectorViewModel`

```
מיקום: SiNetSQL/MVVM/DesignerConnectorViewModel.cs
```

מייצג חיבור (חץ) בין שני צמתים. **חייב להאזין לשינויי מיקום של Source ו-Target** כדי לעדכן את הקו בזמן גרירה.

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace SiNetSQL.MVVM;

/// <summary>
/// Represents a connector (arrow) between two nodes on the designer canvas.
/// Maps to <see cref="Models.WorkflowTransitionRule"/> in the DB.
/// Listens to Source/Target position changes to update visual endpoints.
/// </summary>
public class DesignerConnectorViewModel : INotifyPropertyChanged, IDisposable
{
    private DesignerNodeViewModel _source = null!;
    private DesignerNodeViewModel _target = null!;

    /// <summary>DB Id — null for unsaved connectors.</summary>
    public int? RuleId { get; set; }

    public DesignerNodeViewModel Source
    {
        get => _source;
        set
        {
            if (_source is not null) _source.PropertyChanged -= OnEndpointChanged;
            _source = value;
            if (_source is not null) _source.PropertyChanged += OnEndpointChanged;
            OnPropertyChanged();
            RefreshEndpoints();
        }
    }

    public DesignerNodeViewModel Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.PropertyChanged -= OnEndpointChanged;
            _target = value;
            if (_target is not null) _target.PropertyChanged += OnEndpointChanged;
            OnPropertyChanged();
            RefreshEndpoints();
        }
    }

    // ═══ Transition metadata ═══

    private string? _label;
    /// <summary>Display label on the arrow (e.g. "כן", "לא", "מאושר").</summary>
    public string? Label { get => _label; set { _label = value; OnPropertyChanged(); } }

    private string? _condition;
    /// <summary>Condition expression (e.g. "AllTasksComplete", "Approved").</summary>
    public string? Condition { get => _condition; set { _condition = value; OnPropertyChanged(); } }

    private int _priority;
    public int Priority { get => _priority; set { _priority = value; OnPropertyChanged(); } }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    // ═══ Computed visual endpoints ═══

    private Point _startPoint;
    /// <summary>Start point — computed from Source center.</summary>
    public Point StartPoint { get => _startPoint; private set { _startPoint = value; OnPropertyChanged(); } }

    private Point _endPoint;
    /// <summary>End point — computed from Target center.</summary>
    public Point EndPoint { get => _endPoint; private set { _endPoint = value; OnPropertyChanged(); } }

    /// <summary>Midpoint for label placement.</summary>
    public Point MidPoint => new((StartPoint.X + EndPoint.X) / 2, (StartPoint.Y + EndPoint.Y) / 2);

    /// <summary>
    /// Recalculate when Source or Target moves.
    /// Only react to CenterX/CenterY to avoid noise.
    /// </summary>
    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesignerNodeViewModel.CenterX) or nameof(DesignerNodeViewModel.CenterY))
            RefreshEndpoints();
    }

    private void RefreshEndpoints()
    {
        if (_source is null || _target is null) return;
        StartPoint = new Point(_source.CenterX, _source.CenterY);
        EndPoint = new Point(_target.CenterX, _target.CenterY);
        OnPropertyChanged(nameof(MidPoint));
    }

    // ═══ Cleanup ═══

    public void Dispose()
    {
        if (_source is not null) _source.PropertyChanged -= OnEndpointChanged;
        if (_target is not null) _target.PropertyChanged -= OnEndpointChanged;
    }

    // ═══ INotifyPropertyChanged ═══

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

### 2.3 צור `WorkflowDesignerViewModel`

```
מיקום: SiNetSQL/MVVM/WorkflowDesignerViewModel.cs
```

ה-ViewModel הראשי של ה-Designer. עוקב אחרי הדפוס של `WorkflowDashboardViewModel`:
- Constructor מקבל dependencies דרך DI
- משתמש ב-`RelayCommand` הקיים
- משתמש ב-`IDbContextFactory<SiNetSQLDbContext>`

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.MVVM;

/// <summary>
/// Main ViewModel for the Workflow Visual Designer canvas.
/// Manages nodes, connectors, selection, and persistence.
/// </summary>
public class WorkflowDesignerViewModel : INotifyPropertyChanged
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public WorkflowDesignerViewModel(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;

        // ── Node commands ──
        AddStageCommand = new RelayCommand(_ => AddNode("Stage"), _ => _currentDefinitionId > 0);
        AddDecisionCommand = new RelayCommand(_ => AddNode("Decision"), _ => _currentDefinitionId > 0);
        AddForkCommand = new RelayCommand(_ => AddNode("Fork"), _ => _currentDefinitionId > 0);
        AddJoinCommand = new RelayCommand(_ => AddNode("Join"), _ => _currentDefinitionId > 0);
        DeleteNodeCommand = new RelayCommand(_ => DeleteSelectedNode(), _ => SelectedNode is not null);

        // ── Connector commands ──
        BeginConnectCommand = new RelayCommand(p => BeginConnect(p as DesignerNodeViewModel), _ => !IsConnecting);
        EndConnectCommand = new RelayCommand(p => EndConnect(p as DesignerNodeViewModel), _ => IsConnecting);
        CancelConnectCommand = new RelayCommand(_ => CancelConnect(), _ => IsConnecting);
        DeleteConnectorCommand = new RelayCommand(_ => DeleteSelectedConnector(), _ => SelectedConnector is not null);

        // ── Persistence ──
        SaveCommand = new RelayCommand(async _ => await SaveAsync(CancellationToken.None), _ => _currentDefinitionId > 0 && !_isLoading);
        LoadCommand = new RelayCommand(async _ => await LoadAsync(_currentDefinitionId, CancellationToken.None), _ => _currentDefinitionId > 0 && !_isLoading);
        ValidateCommand = new RelayCommand(_ => Validate(), _ => Nodes.Count > 0);
        AutoLayoutCommand = new RelayCommand(_ => RunAutoLayout(), _ => Nodes.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  State
    // ═══════════════════════════════════════════════════════════════════════

    private int _currentDefinitionId;
    /// <summary>ID of the WorkflowDefinition being edited.</summary>
    public int CurrentDefinitionId
    {
        get => _currentDefinitionId;
        set { _currentDefinitionId = value; OnPropertyChanged(); }
    }

    private string _definitionName = string.Empty;
    public string DefinitionName
    {
        get => _definitionName;
        set { _definitionName = value; OnPropertyChanged(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private bool _hasUnsavedChanges;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set { _hasUnsavedChanges = value; OnPropertyChanged(); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Collections
    // ═══════════════════════════════════════════════════════════════════════

    public ObservableCollection<DesignerNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<DesignerConnectorViewModel> Connectors { get; } = [];

    // ═══════════════════════════════════════════════════════════════════════
    //  Selection
    // ═══════════════════════════════════════════════════════════════════════

    private DesignerNodeViewModel? _selectedNode;
    public DesignerNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode is not null) _selectedNode.IsSelected = false;
            _selectedNode = value;
            if (_selectedNode is not null) _selectedNode.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedNode));
            // Clear connector selection when node selected
            if (value is not null) SelectedConnector = null;
        }
    }

    private DesignerConnectorViewModel? _selectedConnector;
    public DesignerConnectorViewModel? SelectedConnector
    {
        get => _selectedConnector;
        set
        {
            if (_selectedConnector is not null) _selectedConnector.IsSelected = false;
            _selectedConnector = value;
            if (_selectedConnector is not null) _selectedConnector.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedConnector));
            if (value is not null) SelectedNode = null;
        }
    }

    public bool HasSelectedNode => SelectedNode is not null;
    public bool HasSelectedConnector => SelectedConnector is not null;

    // ═══════════════════════════════════════════════════════════════════════
    //  Connect Mode (drawing a new connector)
    // ═══════════════════════════════════════════════════════════════════════

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        private set { _isConnecting = value; OnPropertyChanged(); }
    }

    private DesignerNodeViewModel? _connectSource;

    // ═══════════════════════════════════════════════════════════════════════
    //  Validation results
    // ═══════════════════════════════════════════════════════════════════════

    private ObservableCollection<string> _validationErrors = [];
    public ObservableCollection<string> ValidationErrors
    {
        get => _validationErrors;
        private set { _validationErrors = value; OnPropertyChanged(); }
    }

    private ObservableCollection<string> _validationWarnings = [];
    public ObservableCollection<string> ValidationWarnings
    {
        get => _validationWarnings;
        private set { _validationWarnings = value; OnPropertyChanged(); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════════════════════════════

    // Nodes
    public ICommand AddStageCommand { get; }
    public ICommand AddDecisionCommand { get; }
    public ICommand AddForkCommand { get; }
    public ICommand AddJoinCommand { get; }
    public ICommand DeleteNodeCommand { get; }

    // Connectors
    public ICommand BeginConnectCommand { get; }
    public ICommand EndConnectCommand { get; }
    public ICommand CancelConnectCommand { get; }
    public ICommand DeleteConnectorCommand { get; }

    // Persistence
    public ICommand SaveCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand AutoLayoutCommand { get; }

    // ═══════════════════════════════════════════════════════════════════════
    //  Node operations
    // ═══════════════════════════════════════════════════════════════════════

    private void AddNode(string nodeType)
    {
        // Place new node at a default position (offset from last node)
        var offsetX = 50 + Nodes.Count * 30;
        var offsetY = 200;

        var node = new DesignerNodeViewModel
        {
            NodeType = nodeType,
            Name = nodeType switch
            {
                "Start" => "התחלה",
                "End" => "סיום",
                "Decision" => "תנאי חדש",
                "Fork" => "פיצול",
                "Join" => "מיזוג",
                _ => $"שלב {Nodes.Count(n => n.NodeType == "Stage") + 1}"
            },
            X = offsetX,
            Y = offsetY,
            IsInitial = nodeType == "Start",
            IsFinal = nodeType == "End",
        };

        Nodes.Add(node);
        SelectedNode = node;
        HasUnsavedChanges = true;
    }

    private void DeleteSelectedNode()
    {
        if (SelectedNode is null) return;

        // Remove all connectors touching this node
        var toRemove = Connectors
            .Where(c => c.Source == SelectedNode || c.Target == SelectedNode)
            .ToList();
        foreach (var c in toRemove)
        {
            c.Dispose();
            Connectors.Remove(c);
        }

        Nodes.Remove(SelectedNode);
        SelectedNode = null;
        HasUnsavedChanges = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Connect operations
    // ═══════════════════════════════════════════════════════════════════════

    private void BeginConnect(DesignerNodeViewModel? source)
    {
        if (source is null) return;
        _connectSource = source;
        IsConnecting = true;
        StatusMessage = $"מצב חיבור: לחץ על צומת יעד (ESC לביטול)";
    }

    private void EndConnect(DesignerNodeViewModel? target)
    {
        if (_connectSource is null || target is null || target == _connectSource)
        {
            CancelConnect();
            return;
        }

        // Check duplicate
        var exists = Connectors.Any(c => c.Source == _connectSource && c.Target == target);
        if (exists)
        {
            StatusMessage = "⚠️ חיבור זה כבר קיים";
            CancelConnect();
            return;
        }

        var connector = new DesignerConnectorViewModel
        {
            Source = _connectSource,
            Target = target,
        };
        Connectors.Add(connector);
        SelectedConnector = connector;
        HasUnsavedChanges = true;

        CancelConnect();
        StatusMessage = "✅ חיבור נוצר";
    }

    private void CancelConnect()
    {
        _connectSource = null;
        IsConnecting = false;
        StatusMessage = null;
    }

    private void DeleteSelectedConnector()
    {
        if (SelectedConnector is null) return;
        SelectedConnector.Dispose();
        Connectors.Remove(SelectedConnector);
        SelectedConnector = null;
        HasUnsavedChanges = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Load / Save / Validate stubs (implement in step 5-6)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task LoadAsync(int definitionId, CancellationToken ct)
    {
        // TODO: implement in step 6
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        // TODO: implement in step 6
    }

    private void Validate()
    {
        // TODO: implement in step 5
    }

    private void RunAutoLayout()
    {
        // TODO: implement in step 7
    }

    // ═══════════════════════════════════════════════════════════════════════

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

### 2.4 רישום DI

הוסף ב-`App.xaml.cs` או בקובץ ה-DI registration:

```csharp
services.AddTransient<WorkflowDesignerViewModel>();
```

---

## שלב 3: בניית ה-Canvas UI (WPF)

### 3.1 מבנה כללי

צור שני קבצים:

```
SiNetProjectManagerV2/WPFUserControl/WorkflowDesignerView.xaml
SiNetProjectManagerV2/WPFUserControl/WorkflowDesignerView.xaml.cs
```

מבנה ויזואלי:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ROW 0: Toolbar                                                          │
│ [➕ שלב][🔷 תנאי][⑂ פיצול][⊕ מיזוג] │ [🔗 חבר][🗑 מחק] │ [💾][✅][🔄] │
├────────────────────────────────────────┬─────────────────────────────────┤
│ ROW 1: Canvas Area (Col 0)            │ ROW 1: Properties Panel (Col 2) │
│                                        │                                 │
│    ┌──────┐                            │  ┌─ Node Properties ─────────┐  │
│    │Start │───►┌────────┐              │  │ שם: [___________]         │  │
│    └──────┘    │ Stage1 │              │  │ סוג: Stage ▼              │  │
│                └───┬────┘              │  │ תיאור: [___________]     │  │
│                    ▼                   │  │ ☐ התחלתי  ☐ סופי        │  │
│               ┌─────────┐             │  │ צבע: [🔵▼]               │  │
│               │🔷 תנאי  │             │  ├─ משימות ────────────────┤  │
│               └────┬────┘             │  │ [DataGrid: tasks]        │  │
│              כן/   \לא               │  │ [+ הוסף] [- הסר]        │  │
│          ┌───┘     └────┐             │  └───────────────────────────┘  │
│          ▼              ▼             │                                 │
│    ┌────────┐    ┌────────┐           │  ┌─ Connector Properties ───┐  │
│    │ Stage2 │    │ Stage3 │           │  │ (visible when connector   │  │
│    └───┬────┘    └────────┘           │  │  is selected)             │  │
│        ▼                              │  │ תווית: [___]             │  │
│    ┌──────┐                           │  │ תנאי: [___]              │  │
│    │ End  │                           │  │ עדיפות: [1]              │  │
│    └──────┘                           │  └───────────────────────────┘  │
│                                        │                                 │
│  [Zoom: 100%] [Fit] [Center]          │                                 │
├────────────────────────────────────────┴─────────────────────────────────┤
│ ROW 2: Status bar  │  Validation: ✅ תקין │ 🔴 2 שגיאות │ 🟡 1 אזהרה  │
└──────────────────────────────────────────────────────────────────────────┘
```

### 3.2 XAML — מבנה ראשי

```xaml
<UserControl x:Class="SiNetProjectManagerV2.WPFUserControl.WorkflowDesignerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:vm="clr-namespace:SiNetSQL.MVVM;assembly=SiNetSQL"
             mc:Ignorable="d"
             d:DesignHeight="600" d:DesignWidth="1100"
             FontFamily="{DynamicResource AppFontFamily}"
             FontSize="{DynamicResource AppFontSize}"
             Background="{DynamicResource AppBackground}"
             Foreground="{DynamicResource AppForeground}"
             FlowDirection="RightToLeft">

    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>

        <!-- ═══ NODE DATA TEMPLATES (selected by NodeType via DataTemplateSelector) ═══ -->

        <!-- Stage node: rounded rectangle -->
        <DataTemplate x:Key="StageNodeTemplate">
            <Border CornerRadius="8"
                    MinWidth="{Binding DefaultWidth}" MinHeight="{Binding DefaultHeight}"
                    Background="{Binding NodeBrush}" Opacity="0.9"
                    BorderBrush="{Binding IsSelected, Converter=...}" BorderThickness="2"
                    Cursor="Hand" Padding="8,6">
                <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock Text="{Binding Name}" FontWeight="Bold" Foreground="White"
                               TextAlignment="Center" TextWrapping="Wrap" MaxWidth="120"/>
                    <TextBlock Text="{Binding Description}" FontSize="10" Foreground="#E0E0E0"
                               TextAlignment="Center" TextTrimming="CharacterEllipsis"
                               Visibility="{Binding Description, Converter=...}"/>
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- Decision node: diamond shape via RotateTransform -->
        <DataTemplate x:Key="DecisionNodeTemplate">
            <Grid Width="{Binding DefaultWidth}" Height="{Binding DefaultHeight}">
                <Border Background="{Binding NodeBrush}" CornerRadius="4"
                        RenderTransformOrigin="0.5,0.5"
                        BorderBrush="{Binding IsSelected, Converter=...}" BorderThickness="2">
                    <Border.RenderTransform>
                        <RotateTransform Angle="45"/>
                    </Border.RenderTransform>
                </Border>
                <!-- Text NOT rotated — overlaid -->
                <TextBlock Text="{Binding Name}" FontWeight="Bold" Foreground="White"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           FontSize="11" TextAlignment="Center"/>
            </Grid>
        </DataTemplate>

        <!-- Start/End node: circle -->
        <DataTemplate x:Key="CircleNodeTemplate">
            <Border CornerRadius="30"
                    Width="{Binding DefaultWidth}" Height="{Binding DefaultHeight}"
                    Background="{Binding NodeBrush}"
                    BorderBrush="{Binding IsSelected, Converter=...}" BorderThickness="2"
                    Cursor="Hand">
                <TextBlock Text="{Binding Name}" FontWeight="Bold" Foreground="White"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           FontSize="11"/>
            </Border>
        </DataTemplate>

        <!-- Fork/Join node: thick horizontal bar -->
        <DataTemplate x:Key="BarNodeTemplate">
            <Border Width="{Binding DefaultWidth}" Height="{Binding DefaultHeight}"
                    Background="{Binding NodeBrush}" CornerRadius="4"
                    BorderBrush="{Binding IsSelected, Converter=...}" BorderThickness="2"
                    Cursor="Hand">
                <TextBlock Text="{Binding IconText}" HorizontalAlignment="Center"
                           VerticalAlignment="Center" FontSize="16"/>
            </Border>
        </DataTemplate>

        <!-- ═══ CONNECTOR TEMPLATE (arrow line + label) ═══ -->

        <DataTemplate x:Key="ConnectorTemplate">
            <Canvas>
                <!-- Arrow line -->
                <Line X1="{Binding StartPoint.X}" Y1="{Binding StartPoint.Y}"
                      X2="{Binding EndPoint.X}" Y2="{Binding EndPoint.Y}"
                      Stroke="{Binding IsSelected, Converter=...}"
                      StrokeThickness="2" />

                <!-- Arrowhead (triangle at end) -->
                <!-- Use a small Polygon positioned at EndPoint with rotation -->

                <!-- Label at midpoint -->
                <TextBlock Text="{Binding Label}"
                           Canvas.Left="{Binding MidPoint.X}"
                           Canvas.Top="{Binding MidPoint.Y}"
                           FontSize="10" FontWeight="SemiBold"
                           Foreground="#424242" Background="#FFFFFFCC"
                           Padding="3,1"
                           Visibility="{Binding Label, Converter=...}"/>
            </Canvas>
        </DataTemplate>
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Toolbar -->
            <RowDefinition Height="*"/>     <!-- Canvas + Properties -->
            <RowDefinition Height="Auto"/>  <!-- Status bar -->
        </Grid.RowDefinitions>

        <!-- ═══ ROW 0: TOOLBAR ═══ -->
        <Border Grid.Row="0" Background="#F5F5F5" Padding="8,6" BorderBrush="#E0E0E0" BorderThickness="0,0,0,1">
            <WrapPanel Orientation="Horizontal">
                <!-- Add nodes -->
                <TextBlock Text="הוסף:" VerticalAlignment="Center" Margin="0,0,8,0" FontWeight="SemiBold"/>
                <Button Content="➕ שלב" Command="{Binding AddStageCommand}" Padding="8,4" Margin="0,0,4,0"
                        Background="#E3F2FD" BorderBrush="#90CAF9"/>
                <Button Content="🔷 תנאי" Command="{Binding AddDecisionCommand}" Padding="8,4" Margin="0,0,4,0"
                        Background="#FFF3E0" BorderBrush="#FFB74D"/>
                <Button Content="⑂ פיצול" Command="{Binding AddForkCommand}" Padding="8,4" Margin="0,0,4,0"
                        Background="#F3E5F5" BorderBrush="#CE93D8"/>
                <Button Content="⊕ מיזוג" Command="{Binding AddJoinCommand}" Padding="8,4" Margin="0,0,16,0"
                        Background="#E0F2F1" BorderBrush="#80CBC4"/>

                <Border BorderBrush="#E0E0E0" BorderThickness="1,0,0,0" Margin="0,2,12,2"/>

                <!-- Connect / Delete -->
                <Button Content="🔗 חבר" Command="{Binding BeginConnectCommand}" Padding="8,4" Margin="0,0,4,0"
                        Background="#E8EAF6" BorderBrush="#9FA8DA"
                        ToolTip="לחץ, ואז בחר צומת מקור ← צומת יעד"/>
                <Button Content="🗑 מחק" Command="{Binding DeleteNodeCommand}" Padding="8,4" Margin="0,0,16,0"
                        Background="#FFEBEE" BorderBrush="#EF9A9A"/>

                <Border BorderBrush="#E0E0E0" BorderThickness="1,0,0,0" Margin="0,2,12,2"/>

                <!-- Persistence -->
                <Button Content="💾 שמור" Command="{Binding SaveCommand}" Padding="8,4" Margin="0,0,4,0"
                        Background="#C8E6C9" BorderBrush="#66BB6A" FontWeight="Bold"/>
                <Button Content="✅ בדוק" Command="{Binding ValidateCommand}" Padding="8,4" Margin="0,0,4,0"/>
                <Button Content="🔄 סדר אוטומטי" Command="{Binding AutoLayoutCommand}" Padding="8,4"/>

                <!-- Connect mode indicator -->
                <TextBlock Text="🔗 מצב חיבור — לחץ על צומת יעד (ESC לביטול)"
                           Foreground="#FF5722" FontWeight="Bold" VerticalAlignment="Center" Margin="16,0,0,0"
                           Visibility="{Binding IsConnecting, Converter={StaticResource BoolToVis}}"/>
            </WrapPanel>
        </Border>

        <!-- ═══ ROW 1: CANVAS + PROPERTIES PANEL ═══ -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" MinWidth="400"/>         <!-- Canvas -->
                <ColumnDefinition Width="Auto"/>                     <!-- Splitter -->
                <ColumnDefinition Width="280" MinWidth="200"/>       <!-- Properties -->
            </Grid.ColumnDefinitions>

            <!-- ─── Canvas Area ─── -->
            <Border Grid.Column="0" Background="White" BorderBrush="#E0E0E0" BorderThickness="1"
                    ClipToBounds="True">
                <!-- ScrollViewer for pan, with ScaleTransform for zoom -->
                <ScrollViewer HorizontalScrollBarVisibility="Auto"
                              VerticalScrollBarVisibility="Auto"
                              x:Name="CanvasScroller">
                    <Grid x:Name="CanvasRoot" RenderTransformOrigin="0,0"
                          Background="White"
                          Width="2000" Height="1500">
                        <Grid.RenderTransform>
                            <TransformGroup>
                                <ScaleTransform x:Name="CanvasScale" ScaleX="1" ScaleY="1"/>
                            </TransformGroup>
                        </Grid.RenderTransform>

                        <!-- Grid dots background (optional visual aid) -->
                        <Rectangle Fill="White" IsHitTestVisible="False">
                            <Rectangle.OpacityMask>
                                <DrawingBrush TileMode="Tile" Viewport="0,0,20,20" ViewportUnits="Absolute">
                                    <DrawingBrush.Drawing>
                                        <GeometryDrawing Brush="#20000000">
                                            <GeometryDrawing.Geometry>
                                                <EllipseGeometry Center="0,0" RadiusX="0.8" RadiusY="0.8"/>
                                            </GeometryDrawing.Geometry>
                                        </GeometryDrawing>
                                    </DrawingBrush.Drawing>
                                </DrawingBrush>
                            </Rectangle.OpacityMask>
                        </Rectangle>

                        <!-- Layer 1: Connectors (behind nodes) -->
                        <ItemsControl ItemsSource="{Binding Connectors}"
                                      ItemTemplate="{StaticResource ConnectorTemplate}"
                                      Panel.ZIndex="0">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <Canvas/>
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <!-- Position is handled inside the template via Line coordinates -->
                        </ItemsControl>

                        <!-- Layer 2: Nodes (above connectors) -->
                        <ItemsControl ItemsSource="{Binding Nodes}"
                                      Panel.ZIndex="1">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <Canvas/>
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemContainerStyle>
                                <Style TargetType="ContentPresenter">
                                    <Setter Property="Canvas.Left" Value="{Binding X}"/>
                                    <Setter Property="Canvas.Top" Value="{Binding Y}"/>
                                </Style>
                            </ItemsControl.ItemContainerStyle>
                            <!-- Use DataTemplateSelector to pick template by NodeType -->
                        </ItemsControl>
                    </Grid>
                </ScrollViewer>
            </Border>

            <!-- ─── Splitter ─── -->
            <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Center"
                          Background="Transparent" Cursor="SizeWE"/>

            <!-- ─── Properties Panel ─── -->
            <Border Grid.Column="2" Background="#FAFAFA" BorderBrush="#E0E0E0"
                    BorderThickness="1,0,0,0" Padding="10">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <!-- Node properties (visible when node selected) -->
                        <StackPanel Visibility="{Binding HasSelectedNode, Converter={StaticResource BoolToVis}}">
                            <TextBlock Text="מאפייני צומת" FontWeight="Bold" FontSize="14" Margin="0,0,0,10"/>
                            <!-- Name -->
                            <TextBlock Text="שם:" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding SelectedNode.Name, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8"/>
                            <!-- Description -->
                            <TextBlock Text="תיאור:" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding SelectedNode.Description, UpdateSourceTrigger=PropertyChanged}"
                                     AcceptsReturn="True" Height="60" TextWrapping="Wrap" Margin="0,0,0,8"/>
                            <!-- Flags -->
                            <CheckBox Content="שלב התחלתי (Initial)" IsChecked="{Binding SelectedNode.IsInitial}" Margin="0,0,0,4"/>
                            <CheckBox Content="שלב סופי (Final)" IsChecked="{Binding SelectedNode.IsFinal}" Margin="0,0,0,8"/>
                            <!-- Condition (for Decision) -->
                            <StackPanel Visibility="{Binding SelectedNode.IsDecision, Converter={StaticResource BoolToVis}}">
                                <TextBlock Text="ביטוי תנאי:" Margin="0,0,0,4"/>
                                <TextBox Text="{Binding SelectedNode.ConditionExpression, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,0,0,8"/>
                            </StackPanel>
                            <!-- Tasks (for Stage) -->
                            <StackPanel Visibility="{Binding SelectedNode.IsStage, Converter={StaticResource BoolToVis}}">
                                <TextBlock Text="משימות בשלב:" FontWeight="SemiBold" Margin="0,8,0,4"/>
                                <DataGrid ItemsSource="{Binding SelectedNode.Tasks}"
                                          AutoGenerateColumns="False" CanUserAddRows="False"
                                          MaxHeight="200" IsReadOnly="True">
                                    <DataGrid.Columns>
                                        <DataGridTextColumn Header="סוג" Binding="{Binding TaskType.Title}" Width="*"/>
                                        <DataGridTextColumn Header="אחראי" Binding="{Binding DefaultAssignee.Name}" Width="80"/>
                                        <DataGridCheckBoxColumn Header="חובה" Binding="{Binding IsRequired}" Width="50"/>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </StackPanel>

                        <!-- Connector properties (visible when connector selected) -->
                        <StackPanel Visibility="{Binding HasSelectedConnector, Converter={StaticResource BoolToVis}}">
                            <TextBlock Text="מאפייני חיבור" FontWeight="Bold" FontSize="14" Margin="0,0,0,10"/>
                            <TextBlock Text="תווית:" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding SelectedConnector.Label, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8"/>
                            <TextBlock Text="תנאי:" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding SelectedConnector.Condition, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8"/>
                            <TextBlock Text="עדיפות:" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding SelectedConnector.Priority, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8"/>
                            <!-- Source/Target display -->
                            <TextBlock Margin="0,8,0,0" Opacity="0.7">
                                <Run Text="מ: "/><Run Text="{Binding SelectedConnector.Source.Name, Mode=OneWay}" FontWeight="SemiBold"/>
                                <Run Text=" → "/><Run Text="{Binding SelectedConnector.Target.Name, Mode=OneWay}" FontWeight="SemiBold"/>
                            </TextBlock>
                        </StackPanel>

                        <!-- No selection message -->
                        <TextBlock Text="בחר צומת או חיבור לעריכה"
                                   Foreground="Gray" FontStyle="Italic"
                                   HorizontalAlignment="Center" Margin="0,40,0,0">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <MultiDataTrigger>
                                            <MultiDataTrigger.Conditions>
                                                <Condition Binding="{Binding HasSelectedNode}" Value="False"/>
                                                <Condition Binding="{Binding HasSelectedConnector}" Value="False"/>
                                            </MultiDataTrigger.Conditions>
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </MultiDataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Grid>

        <!-- ═══ ROW 2: STATUS BAR ═══ -->
        <Border Grid.Row="2" Background="#F5F5F5" Padding="8,4" BorderBrush="#E0E0E0" BorderThickness="0,1,0,0">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding StatusMessage}" Foreground="Gray" Margin="0,0,16,0"/>
                <TextBlock Text="{Binding Nodes.Count, StringFormat='צמתים: {0}'}" Margin="0,0,12,0"/>
                <TextBlock Text="{Binding Connectors.Count, StringFormat='חיבורים: {0}'}" Margin="0,0,12,0"/>
                <TextBlock Text="● שינויים לא שמורים" Foreground="#FF5722" FontWeight="Bold"
                           Visibility="{Binding HasUnsavedChanges, Converter={StaticResource BoolToVis}}"/>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

### 3.3 Code-Behind — גרירת צמתים (Drag)

```
מיקום: SiNetProjectManagerV2/WPFUserControl/WorkflowDesignerView.xaml.cs
```

ה-code-behind מטפל **רק** באינטראקציות שלא ניתן לעשות ב-XAML binding:
1. **Node drag** — `MouseLeftButtonDown` / `MouseMove` / `MouseLeftButtonUp`
2. **Node click for selection** — `PreviewMouseLeftButtonDown` על צומת
3. **Connect mode click** — `PreviewMouseLeftButtonDown` על צומת כשב-connect-mode
4. **Zoom** — `PreviewMouseWheel` על ה-Canvas
5. **ESC** — ביטול connect mode

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPFUserControl;

public partial class WorkflowDesignerView : UserControl
{
    // ── Drag state ──
    private bool _isDragging;
    private Point _dragStart;
    private DesignerNodeViewModel? _dragNode;

    public WorkflowDesignerView()
    {
        InitializeComponent();

        // Resolve ViewModel via DI
        var vm = App.ServiceProvider.GetRequiredService<WorkflowDesignerViewModel>();
        DataContext = vm;
    }

    private WorkflowDesignerViewModel ViewModel => (WorkflowDesignerViewModel)DataContext;

    // ── Node mouse down: start drag or connect ──
    // Attach this handler to each node ContentPresenter via EventSetter or Loaded event
    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DesignerNodeViewModel node) return;

        if (ViewModel.IsConnecting)
        {
            // In connect mode — this is the target
            ViewModel.EndConnectCommand.Execute(node);
            e.Handled = true;
            return;
        }

        // Select node
        ViewModel.SelectedNode = node;

        // Begin drag
        _isDragging = true;
        _dragNode = node;
        _dragStart = e.GetPosition(CanvasRoot);
        fe.CaptureMouse();
        e.Handled = true;
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _dragNode is null) return;

        var pos = e.GetPosition(CanvasRoot);
        _dragNode.X += pos.X - _dragStart.X;
        _dragNode.Y += pos.Y - _dragStart.Y;
        _dragStart = pos;

        ViewModel.HasUnsavedChanges = true;
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragNode = null;
        if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
    }

    // ── Zoom via mouse wheel ──
    private void Canvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        var delta = e.Delta > 0 ? 1.1 : 0.9;
        var newScale = CanvasScale.ScaleX * delta;
        newScale = Math.Clamp(newScale, 0.3, 3.0);

        CanvasScale.ScaleX = newScale;
        CanvasScale.ScaleY = newScale;
        e.Handled = true;
    }

    // ── ESC to cancel connect mode ──
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsConnecting)
        {
            ViewModel.CancelConnectCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (ViewModel.SelectedNode is not null)
                ViewModel.DeleteNodeCommand.Execute(null);
            else if (ViewModel.SelectedConnector is not null)
                ViewModel.DeleteConnectorCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
```

### 3.4 DataTemplateSelector — בחירת template לפי NodeType

```
מיקום: SiNetProjectManagerV2/WPFUserControl/NodeTemplateSelector.cs
```

```csharp
using System.Windows;
using System.Windows.Controls;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Selects the correct DataTemplate for a designer node based on its NodeType.
/// </summary>
public class NodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StageTemplate { get; set; }
    public DataTemplate? DecisionTemplate { get; set; }
    public DataTemplate? CircleTemplate { get; set; }   // Start / End
    public DataTemplate? BarTemplate { get; set; }       // Fork / Join

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not DesignerNodeViewModel node) return base.SelectTemplate(item, container);

        return node.NodeType switch
        {
            "Stage" => StageTemplate,
            "Decision" => DecisionTemplate,
            "Start" or "End" => CircleTemplate,
            "Fork" or "Join" => BarTemplate,
            _ => StageTemplate
        };
    }
}
```

להשתמש ב-XAML כך:

```xaml
<local:NodeTemplateSelector x:Key="NodeSelector"
    StageTemplate="{StaticResource StageNodeTemplate}"
    DecisionTemplate="{StaticResource DecisionNodeTemplate}"
    CircleTemplate="{StaticResource CircleNodeTemplate}"
    BarTemplate="{StaticResource BarNodeTemplate}"/>

<ItemsControl ItemsSource="{Binding Nodes}"
              ItemTemplateSelector="{StaticResource NodeSelector}"
              ... />
```

### 3.5 סוגי צמתים — טבלת רפרנס

| NodeType | Template | צורה | צבע ברירת מחדל | גודל ברירת מחדל | אייקון |
|----------|----------|-------|----------------|-----------------|--------|
| Start    | CircleNodeTemplate | עיגול | #4CAF50 ירוק | 60×60 | 🟢 |
| End      | CircleNodeTemplate | עיגול | #F44336 אדום | 60×60 | 🔴 |
| Stage    | StageNodeTemplate | מלבן מעוגל | #2196F3 כחול | 140×80 | 🔵 |
| Decision | DecisionNodeTemplate | מעוין (45° rotate) | #FF9800 כתום | 100×70 | 🔷 |
| Fork     | BarNodeTemplate | פס אופקי עבה | #9C27B0 סגול | 120×30 | ⑂ |
| Join     | BarNodeTemplate | פס אופקי עבה | #009688 טורקיז | 120×30 | ⊕ |

### 3.6 חיצים (Connectors) — דגשים

- **קו רגיל**: `Line` מ-`StartPoint` ל-`EndPoint` עם `StrokeThickness="2"`
- **ראש חץ**: `Polygon` (משולש) מסובב לכיוון הקו, ממוקם ב-`EndPoint`
- **תווית**: `TextBlock` ממוקם ב-`MidPoint` עם רקע שקוף-לבן
- **מצב בחירה**: כשחיבור נבחר — `Stroke="#FF5722"` + `StrokeThickness="3"`
- **לולאה (loop back)**: כש-`EndPoint.Y < StartPoint.Y` — השתמש ב-`PathGeometry` עם `BezierSegment` שיוצר עיקול מסביב
- **לחיצה על חיבור**: הוסף `Line` שקוף עם `StrokeThickness="12"` (hit area) מתחת לקו הנראה

---

## שלב 4: Properties Panel (פאנל מאפיינים)

כאשר נבחר צומת — הצג פאנל עריכה בצד ימין:

### עבור Stage:
- שם השלב (TextBox)
- תיאור (TextBox multiline)
- צבע (ColorPicker / ComboBox של צבעים)
- IsInitial / IsFinal (CheckBox)
- רשימת משימות (DataGrid של `WorkflowStageTask` — סוג משימה, אחראי, חובה?)
- הוספת/הסרת משימה

### עבור Decision:
- שם התנאי
- ביטוי תנאי (`Condition`) — כרגע טקסט חופשי, בעתיד אפשר expression builder
- מעברים יוצאים — לכל מעבר: תווית (Label) ותנאי

### עבור Connector (כשנבחר חיבור):
- Label (תווית על החץ)
- Condition (תנאי)
- Priority (סדר עדיפות)

---

## שלב 5: Validation Service

צור שירות שמוודא תקינות Workflow לפני שמירה:

```
מיקום: SiNetSQL/Services/Workflow/WorkflowDesignerValidationService.cs
```

בדיקות:
1. **יש בדיוק צומת Start אחד** שמסומן `IsInitial`
2. **יש לפחות צומת End אחד** שמסומן `IsFinal`
3. **כל צומת נגיש** — אפשר להגיע אליו מ-Start (DFS/BFS)
4. **כל צומת Decision יש לפחות 2 מעברים יוצאים** (כן/לא)
5. **כל Fork יש לפחות 2 מעברים יוצאים**
6. **כל Join יש לפחות 2 מעברים נכנסים**
7. **אין צמתים "יתומים"** — ללא חיבורים כלל
8. **אין לולאות אינסופיות** — אם יש לולאה, חייב להיות מוצא (Decision שמוציא מהלולאה)

הצג שגיאות/אזהרות ב-UI:
- 🔴 Error: בעיה שמונעת שמירה
- 🟡 Warning: בעיה שמומלץ לתקן

---

## שלב 6: שמירה וטעינה

### שמירה (Save):
1. Validate — אם יש errors, אל תשמור
2. המר `DesignerNodeViewModel` → `WorkflowStageDefinition` (עם `CanvasX`, `CanvasY`, `NodeType`)
3. המר `DesignerConnectorViewModel` → `WorkflowTransitionRule` (עם `Condition`, `Label`, `Priority`)
4. שמור ב-Transaction אחד ל-DB
5. עדכן ID-ים שחזרו מה-DB

### טעינה (Load):
1. טען `WorkflowDefinition` עם `Include(Stages)` ו-`Include(TransitionRules)`
2. המר `WorkflowStageDefinition` → `DesignerNodeViewModel`
3. המר `WorkflowTransitionRule` → `DesignerConnectorViewModel`
4. אם אין מיקומי Canvas (workflow ישן) — הפעל Auto-Layout

---

## שלב 7: Auto-Layout Algorithm

כאשר Workflow נטען בלי מיקומי Canvas, או כשמשתמש לוחץ "סדר אוטומטי":

1. מצא את צומת Start
2. בצע BFS/topological sort
3. סדר צמתים בשכבות (layers) — כל שכבה = level ב-BFS
4. חלק כל שכבה אופקית (מרווח שווה)
5. עבור Decisions — צמתי "כן" ו"לא" ישבו בשכבות שונות בציר X
6. מרחק ברירת מחדל: 200px אופקי, 150px אנכי

---

## שלב 8: שילוב בחלון WorkflowManagement הקיים

### אפשרות מומלצת: Tab חדש

הוסף Tab חדש בחלון `WorkflowManagementWindow` בשם **"עורך ויזואלי"** (Visual Designer):

- Tab 1: Builder (הקיים — עץ)
- Tab 2: **Visual Designer** (חדש — Canvas)
- Tab 3: Policy
- Tab 4: Dashboard
- Tab 5: Behavior
- Tab 6: Help

שני ה-Tabs (Builder ו-Designer) עורכים את אותם נתונים. כאשר עוברים בין tabs — טען מחדש.

---

## כללים חשובים

- **אל תשבור קוד קיים** — ה-Builder, Dashboard, Engine, ו-Policy צריכים להמשיך לעבוד בדיוק כמו היום
- **Dependency Injection** — כל Services חדשים חייבים DI
- **CancellationToken** — בכל async method
- **MVVM** — ה-Canvas UI עובד עם ViewModel, לא code-behind
- **אין hard-coded values** — צבעים, גדלים, טקסטים ב-Resources או constants
- **Migration** — ספק פקודה, אל תריץ אוטומטית
