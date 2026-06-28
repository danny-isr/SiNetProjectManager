using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;
using SiNetSQL.Services.Workflow;
using SiNetSQL.Services.TaskLifecycle;
using WorkflowStatus = SiNet.Domain.Workflow.WorkflowStatus;

namespace SiNetProjectManagerV2.Dialogs;

// ═══════════════════════════════════════════════════════════════════════════
// Shared DTO classes (reused across tabs)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a ProjectType row in the Policy tab left-side list.
/// </summary>
public sealed class PolicyProjectTypeItem : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Title { get; init; } = "";

    private int _mappingCount;
    public int MappingCount
    {
        get => _mappingCount;
        set { _mappingCount = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Represents a WorkflowStageDefinition activation row for a single ProjectType
/// (Policy → Stages sub-tab). Editable: <see cref="IsActive"/>, <see cref="IsRequired"/>.
/// </summary>
public sealed class PolicyStageItem : INotifyPropertyChanged
{
    public int? MappingId { get; set; }
    public int StageDefinitionId { get; init; }
    public string StageName { get; init; } = "";
    public string StageCode { get; init; } = "";
    public int SortOrder { get; init; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    private bool _isRequired;
    public bool IsRequired
    {
        get => _isRequired;
        set { _isRequired = value; OnPropertyChanged(); }
    }

    public bool IsDirty { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Represents a Discipline (TaskType) activation row for a single ProjectType
/// (Policy → Disciplines sub-tab).
/// </summary>
public sealed class PolicyDisciplineItem : INotifyPropertyChanged
{
    public int? MappingId { get; set; }
    public int DisciplineTaskTypeId { get; init; }
    public string DisciplineName { get; init; } = "";
    public string DisciplineCode { get; init; } = "";

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    private bool _isRequired;
    public bool IsRequired
    {
        get => _isRequired;
        set { _isRequired = value; OnPropertyChanged(); }
    }

    public bool IsDirty { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Represents a WorkflowDefinition row in the Policy tab right-side checklist.
/// </summary>
public sealed class PolicyWorkflowMappingItem : INotifyPropertyChanged
{
    public int DefinitionId { get; init; }
    public string DefinitionName { get; init; } = "";
    public string? DefinitionDescription { get; init; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    private bool _isDefault;
    public bool IsDefault
    {
        get => _isDefault;
        set { _isDefault = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ═══════════════════════════════════════════════════════════════════════════
// Tree node wrappers (Builder tab)
// ═══════════════════════════════════════════════════════════════════════════

public sealed record MgmtWorkflowNode(WorkflowDefinition Definition);
public sealed record MgmtStageNode(WorkflowStageDefinition Stage, WorkflowDefinition Definition);
public sealed record MgmtStageTaskNode(WorkflowStageTask StageTask, WorkflowStageDefinition Stage);
public sealed record MgmtTransGroupNode(WorkflowStageDefinition Stage, WorkflowDefinition Definition, bool IsForward);
public sealed record MgmtTransitionNode(WorkflowTransitionRule Rule, WorkflowDefinition Definition);
public sealed record MgmtTaskGroupNode(WorkflowStageDefinition Stage, WorkflowDefinition Definition);

/// <summary>
/// Represents a TaskBehaviorDefinition row in the Behavior tab left-side list.
/// </summary>
public sealed class BehaviorListItem
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ActiveIcon { get; init; } = "";
    public string Summary { get; init; } = "";
}

/// <summary>
/// Unified Workflow Management window — consolidates Builder, Policy, and Dashboard
/// into a single tabbed interface for complete workflow administration.
/// </summary>
public partial class WorkflowManagementWindow : Window
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared
    // ═══════════════════════════════════════════════════════════════════════

    private IDbContextFactory<SiNetSQLDbContext>? GetFactory() =>
        App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 1: Builder state
    // ═══════════════════════════════════════════════════════════════════════

    private List<Siuser> _builderEmployees = [];
    private List<TaskType> _builderTaskTypes = [];
    private bool _builderLoaded;

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 2: Policy state
    // ═══════════════════════════════════════════════════════════════════════

    private ObservableCollection<PolicyProjectTypeItem> _policyAllProjectTypes = [];
    private List<WorkflowDefinition> _policyAllDefinitions = [];
    private readonly Dictionary<int, List<PolicyMappingState>> _policyMappingMap = [];
    private int? _policySelectedProjectTypeId;
    private bool _policyLoaded;

    // Stages / Disciplines sub-tab state
    private List<WorkflowStageDefinition> _policyAllStages = [];
    private List<TaskType> _policyAllDisciplineTaskTypes = [];
    private readonly Dictionary<int, ObservableCollection<PolicyStageItem>> _policyStagesMap = [];
    private readonly Dictionary<int, ObservableCollection<PolicyDisciplineItem>> _policyDisciplinesMap = [];
    private bool _policyStagesLoaded;
    private bool _policyDisciplinesLoaded;

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 3: Dashboard state
    // ═══════════════════════════════════════════════════════════════════════

    private IWorkflowQueryService? _dashboardQueryService;
    private WorkflowTaskOrchestrator? _dashboardOrchestrator;
    private IProjectWorkflowPolicyService? _dashboardPolicyService;
    private List<Project> _dashboardProjects = [];
    private Project? _dashboardSelectedProject;
    private List<WorkflowDefinitionDto> _dashboardDefinitions = [];
    private bool _dashboardLoaded;

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 4: Behavior state
    // ═══════════════════════════════════════════════════════════════════════

    private List<TaskType> _behaviorTaskTypes = [];
    private List<ProjectAssignmentStatus> _behaviorStatuses = [];
    private List<BehaviorListItem> _behaviorItems = [];
    private bool _behaviorLoaded;

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 5: Help state
    // ═══════════════════════════════════════════════════════════════════════

    private bool _helpLoaded;

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor
    // ═══════════════════════════════════════════════════════════════════════

    public WorkflowManagementWindow()
    {
        InitializeComponent();

        // Load the first tab (Builder) immediately
        LoadBuilderTab();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TAB SWITCHING — lazy load each tab on first visit
    // ═══════════════════════════════════════════════════════════════════════

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabs) return;

        if (MainTabs.SelectedItem == BuilderTab && !_builderLoaded)
            LoadBuilderTab();
        else if (MainTabs.SelectedItem == PolicyTab && !_policyLoaded)
            LoadPolicyTab();
        else if (MainTabs.SelectedItem == DashboardTab && !_dashboardLoaded)
            LoadDashboardTab();
        else if (MainTabs.SelectedItem == BehaviorTab && !_behaviorLoaded)
            LoadBehaviorTab();
        else if (MainTabs.SelectedItem == HelpTab && !_helpLoaded)
            LoadHelpTab();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  TAB 1: BUILDER — Workflow → Stage → Task tree                      ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    #region Builder Tab

    private void LoadBuilderTab()
    {
        _builderLoaded = true;
        LoadBuilderReferenceData();
        BuildTree();
    }

    private void LoadBuilderReferenceData()
    {
        var factory = GetFactory();
        if (factory == null) return;

        using var db = factory.CreateDbContext();

        _builderEmployees = db.Siusers
            .Where(u => u.IsActive && u.Email != null && u.Email != "")
            .OrderBy(u => u.Name)
            .AsNoTracking()
            .ToList();

        _builderTaskTypes = db.TaskTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .AsNoTracking()
            .ToList();
    }

    private void BuildTree()
    {
        WorkflowTree.Items.Clear();

        var factory = GetFactory();
        if (factory == null) return;

        using var db = factory.CreateDbContext();

        var definitions = db.WorkflowDefinitions
            .Include(d => d.Stages.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.StageTasks.OrderBy(st => st.SortOrder))
                    .ThenInclude(st => st.TaskType)
            .Include(d => d.Stages)
                .ThenInclude(s => s.StageTasks)
                    .ThenInclude(st => st.DefaultAssignee)
            .Include(d => d.TransitionRules)
            .OrderBy(d => d.Code)
            .AsNoTracking()
            .ToList();

        int totalTasks = 0;

        foreach (var def in definitions)
        {
            var defItem = new TreeViewItem
            {
                Tag = new MgmtWorkflowNode(def),
                IsExpanded = true,
            };
            defItem.Header = BuildWorkflowHeader(def);

            foreach (var stage in def.Stages.OrderBy(s => s.SortOrder))
            {
                var stageItem = new TreeViewItem
                {
                    Tag = new MgmtStageNode(stage, def),
                    IsExpanded = false,
                };
                stageItem.Header = BuildStageHeader(stage, def);

                // Outgoing transitions from this stage
                var outgoing = def.TransitionRules
                    .Where(r => r.FromStageId == stage.Id)
                    .ToList();

                var forward = outgoing
                    .Where(r => def.Stages.FirstOrDefault(s => s.Id == r.ToStageId) is { } to
                             && to.SortOrder > stage.SortOrder)
                    .ToList();

                var backward = outgoing
                    .Where(r => def.Stages.FirstOrDefault(s => s.Id == r.ToStageId) is { } to
                             && to.SortOrder < stage.SortOrder)
                    .ToList();

                // ➡️ Forward transitions group
                var fwdGroup = new TreeViewItem
                {
                    Tag = new MgmtTransGroupNode(stage, def, true),
                    IsExpanded = true,
                };
                fwdGroup.Header = BuildTransGroupHeader("➡️", "קדימה", forward.Count);
                foreach (var rule in forward)
                {
                    var toStage = def.Stages.First(s => s.Id == rule.ToStageId);
                    fwdGroup.Items.Add(new TreeViewItem
                    {
                        Tag = new MgmtTransitionNode(rule, def),
                        Header = BuildTransitionHeader(rule, toStage, true),
                    });
                }
                stageItem.Items.Add(fwdGroup);

                // ↩️ Backward transitions group
                var bwdGroup = new TreeViewItem
                {
                    Tag = new MgmtTransGroupNode(stage, def, false),
                    IsExpanded = true,
                };
                bwdGroup.Header = BuildTransGroupHeader("↩️", "חזרה", backward.Count);
                foreach (var rule in backward)
                {
                    var toStage = def.Stages.First(s => s.Id == rule.ToStageId);
                    bwdGroup.Items.Add(new TreeViewItem
                    {
                        Tag = new MgmtTransitionNode(rule, def),
                        Header = BuildTransitionHeader(rule, toStage, false),
                    });
                }
                stageItem.Items.Add(bwdGroup);

                // 📋 Tasks group
                var taskGroup = new TreeViewItem
                {
                    Tag = new MgmtTaskGroupNode(stage, def),
                    IsExpanded = true,
                };
                taskGroup.Header = BuildTransGroupHeader("📋", "משימות", stage.StageTasks.Count);
                foreach (var stageTask in stage.StageTasks.OrderBy(st => st.SortOrder))
                {
                    totalTasks++;
                    taskGroup.Items.Add(new TreeViewItem
                    {
                        Tag = new MgmtStageTaskNode(stageTask, stage),
                        Header = BuildTaskHeader(stageTask),
                    });
                }
                stageItem.Items.Add(taskGroup);

                defItem.Items.Add(stageItem);
            }

            WorkflowTree.Items.Add(defItem);
        }

        var totalTransitions = definitions.Sum(d => d.TransitionRules.Count);
        StatusText.Text = $"{definitions.Count} תהליכים, {definitions.Sum(d => d.Stages.Count)} שלבים, {totalTransitions} מעברים, {totalTasks} משימות";
    }

    // ─── Tree headers ───

    private static StackPanel BuildWorkflowHeader(WorkflowDefinition def)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = def.IsActive ? "📂 " : "📂 ⏸️ ",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = def.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = def.IsActive ? Brushes.Black : Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"  [{def.Stages.Count} שלבים, {def.TransitionRules.Count} מעברים]",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    private static StackPanel BuildStageHeader(WorkflowStageDefinition stage, WorkflowDefinition def)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };

        string icon = stage.IsInitial ? "🟢" : stage.IsFinal ? "🔴" : "🔵";
        sp.Children.Add(new TextBlock
        {
            Text = $"{icon} ",
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{stage.SortOrder}. {stage.Name}",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var transCount = def.TransitionRules.Count(r => r.FromStageId == stage.Id);
        int taskCount = stage.StageTasks.Count;
        var parts = new List<string>();
        if (transCount > 0) parts.Add($"{transCount} מעברים");
        if (taskCount > 0) parts.Add($"{taskCount} משימות");
        if (parts.Count > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"  ({string.Join(", ", parts)})",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return sp;
    }

    private static StackPanel BuildTaskHeader(WorkflowStageTask stageTask)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = stageTask.IsRequired ? "📌 " : "📋 ",
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = stageTask.TaskType?.Name ?? "(ללא סוג)",
            VerticalAlignment = VerticalAlignment.Center,
        });

        var assigneeName = stageTask.DefaultAssignee?.Name;
        sp.Children.Add(new TextBlock
        {
            Text = assigneeName != null ? $"  → 👤 {assigneeName}" : "  → ❓ לא שוייך",
            Foreground = assigneeName != null ? Brushes.DarkGreen : Brushes.OrangeRed,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return sp;
    }

    private static StackPanel BuildTransGroupHeader(string icon, string label, int count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = $"{icon} ",
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{label} ({count})",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = count > 0 ? Brushes.DarkSlateGray : Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    private static StackPanel BuildTransitionHeader(
        WorkflowTransitionRule rule, WorkflowStageDefinition toStage, bool isForward)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = isForward ? "→ " : "← ",
            Foreground = isForward ? Brushes.DarkGreen : Brushes.DarkOrange,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{toStage.SortOrder}. {toStage.Name}",
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(rule.Name))
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"  ({rule.Name})",
                Foreground = Brushes.Gray,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return sp;
    }

    // ─── Tree selection ───

    private void WorkflowTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        BuilderDetailPanel.Children.Clear();

        if (e.NewValue is TreeViewItem { Tag: MgmtWorkflowNode wn })
            ShowWorkflowDetail(wn.Definition);
        else if (e.NewValue is TreeViewItem { Tag: MgmtStageNode sn })
            ShowStageDetail(sn.Stage, sn.Definition);
        else if (e.NewValue is TreeViewItem { Tag: MgmtTransGroupNode tgn })
            ShowTransGroupDetail(tgn.Stage, tgn.Definition, tgn.IsForward);
        else if (e.NewValue is TreeViewItem { Tag: MgmtTransitionNode trn })
            ShowTransitionDetail(trn.Rule, trn.Definition);
        else if (e.NewValue is TreeViewItem { Tag: MgmtTaskGroupNode tkn })
            ShowTaskGroupDetail(tkn.Stage, tkn.Definition);
        else if (e.NewValue is TreeViewItem { Tag: MgmtStageTaskNode stn })
            ShowTaskDetail(stn.StageTask, stn.Stage);
    }

    private void ShowWorkflowDetail(WorkflowDefinition def)
    {
        BuilderDetailHeader.Text = $"📂 תהליך: {def.Name}";
        var panel = BuilderDetailPanel;

        // ── Editable fields ──
        AddLabel(panel, "שם:", "", isBold: true);
        var nameBox = new TextBox { Text = def.Name, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(nameBox);

        var nameDupWarning = new TextBlock
        {
            Text = "⚠ שם זה כבר קיים — נא לבחור שם אחר",
            Foreground = Brushes.Red,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(nameDupWarning);

        AddLabel(panel, "תיאור:", "", isBold: true);
        var descBox = new TextBox
        {
            Text = def.Description ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 50,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(descBox);

        var isActiveCb = new CheckBox
        {
            Content = "תהליך פעיל",
            IsChecked = def.IsActive,
            Margin = new Thickness(0, 8, 0, 0),
        };
        panel.Children.Add(isActiveCb);

        AddLabel(panel, "שלבים:", def.Stages.Count.ToString());
        AddLabel(panel, "מעברים:", def.TransitionRules.Count.ToString());

        // ── Save / Add Stage / Remove buttons ──
        var actionPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };

        var saveBtn = new Button
        {
            Content = "💾 שמור שינויים",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
        };
        saveBtn.Click += (_, _) => SaveWorkflow(def.Id, nameBox.Text.Trim(),
            descBox.Text.Trim(), isActiveCb.IsChecked == true);
        actionPanel.Children.Add(saveBtn);

        // ── Real-time duplicate name check ──
        var defaultBorder = nameBox.BorderBrush;
        nameBox.TextChanged += (_, _) =>
        {
            var text = nameBox.Text.Trim();
            bool isDuplicate = false;
            if (!string.IsNullOrWhiteSpace(text) && text != def.Name)
            {
                var factory = GetFactory();
                if (factory != null)
                {
                    using var db = factory.CreateDbContext();
                    isDuplicate = db.WorkflowDefinitions
                        .Any(d => d.Id != def.Id && d.Name == text);
                }
            }
            nameDupWarning.Visibility = isDuplicate ? Visibility.Visible : Visibility.Collapsed;
            nameBox.BorderBrush = isDuplicate ? Brushes.Red : defaultBorder;
            saveBtn.IsEnabled = !isDuplicate;
        };

        var addStageBtn = new Button
        {
            Content = "➕ הוסף שלב חדש",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
        };
        addStageBtn.Click += (_, _) => AddStage(def.Id);
        actionPanel.Children.Add(addStageBtn);

        var removeBtn = new Button
        {
            Content = "🗑 מחק תהליך",
            Padding = new Thickness(10, 4, 10, 4),
            Foreground = Brushes.DarkRed,
        };
        removeBtn.Click += (_, _) =>
        {
            if (MessageBox.Show($"למחוק את התהליך '{def.Name}' וכל השלבים והמעברים שלו?",
                "אישור מחיקה", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                RemoveWorkflow(def.Id);
            }
        };
        actionPanel.Children.Add(removeBtn);

        panel.Children.Add(actionPanel);
    }

    private void ShowStageDetail(WorkflowStageDefinition stage, WorkflowDefinition def)
    {
        BuilderDetailHeader.Text = $"🔵 שלב: {stage.Name}";
        var panel = BuilderDetailPanel;

        // ── Editable fields ──
        AddLabel(panel, "שם:", "", isBold: true);
        var nameBox = new TextBox { Text = stage.Name, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(nameBox);

        var nameDupWarning = new TextBlock
        {
            Text = "⚠ שם שלב זה כבר קיים בתהליך הזה — נא לבחור שם אחר",
            Foreground = Brushes.Red,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(nameDupWarning);

        AddLabel(panel, "תיאור:", "", isBold: true);
        var descBox = new TextBox
        {
            Text = stage.Description ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 50,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(descBox);

        AddLabel(panel, "סדר:", stage.SortOrder.ToString());

        // IsInitial/IsFinal are auto-derived from position (first=Initial, last=Final)
        var roleText = stage.IsInitial ? "🟢 שלב התחלתי (אוטומטי — ראשון בסדר)"
                     : stage.IsFinal  ? "🔴 שלב סופי (אוטומטי — אחרון בסדר)"
                     : "🔵 שלב ביניים";
        AddLabel(panel, "תפקיד:", roleText);

        AddLabel(panel, "משימות:", stage.StageTasks.Count.ToString());

        // ── Save / Reorder / Remove buttons ──
        var actionPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };

        var saveBtn = new Button
        {
            Content = "💾 שמור שינויים",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
        };
        saveBtn.Click += (_, _) => SaveStage(stage.Id, nameBox.Text.Trim(),
            descBox.Text.Trim());
        actionPanel.Children.Add(saveBtn);

        // ── Real-time duplicate name check ──
        var defaultBorder = nameBox.BorderBrush;
        nameBox.TextChanged += (_, _) =>
        {
            var text = nameBox.Text.Trim();
            bool isDuplicate = false;
            if (!string.IsNullOrWhiteSpace(text) && text != stage.Name)
            {
                var factory = GetFactory();
                if (factory != null)
                {
                    using var db = factory.CreateDbContext();
                    isDuplicate = db.WorkflowStageDefinitions
                        .Any(s => s.Id != stage.Id
                               && s.WorkflowDefinitionId == stage.WorkflowDefinitionId
                               && s.Name == text);
                }
            }
            nameDupWarning.Visibility = isDuplicate ? Visibility.Visible : Visibility.Collapsed;
            nameBox.BorderBrush = isDuplicate ? Brushes.Red : defaultBorder;
            saveBtn.IsEnabled = !isDuplicate;
        };

        var moveUpBtn = new Button
        {
            Content = "⬆ הזז למעלה",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        moveUpBtn.Click += (_, _) => MoveStage(stage.Id, def.Id, -1);
        actionPanel.Children.Add(moveUpBtn);

        var moveDownBtn = new Button
        {
            Content = "⬇ הזז למטה",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        moveDownBtn.Click += (_, _) => MoveStage(stage.Id, def.Id, +1);
        actionPanel.Children.Add(moveDownBtn);

        var removeBtn = new Button
        {
            Content = "🗑 מחק שלב",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
            Foreground = Brushes.DarkRed,
        };
        removeBtn.Click += (_, _) =>
        {
            if (MessageBox.Show($"למחוק את השלב '{stage.Name}' וכל המשימות והמעברים הקשורים?",
                "אישור מחיקה", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                RemoveStage(stage.Id);
            }
        };
        actionPanel.Children.Add(removeBtn);

        panel.Children.Add(actionPanel);
    }

    private void ShowTaskDetail(WorkflowStageTask stageTask, WorkflowStageDefinition stage)
    {
        BuilderDetailHeader.Text = $"📋 משימה: {stageTask.TaskType?.Name ?? "—"}";
        var panel = BuilderDetailPanel;

        AddLabel(panel, "שלב:", stage.Name);
        AddLabel(panel, "סוג משימה:", stageTask.TaskType?.Name ?? "—");
        AddLabel(panel, "סדר:", stageTask.SortOrder.ToString());
        AddLabel(panel, "חובה:", stageTask.IsRequired ? "✅ כן" : "❌ לא");
        AddLabel(panel, "הערות:", stageTask.Notes ?? "—");

        AddSeparator(panel);

        AddLabel(panel, "שיוך עובד ברירת מחדל:", "", isBold: true);

        var assigneeCombo = new ComboBox
        {
            ItemsSource = _builderEmployees,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 4, 0, 0),
        };

        if (stageTask.DefaultAssigneeId.HasValue)
        {
            var current = _builderEmployees.FirstOrDefault(e => e.Id == stageTask.DefaultAssigneeId.Value);
            if (current != null) assigneeCombo.SelectedItem = current;
        }

        panel.Children.Add(assigneeCombo);

        var requiredCb = new CheckBox
        {
            Content = "משימה חובה (נדרש השלמה לפני קידום שלב)",
            IsChecked = stageTask.IsRequired,
            Margin = new Thickness(0, 8, 0, 0),
        };
        panel.Children.Add(requiredCb);

        AddLabel(panel, "הערות:", "", isBold: true);
        var notesBox = new TextBox
        {
            Text = stageTask.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 50,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(notesBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var saveBtn = new Button
        {
            Content = "💾 שמור שינויים",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
        };
        saveBtn.Click += (_, _) =>
        {
            var selectedEmployee = assigneeCombo.SelectedItem as Siuser;
            SaveStageTask(stageTask.Id, selectedEmployee?.Id, requiredCb.IsChecked == true, notesBox.Text);
        };
        btnPanel.Children.Add(saveBtn);

        var deleteBtn = new Button
        {
            Content = "🗑 הסר משימה",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = Brushes.DarkRed,
        };
        deleteBtn.Click += (_, _) =>
        {
            if (MessageBox.Show($"להסיר את המשימה '{stageTask.TaskType?.Name}' מהשלב?",
                "אישור", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                RemoveStageTask(stageTask.Id);
            }
        };
        btnPanel.Children.Add(deleteBtn);

        var moveUpBtn = new Button
        {
            Content = "⬆",
            ToolTip = "הזז למעלה",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 4, 0),
        };
        moveUpBtn.Click += (_, _) => MoveTask(stageTask.Id, stage.Id, -1);
        btnPanel.Children.Add(moveUpBtn);

        var moveDownBtn = new Button
        {
            Content = "⬇",
            ToolTip = "הזז למטה",
            Padding = new Thickness(8, 4, 8, 4),
        };
        moveDownBtn.Click += (_, _) => MoveTask(stageTask.Id, stage.Id, +1);
        btnPanel.Children.Add(moveDownBtn);

        panel.Children.Add(btnPanel);
    }

    private void ShowTransGroupDetail(
        WorkflowStageDefinition stage, WorkflowDefinition def, bool isForward)
    {
        var dirLabel = isForward ? "קדימה" : "חזרה";
        var dirIcon = isForward ? "➡️" : "↩️";
        BuilderDetailHeader.Text = $"{dirIcon} מעברים {dirLabel} מ: {stage.SortOrder}. {stage.Name}";
        var panel = BuilderDetailPanel;

        var outgoing = def.TransitionRules
            .Where(r => r.FromStageId == stage.Id)
            .ToList();

        var filtered = outgoing
            .Where(r => def.Stages.FirstOrDefault(s => s.Id == r.ToStageId) is { } to
                     && (isForward ? to.SortOrder > stage.SortOrder : to.SortOrder < stage.SortOrder))
            .ToList();

        if (filtered.Count == 0)
        {
            AddLabel(panel, $"אין מעברים {dirLabel} מוגדרים מהשלב הזה.", "");
        }
        else
        {
            foreach (var rule in filtered)
            {
                var toStage = def.Stages.First(s => s.Id == rule.ToStageId);

                var ruleBorder = new Border
                {
                    Background = new SolidColorBrush(isForward
                        ? Color.FromRgb(0xE8, 0xF5, 0xE9)
                        : Color.FromRgb(0xFF, 0xF3, 0xE0)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 4, 0, 0),
                };

                var ruleGrid = new Grid();
                ruleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ruleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var ruleInfo = new StackPanel();
                ruleInfo.Children.Add(new TextBlock
                {
                    Text = $"{stage.Name}  →  {toStage.SortOrder}. {toStage.Name}",
                    FontWeight = FontWeights.SemiBold,
                });
                if (!string.IsNullOrEmpty(rule.Name))
                {
                    ruleInfo.Children.Add(new TextBlock
                    {
                        Text = $"תנאי / תיאור: {rule.Name}",
                        Foreground = Brushes.Gray,
                        FontSize = 11,
                        FontStyle = FontStyles.Italic,
                    });
                }
                Grid.SetColumn(ruleInfo, 0);
                ruleGrid.Children.Add(ruleInfo);

                var capturedRuleId = rule.Id;
                var removeRuleBtn = new Button
                {
                    Content = "🗑",
                    ToolTip = "הסר מעבר",
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                };
                removeRuleBtn.Click += (_, _) =>
                {
                    if (MessageBox.Show("להסיר מעבר זה?", "אישור",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        RemoveTransitionRule(capturedRuleId);
                    }
                };
                Grid.SetColumn(removeRuleBtn, 1);
                ruleGrid.Children.Add(removeRuleBtn);

                ruleBorder.Child = ruleGrid;
                panel.Children.Add(ruleBorder);
            }
        }

        // ── Add transition form ──
        AddSeparator(panel);
        AddLabel(panel, $"➕ הוסף מעבר {dirLabel}:", "", isBold: true);

        var targetStages = def.Stages
            .Where(s => isForward ? s.SortOrder > stage.SortOrder : s.SortOrder < stage.SortOrder)
            .OrderBy(s => s.SortOrder)
            .ToList();

        if (targetStages.Count == 0)
        {
            AddLabel(panel, $"אין שלבים זמינים בכיוון {dirLabel}.", "");
            return;
        }

        var addGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toCombo = new ComboBox
        {
            ItemsSource = targetStages,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 4, 0),
        };
        Grid.SetColumn(toCombo, 0);
        addGrid.Children.Add(toCombo);

        var nameBox = new TextBox
        {
            Text = "",
            Margin = new Thickness(0, 0, 4, 0),
        };
        Grid.SetColumn(nameBox, 1);
        addGrid.Children.Add(nameBox);

        AddLabel(panel, "שלב יעד:", "");
        panel.Children.Add(addGrid);

        // Label hint
        AddLabel(panel, "", "תנאי / תיאור (אופציונלי) — לתיאור הסיבה למעבר");

        var addBtn = new Button
        {
            Content = "➕ הוסף מעבר",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 6, 0, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(isForward
                ? Color.FromRgb(0xC8, 0xE6, 0xC9)
                : Color.FromRgb(0xFF, 0xE0, 0xB2)),
        };
        addBtn.Click += (_, _) =>
        {
            if (toCombo.SelectedValue is not int toId)
            {
                MessageBox.Show("נא לבחור שלב יעד.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AddTransitionRule(def.Id, stage.Id, toId, nameBox.Text.Trim());
        };
        panel.Children.Add(addBtn);
    }

    private void ShowTransitionDetail(WorkflowTransitionRule rule, WorkflowDefinition def)
    {
        var fromStage = def.Stages.FirstOrDefault(s => s.Id == rule.FromStageId);
        var toStage = def.Stages.FirstOrDefault(s => s.Id == rule.ToStageId);
        var fromName = fromStage?.Name ?? "?";
        var toName = toStage?.Name ?? "?";
        bool isForward = fromStage != null && toStage != null && toStage.SortOrder > fromStage.SortOrder;
        var dirIcon = isForward ? "→" : "←";

        BuilderDetailHeader.Text = $"{dirIcon} מעבר: {fromName} → {toName}";
        var panel = BuilderDetailPanel;

        AddLabel(panel, "משלב:", $"{fromStage?.SortOrder}. {fromName}");
        AddLabel(panel, "לשלב:", $"{toStage?.SortOrder}. {toName}");
        AddLabel(panel, "כיוון:", isForward ? "➡️ קדימה" : "↩️ חזרה");

        AddSeparator(panel);

        AddLabel(panel, "תנאי / תיאור:", "", isBold: true);
        var nameBox = new TextBox
        {
            Text = rule.Name ?? "",
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(nameBox);
        AddLabel(panel, "", "תיאור הסיבה או התנאי למעבר זה (למשל: \"החזרה לתיקונים\")");

        var btnPanel = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };

        var saveBtn = new Button
        {
            Content = "💾 שמור",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
        };
        saveBtn.Click += (_, _) => SaveTransitionRule(rule.Id, nameBox.Text.Trim());
        btnPanel.Children.Add(saveBtn);

        var removeBtn = new Button
        {
            Content = "🗑 הסר מעבר",
            Padding = new Thickness(10, 4, 10, 4),
            Foreground = Brushes.DarkRed,
        };
        removeBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("להסיר מעבר זה?", "אישור",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                RemoveTransitionRule(rule.Id);
            }
        };
        btnPanel.Children.Add(removeBtn);

        panel.Children.Add(btnPanel);
    }

    private void ShowTaskGroupDetail(WorkflowStageDefinition stage, WorkflowDefinition def)
    {
        BuilderDetailHeader.Text = $"📋 משימות בשלב: {stage.SortOrder}. {stage.Name}";
        var panel = BuilderDetailPanel;

        if (stage.StageTasks.Count == 0)
        {
            AddLabel(panel, "אין משימות מוגדרות בשלב זה.", "");
        }
        else
        {
            AddLabel(panel, $"משימות ({stage.StageTasks.Count}):", "", isBold: true);
            foreach (var stageTask in stage.StageTasks.OrderBy(st => st.SortOrder))
            {
                var taskBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 2, 0, 0),
                };
                var taskText = new TextBlock
                {
                    Text = $"{(stageTask.IsRequired ? "📌" : "📋")} {stageTask.TaskType?.Name ?? "—"}" +
                           $"  →  {(stageTask.DefaultAssignee?.Name is { } n ? $"👤 {n}" : "❓ לא שוייך")}",
                };
                taskBorder.Child = taskText;
                panel.Children.Add(taskBorder);
            }
        }

        // ── Add task form ──
        AddSeparator(panel);
        AddLabel(panel, "הוסף משימה לשלב:", "", isBold: true);

        var addGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var taskTypeCombo = new ComboBox
        {
            ItemsSource = _builderTaskTypes,
            DisplayMemberPath = "Name",
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(taskTypeCombo, 0);
        addGrid.Children.Add(taskTypeCombo);

        var addBtn = new Button
        {
            Content = "➕ הוסף",
            Padding = new Thickness(10, 4, 10, 4),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
        };
        addBtn.Click += (_, _) =>
        {
            if (taskTypeCombo.SelectedItem is not TaskType selectedType)
            {
                MessageBox.Show("יש לבחור סוג משימה.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AddTaskToStage(stage.Id, selectedType.Id);
        };
        Grid.SetColumn(addBtn, 1);
        addGrid.Children.Add(addBtn);

        panel.Children.Add(addGrid);
    }

    // ─── Builder CRUD ───

    private void BuilderAddWorkflow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var code = $"WF_{DateTime.UtcNow:yyyyMMddHHmmss}";

            var entity = new WorkflowDefinition
            {
                Code = code,
                Name = "תהליך חדש",
                Description = null,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };

            db.WorkflowDefinitions.Add(entity);
            db.SaveChanges();

            BuildTree();
            StatusText.Text = "✅ תהליך חדש נוצר — יש לערוך את הפרטים";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה ביצירת תהליך: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveWorkflow(int defId, string name, string description, bool isActive)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("שם תהליך לא יכול להיות ריק.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            // Unique name check (global)
            var duplicate = db.WorkflowDefinitions
                .Any(d => d.Id != defId && d.Name == name);
            if (duplicate)
            {
                MessageBox.Show($"תהליך בשם '{name}' כבר קיים. נא לבחור שם אחר.", "שם כפול",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var entity = db.WorkflowDefinitions.Find(defId);
            if (entity == null) return;

            entity.Name = name;
            entity.Code = GenerateCode(name, entity.Code);
            entity.Description = string.IsNullOrWhiteSpace(description) ? null : description;
            entity.IsActive = isActive;
            entity.ModifiedAtUtc = DateTime.UtcNow;

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ תהליך נשמר";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת תהליך: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveWorkflow(int defId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowDefinitions
                .Include(d => d.Stages)
                    .ThenInclude(s => s.StageTasks)
                .Include(d => d.TransitionRules)
                .Include(d => d.AllowedForProjectTypes)
                .FirstOrDefault(d => d.Id == defId);

            if (entity == null) return;

            // Check for active instances
            var hasInstances = db.WorkflowInstances.Any(i => i.WorkflowDefinitionId == defId);
            if (hasInstances)
            {
                MessageBox.Show("לא ניתן למחוק תהליך שיש לו מופעים פעילים. ניתן לבטל את הפעלתו במקום.",
                    "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Cascade: tasks → stages → transitions → policy mappings → definition
            foreach (var stage in entity.Stages)
                db.WorkflowStageTasks.RemoveRange(stage.StageTasks);

            db.WorkflowStageDefinitions.RemoveRange(entity.Stages);
            db.WorkflowTransitionRules.RemoveRange(entity.TransitionRules);
            db.Set<ProjectTypeWorkflowDefinition>().RemoveRange(entity.AllowedForProjectTypes);
            db.WorkflowDefinitions.Remove(entity);

            db.SaveChanges();
            BuildTree();
            BuilderDetailPanel.Children.Clear();
            BuilderDetailHeader.Text = "בחר פריט מהעץ...";
            StatusText.Text = "✅ תהליך נמחק";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה במחיקת תהליך: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Auto-sets IsInitial on the first stage (min SortOrder) and IsFinal on the last stage (max SortOrder).
    /// All other stages get both flags cleared. Call after any SortOrder change (add/remove/move).
    /// Must be called within an open DbContext — caller is responsible for SaveChanges().
    /// </summary>
    private static void AutoSetInitialFinalFlags(SiNetSQLDbContext db, int defId)
    {
        var stages = db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == defId)
            .OrderBy(s => s.SortOrder)
            .ToList();

        for (int i = 0; i < stages.Count; i++)
        {
            stages[i].IsInitial = i == 0;
            stages[i].IsFinal = i == stages.Count - 1;
        }
    }

    /// <summary>
    /// Generates a machine-readable Code from a display Name.
    /// Latin characters → PascalCase slug. Hebrew/non-Latin → keeps the existing code unchanged.
    /// </summary>
    private static string GenerateCode(string name, string existingCode)
    {
        // Extract only Latin letters, digits, and spaces
        var latinChars = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ').ToArray()).Trim();

        if (latinChars.Length == 0)
            return existingCode; // Hebrew-only name — keep existing auto-generated code

        // PascalCase: "Material Intake" → "MaterialIntake"
        return string.Concat(latinChars
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private void AddStage(int defId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var maxSort = db.WorkflowStageDefinitions
                .Where(s => s.WorkflowDefinitionId == defId)
                .Select(s => (int?)s.SortOrder)
                .Max() ?? 0;

            var code = $"Stage_{maxSort + 1}";

            db.WorkflowStageDefinitions.Add(new WorkflowStageDefinition
            {
                WorkflowDefinitionId = defId,
                Code = code,
                Name = "שלב חדש",
                SortOrder = maxSort + 1,
            });

            db.SaveChanges();
            AutoSetInitialFinalFlags(db, defId);
            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ שלב חדש נוסף";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהוספת שלב: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveStage(int stageId, string name, string description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("שם שלב לא יכול להיות ריק.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowStageDefinitions.Find(stageId);
            if (entity == null) return;

            // Unique name check (within same workflow definition)
            var duplicate = db.WorkflowStageDefinitions
                .Any(s => s.Id != stageId
                       && s.WorkflowDefinitionId == entity.WorkflowDefinitionId
                       && s.Name == name);
            if (duplicate)
            {
                MessageBox.Show($"שלב בשם '{name}' כבר קיים בתהליך הזה. נא לבחור שם אחר.", "שם כפול",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            entity.Name = name;
            entity.Code = GenerateCode(name, entity.Code);
            entity.Description = string.IsNullOrWhiteSpace(description) ? null : description;
            // IsInitial/IsFinal are auto-derived from position — not set here

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ שלב נשמר";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת שלב: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveStage(int stageId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowStageDefinitions
                .Include(s => s.StageTasks)
                .Include(s => s.TransitionRulesFrom)
                .Include(s => s.TransitionRulesTo)
                .FirstOrDefault(s => s.Id == stageId);

            if (entity == null) return;

            db.WorkflowStageTasks.RemoveRange(entity.StageTasks);
            db.WorkflowTransitionRules.RemoveRange(entity.TransitionRulesFrom);
            db.WorkflowTransitionRules.RemoveRange(entity.TransitionRulesTo);
            db.WorkflowStageDefinitions.Remove(entity);

            // Re-number remaining stages
            var remaining = db.WorkflowStageDefinitions
                .Where(s => s.WorkflowDefinitionId == entity.WorkflowDefinitionId && s.Id != stageId)
                .OrderBy(s => s.SortOrder)
                .ToList();

            for (int i = 0; i < remaining.Count; i++)
                remaining[i].SortOrder = i + 1;

            AutoSetInitialFinalFlags(db, entity.WorkflowDefinitionId);
            db.SaveChanges();
            BuildTree();
            BuilderDetailPanel.Children.Clear();
            BuilderDetailHeader.Text = "בחר פריט מהעץ...";
            StatusText.Text = "✅ שלב נמחק";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה במחיקת שלב: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MoveStage(int stageId, int defId, int direction)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var stages = db.WorkflowStageDefinitions
                .Where(s => s.WorkflowDefinitionId == defId)
                .OrderBy(s => s.SortOrder)
                .ToList();

            var idx = stages.FindIndex(s => s.Id == stageId);
            if (idx < 0) return;

            var targetIdx = idx + direction;
            if (targetIdx < 0 || targetIdx >= stages.Count) return;

            // Swap sort orders
            (stages[idx].SortOrder, stages[targetIdx].SortOrder) =
                (stages[targetIdx].SortOrder, stages[idx].SortOrder);

            AutoSetInitialFinalFlags(db, defId);
            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ סדר שלבים עודכן";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשינוי סדר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MoveTask(int stageTaskId, int stageId, int direction)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var tasks = db.WorkflowStageTasks
                .Where(t => t.StageDefinitionId == stageId)
                .OrderBy(t => t.SortOrder)
                .ToList();

            var idx = tasks.FindIndex(t => t.Id == stageTaskId);
            if (idx < 0) return;

            var targetIdx = idx + direction;
            if (targetIdx < 0 || targetIdx >= tasks.Count) return;

            (tasks[idx].SortOrder, tasks[targetIdx].SortOrder) =
                (tasks[targetIdx].SortOrder, tasks[idx].SortOrder);

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ סדר משימות עודכן";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשינוי סדר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddTransitionRule(int defId, int fromStageId, int toStageId, string? name)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var exists = db.WorkflowTransitionRules
                .Any(r => r.WorkflowDefinitionId == defId
                    && r.FromStageId == fromStageId
                    && r.ToStageId == toStageId);

            if (exists)
            {
                MessageBox.Show("מעבר זה כבר קיים.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            db.WorkflowTransitionRules.Add(new WorkflowTransitionRule
            {
                WorkflowDefinitionId = defId,
                FromStageId = fromStageId,
                ToStageId = toStageId,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
            });

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ מעבר נוסף";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהוספת מעבר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveTransitionRule(int ruleId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowTransitionRules.Find(ruleId);
            if (entity == null) return;

            db.WorkflowTransitionRules.Remove(entity);
            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ מעבר הוסר";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהסרת מעבר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveTransitionRule(int ruleId, string name)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowTransitionRules.Find(ruleId);
            if (entity == null) return;

            entity.Name = string.IsNullOrWhiteSpace(name) ? null : name;

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ מעבר נשמר";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת מעבר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddTaskToStage(int stageDefinitionId, int taskTypeId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var exists = db.WorkflowStageTasks
                .Any(st => st.StageDefinitionId == stageDefinitionId && st.TaskTypeId == taskTypeId);

            if (exists)
            {
                MessageBox.Show("משימה מסוג זה כבר קיימת בשלב.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var maxSort = db.WorkflowStageTasks
                .Where(st => st.StageDefinitionId == stageDefinitionId)
                .Select(st => (int?)st.SortOrder)
                .Max() ?? 0;

            db.WorkflowStageTasks.Add(new WorkflowStageTask
            {
                StageDefinitionId = stageDefinitionId,
                TaskTypeId = taskTypeId,
                SortOrder = maxSort + 1,
                IsRequired = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            });

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ משימה נוספה בהצלחה";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהוספת משימה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveStageTask(int stageTaskId, int? assigneeId, bool isRequired, string? notes)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowStageTasks.Find(stageTaskId);
            if (entity == null) return;

            entity.DefaultAssigneeId = assigneeId;
            entity.IsRequired = isRequired;
            entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ שינויים נשמרו";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveStageTask(int stageTaskId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            var entity = db.WorkflowStageTasks.Find(stageTaskId);
            if (entity == null) return;

            db.WorkflowStageTasks.Remove(entity);
            db.SaveChanges();
            BuildTree();
            StatusText.Text = "✅ משימה הוסרה";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהסרה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuilderRefresh_Click(object sender, RoutedEventArgs e) => BuildTree();

    #endregion

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  TAB 2: POLICY — ProjectType ↔ Workflow mapping                     ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    #region Policy Tab

    private void LoadPolicyTab()
    {
        _policyLoaded = true;

        try
        {
            var dbFactory = GetFactory();
            if (dbFactory is null) return;

            using var context = dbFactory.CreateDbContext();

            var jobTypes = context.JobTypes
                .AsNoTracking()
                .Where(j => j.Title != null && j.Title != "")
                .OrderBy(j => j.Title)
                .ToList();

            _policyAllDefinitions = context.WorkflowDefinitions
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .ToList();

            var mappings = context.ProjectTypeWorkflowDefinitions
                .AsNoTracking()
                .ToList();

            _policyMappingMap.Clear();
            foreach (var jt in jobTypes)
            {
                var jtMappings = mappings
                    .Where(m => m.ProjectTypeId == jt.Id)
                    .Select(m => new PolicyMappingState
                    {
                        DefinitionId = m.WorkflowDefinitionId,
                        IsEnabled = m.IsEnabled,
                        IsDefault = m.IsDefault,
                    })
                    .ToList();

                _policyMappingMap[jt.Id] = jtMappings;
            }

            _policyAllProjectTypes = new ObservableCollection<PolicyProjectTypeItem>(
                jobTypes.Select(j => new PolicyProjectTypeItem
                {
                    Id = j.Id,
                    Title = j.Title!,
                    MappingCount = _policyMappingMap.GetValueOrDefault(j.Id)?.Count(m => m.IsEnabled) ?? 0,
                }));

            ApplyPolicyProjectTypeFilter();

            var totalMappings = mappings.Count(m => m.IsEnabled);
            StatusText.Text = $"{jobTypes.Count} סוגי פרויקט, {_policyAllDefinitions.Count} תהליכים, {totalMappings} שיוכים";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת נתונים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PolicyProjectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PolicyProjectTypeListBox.SelectedItem is not PolicyProjectTypeItem item) return;

        _policySelectedProjectTypeId = item.Id;
        PolicySelectedTypeHeader.Text = $"מדיניות עבור: {item.Title}";
        RefreshPolicyWorkflowList();
        RefreshPolicyStagesList();
        RefreshPolicyDisciplinesList();
    }

    private void RefreshPolicyWorkflowList()
    {
        if (_policySelectedProjectTypeId is not { } ptId) return;

        var existing = _policyMappingMap.GetValueOrDefault(ptId) ?? [];

        var items = _policyAllDefinitions.Select(def =>
        {
            var mapping = existing.FirstOrDefault(m => m.DefinitionId == def.Id);
            return new PolicyWorkflowMappingItem
            {
                DefinitionId = def.Id,
                DefinitionName = def.Name,
                DefinitionDescription = def.Description,
                IsEnabled = mapping?.IsEnabled ?? false,
                IsDefault = mapping?.IsDefault ?? false,
            };
        }).ToList();

        PolicyWorkflowListBox.ItemsSource = items;
    }

    private void PolicySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyPolicyProjectTypeFilter();
    }

    private void ApplyPolicyProjectTypeFilter()
    {
        var search = PolicySearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            PolicyProjectTypeListBox.ItemsSource = _policyAllProjectTypes;
        }
        else
        {
            PolicyProjectTypeListBox.ItemsSource = _policyAllProjectTypes
                .Where(pt => pt.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void PolicyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_policySelectedProjectTypeId is not { } ptId) return;
        if (sender is not CheckBox { DataContext: PolicyWorkflowMappingItem item }) return;
        UpdatePolicyMapping(ptId, item);
    }

    private void PolicyDefaultToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_policySelectedProjectTypeId is not { } ptId) return;
        if (sender is not ToggleButton { DataContext: PolicyWorkflowMappingItem item }) return;
        UpdatePolicyMapping(ptId, item);
    }

    private void UpdatePolicyMapping(int projectTypeId, PolicyWorkflowMappingItem item)
    {
        if (!_policyMappingMap.TryGetValue(projectTypeId, out var list))
        {
            list = [];
            _policyMappingMap[projectTypeId] = list;
        }

        var existing = list.FirstOrDefault(m => m.DefinitionId == item.DefinitionId);
        if (existing is not null)
        {
            existing.IsEnabled = item.IsEnabled;
            existing.IsDefault = item.IsEnabled && item.IsDefault;
        }
        else if (item.IsEnabled)
        {
            list.Add(new PolicyMappingState
            {
                DefinitionId = item.DefinitionId,
                IsEnabled = true,
                IsDefault = item.IsDefault,
            });
        }

        if (!item.IsEnabled)
            item.IsDefault = false;

        var ptItem = _policyAllProjectTypes.FirstOrDefault(p => p.Id == projectTypeId);
        if (ptItem is not null)
            ptItem.MappingCount = list.Count(m => m.IsEnabled);
    }

    private void PolicySave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dbFactory = GetFactory();
            if (dbFactory is null) return;

            using var context = dbFactory.CreateDbContext();

            var existing = context.ProjectTypeWorkflowDefinitions.ToList();
            var existingLookup = existing.ToLookup(m => m.ProjectTypeId);

            int added = 0, updated = 0, removed = 0;

            foreach (var (projectTypeId, desiredMappings) in _policyMappingMap)
            {
                var currentRows = existingLookup[projectTypeId].ToList();

                foreach (var desired in desiredMappings.Where(m => m.IsEnabled))
                {
                    var row = currentRows.FirstOrDefault(r => r.WorkflowDefinitionId == desired.DefinitionId);
                    if (row is not null)
                    {
                        if (row.IsEnabled != desired.IsEnabled || row.IsDefault != desired.IsDefault)
                        {
                            row.IsEnabled = desired.IsEnabled;
                            row.IsDefault = desired.IsDefault;
                            updated++;
                        }
                    }
                    else
                    {
                        context.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
                        {
                            ProjectTypeId = projectTypeId,
                            WorkflowDefinitionId = desired.DefinitionId,
                            IsEnabled = true,
                            IsDefault = desired.IsDefault,
                            SortOrder = 0,
                        });
                        added++;
                    }
                }

                var desiredDefIds = desiredMappings
                    .Where(m => m.IsEnabled)
                    .Select(m => m.DefinitionId)
                    .ToHashSet();

                foreach (var row in currentRows.Where(r => !desiredDefIds.Contains(r.WorkflowDefinitionId)))
                {
                    context.ProjectTypeWorkflowDefinitions.Remove(row);
                    removed++;
                }
            }

            context.SaveChanges();

            StatusText.Text = $"✅ נשמר — {added} נוספו, {updated} עודכנו, {removed} הוסרו";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class PolicyMappingState
    {
        public int DefinitionId { get; init; }
        public bool IsEnabled { get; set; }
        public bool IsDefault { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Policy → Stages / Disciplines sub-tabs
    // ═══════════════════════════════════════════════════════════════════════

    private void PolicySubTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != PolicySubTabs) return;

        // Lazy-load reference data on first visit to each sub-tab
        if (PolicySubTabs.SelectedIndex == 1 && !_policyStagesLoaded)
        {
            EnsurePolicyStagesReferenceLoaded();
            RefreshPolicyStagesList();
        }
        else if (PolicySubTabs.SelectedIndex == 2 && !_policyDisciplinesLoaded)
        {
            EnsurePolicyDisciplinesReferenceLoaded();
            RefreshPolicyDisciplinesList();
        }
    }

    private void EnsurePolicyStagesReferenceLoaded()
    {
        if (_policyStagesLoaded) return;

        var dbFactory = GetFactory();
        if (dbFactory is null) return;

        try
        {
            using var context = dbFactory.CreateDbContext();

            // Load PlanningWorkflow stages (canonical workflow only). If not present,
            // fall back to all active stages.
            var planning = context.WorkflowDefinitions
                .AsNoTracking()
                .FirstOrDefault(d => d.Code == "PlanningWorkflow" && d.IsActive);

            var stagesQuery = context.WorkflowStageDefinitions.AsNoTracking();
            if (planning is not null)
                stagesQuery = stagesQuery.Where(s => s.WorkflowDefinitionId == planning.Id);

            _policyAllStages = stagesQuery.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
            _policyStagesLoaded = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת שלבים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnsurePolicyDisciplinesReferenceLoaded()
    {
        if (_policyDisciplinesLoaded) return;

        var dbFactory = GetFactory();
        if (dbFactory is null) return;

        try
        {
            using var context = dbFactory.CreateDbContext();

            _policyAllDisciplineTaskTypes = context.TaskTypes
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                .ToList();

            _policyDisciplinesLoaded = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת תחומים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshPolicyStagesList()
    {
        if (_policySelectedProjectTypeId is not { } ptId)
        {
            PolicyStagesListBox.ItemsSource = null;
            return;
        }

        if (!_policyStagesLoaded)
        {
            EnsurePolicyStagesReferenceLoaded();
            if (!_policyStagesLoaded) return;
        }

        if (!_policyStagesMap.TryGetValue(ptId, out var items))
        {
            items = LoadPolicyStagesForProjectType(ptId);
            _policyStagesMap[ptId] = items;
        }

        PolicyStagesListBox.ItemsSource = items;
    }

    private ObservableCollection<PolicyStageItem> LoadPolicyStagesForProjectType(int projectTypeId)
    {
        var dbFactory = GetFactory();
        if (dbFactory is null) return [];

        using var context = dbFactory.CreateDbContext();

        var existing = context.ProjectTypeWorkflowStages
            .AsNoTracking()
            .Where(m => m.ProjectTypeId == projectTypeId)
            .ToList();

        var items = _policyAllStages.Select(s =>
        {
            var mapping = existing.FirstOrDefault(m => m.WorkflowStageDefinitionId == s.Id);
            return new PolicyStageItem
            {
                MappingId = mapping?.Id,
                StageDefinitionId = s.Id,
                StageName = s.Name,
                StageCode = s.Code,
                SortOrder = s.SortOrder,
                IsActive = mapping?.IsActive ?? true,
                IsRequired = mapping?.IsRequired ?? true,
            };
        }).ToList();

        return new ObservableCollection<PolicyStageItem>(items);
    }

    private void RefreshPolicyDisciplinesList()
    {
        if (_policySelectedProjectTypeId is not { } ptId)
        {
            PolicyDisciplinesListBox.ItemsSource = null;
            return;
        }

        if (!_policyDisciplinesLoaded)
        {
            EnsurePolicyDisciplinesReferenceLoaded();
            if (!_policyDisciplinesLoaded) return;
        }

        if (!_policyDisciplinesMap.TryGetValue(ptId, out var items))
        {
            items = LoadPolicyDisciplinesForProjectType(ptId);
            _policyDisciplinesMap[ptId] = items;
        }

        PolicyDisciplinesListBox.ItemsSource = items;
    }

    private ObservableCollection<PolicyDisciplineItem> LoadPolicyDisciplinesForProjectType(int projectTypeId)
    {
        var dbFactory = GetFactory();
        if (dbFactory is null) return [];

        using var context = dbFactory.CreateDbContext();

        var existing = context.ProjectTypeDisciplines
            .AsNoTracking()
            .Where(m => m.ProjectTypeId == projectTypeId)
            .ToList();

        var items = _policyAllDisciplineTaskTypes.Select(t =>
        {
            var mapping = existing.FirstOrDefault(m => m.DisciplineTaskTypeId == t.Id);
            return new PolicyDisciplineItem
            {
                MappingId = mapping?.Id,
                DisciplineTaskTypeId = t.Id,
                DisciplineName = t.Name,
                DisciplineCode = t.Code,
                IsActive = mapping?.IsActive ?? false,
                IsRequired = mapping?.IsRequired ?? false,
            };
        }).ToList();

        return new ObservableCollection<PolicyDisciplineItem>(items);
    }

    private void PolicyStageActive_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PolicyStageItem item })
        {
            item.IsDirty = true;
            if (!item.IsActive) item.IsRequired = false;
        }
    }

    private void PolicyStageRequired_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PolicyStageItem item })
            item.IsDirty = true;
    }

    private void PolicyDisciplineActive_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PolicyDisciplineItem item })
        {
            item.IsDirty = true;
            if (!item.IsActive) item.IsRequired = false;
        }
    }

    private void PolicyDisciplineRequired_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PolicyDisciplineItem item })
            item.IsDirty = true;
    }

    private void PolicyStagesSave_Click(object sender, RoutedEventArgs e)
    {
        if (_policySelectedProjectTypeId is not { } ptId)
        {
            MessageBox.Show("בחר סוג פרויקט תחילה.", "מידע",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_policyStagesMap.TryGetValue(ptId, out var items)) return;

        try
        {
            var dbFactory = GetFactory();
            if (dbFactory is null) return;

            using var context = dbFactory.CreateDbContext();

            int added = 0, updated = 0;

            foreach (var item in items)
            {
                if (item.MappingId is { } id)
                {
                    var row = context.ProjectTypeWorkflowStages.FirstOrDefault(m => m.Id == id);
                    if (row is null) continue;
                    if (row.IsActive != item.IsActive || row.IsRequired != item.IsRequired)
                    {
                        row.IsActive = item.IsActive;
                        row.IsRequired = item.IsActive && item.IsRequired;
                        updated++;
                    }
                }
                else
                {
                    // Persist a row only if user changed defaults (active+required default = true).
                    if (!item.IsDirty && item.IsActive && item.IsRequired)
                        continue;

                    var row = new ProjectTypeWorkflowStage
                    {
                        ProjectTypeId = ptId,
                        WorkflowStageDefinitionId = item.StageDefinitionId,
                        IsActive = item.IsActive,
                        IsRequired = item.IsActive && item.IsRequired,
                        SortOrder = item.SortOrder,
                        CanRepeat = false,
                    };
                    context.ProjectTypeWorkflowStages.Add(row);
                    added++;
                }
            }

            context.SaveChanges();

            // Refresh mapping ids after save so next save updates instead of inserts
            _policyStagesMap.Remove(ptId);
            RefreshPolicyStagesList();

            StatusText.Text = $"✅ שלבים נשמרו — {added} נוספו, {updated} עודכנו";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת שלבים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PolicyDisciplinesSave_Click(object sender, RoutedEventArgs e)
    {
        if (_policySelectedProjectTypeId is not { } ptId)
        {
            MessageBox.Show("בחר סוג פרויקט תחילה.", "מידע",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_policyDisciplinesMap.TryGetValue(ptId, out var items)) return;

        try
        {
            var dbFactory = GetFactory();
            if (dbFactory is null) return;

            using var context = dbFactory.CreateDbContext();

            int added = 0, updated = 0, removed = 0;

            foreach (var item in items)
            {
                if (item.MappingId is { } id)
                {
                    var row = context.ProjectTypeDisciplines.FirstOrDefault(m => m.Id == id);
                    if (row is null) continue;

                    if (!item.IsActive)
                    {
                        // Deactivate by deleting the mapping row (clean state).
                        context.ProjectTypeDisciplines.Remove(row);
                        removed++;
                    }
                    else if (row.IsActive != item.IsActive || row.IsRequired != item.IsRequired)
                    {
                        row.IsActive = item.IsActive;
                        row.IsRequired = item.IsActive && item.IsRequired;
                        updated++;
                    }
                }
                else if (item.IsActive)
                {
                    var row = new ProjectTypeDiscipline
                    {
                        ProjectTypeId = ptId,
                        DisciplineTaskTypeId = item.DisciplineTaskTypeId,
                        IsActive = true,
                        IsRequired = item.IsRequired,
                        SortOrder = 0,
                    };
                    context.ProjectTypeDisciplines.Add(row);
                    added++;
                }
            }

            context.SaveChanges();

            _policyDisciplinesMap.Remove(ptId);
            RefreshPolicyDisciplinesList();

            StatusText.Text = $"✅ תחומים נשמרו — {added} נוספו, {updated} עודכנו, {removed} הוסרו";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת תחומים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  TAB 3: DASHBOARD — Workflow instances                              ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    #region Dashboard Tab

    private async void LoadDashboardTab()
    {
        _dashboardLoaded = true;

        try
        {
            _dashboardQueryService = App.ServiceProvider?.GetRequiredService<IWorkflowQueryService>();
            _dashboardOrchestrator = App.ServiceProvider?.GetRequiredService<WorkflowTaskOrchestrator>();
            _dashboardPolicyService = App.ServiceProvider?.GetRequiredService<IProjectWorkflowPolicyService>();

            var factory = GetFactory();
            if (factory == null) return;

            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);

            _dashboardProjects = await db.Projects
                .AsNoTracking()
                .Where(p => p.EndOfProject != true)
                .OrderBy(p => p.Title)
                .ToListAsync(CancellationToken.None);

            DashboardProjectCombo.ItemsSource = _dashboardProjects;
            StatusText.Text = $"{_dashboardProjects.Count} פרויקטים זמינים";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"שגיאה: {ex.Message}";
        }
    }

    private async void DashboardProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _dashboardSelectedProject = DashboardProjectCombo.SelectedItem as Project;
        if (_dashboardSelectedProject == null) return;

        await LoadDashboardInstancesAsync();
        await LoadDashboardAllowedDefinitionsAsync();
    }

    private async void DashboardActiveOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_dashboardSelectedProject != null)
            await LoadDashboardInstancesAsync();
    }

    private async void DashboardRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboardSelectedProject != null)
            await LoadDashboardInstancesAsync();
    }

    private async Task LoadDashboardInstancesAsync()
    {
        if (_dashboardSelectedProject == null || _dashboardQueryService == null) return;

        try
        {
            StatusText.Text = "טוען תהליכים...";

            var activeOnly = DashboardActiveOnlyCheck.IsChecked == true;
            var statusFilter = activeOnly ? WorkflowStatus.Active : (WorkflowStatus?)null;

            var instances = await _dashboardQueryService.GetByProjectAsync(
                _dashboardSelectedProject.Id, statusFilter, CancellationToken.None);

            DashboardInstancesGrid.ItemsSource = instances;
            StatusText.Text = $"{instances.Count} תהליכים נמצאו";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"שגיאה: {ex.Message}";
        }
    }

    private async Task LoadDashboardAllowedDefinitionsAsync()
    {
        if (_dashboardSelectedProject == null || _dashboardPolicyService == null) return;

        try
        {
            _dashboardDefinitions = (await _dashboardPolicyService.GetAllowedWorkflowsAsync(
                _dashboardSelectedProject.Id, CancellationToken.None)).ToList();

            DashboardDefinitionCombo.ItemsSource = _dashboardDefinitions;

            if (DashboardDefinitionCombo.SelectedItem is WorkflowDefinitionDto wd &&
                !_dashboardDefinitions.Any(d => d.Id == wd.Id))
            {
                DashboardDefinitionCombo.SelectedItem = null;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"שגיאה: {ex.Message}";
        }
    }

    private async void DashboardStartWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboardSelectedProject == null)
        {
            StatusText.Text = "נא לבחור פרויקט.";
            return;
        }

        if (DashboardDefinitionCombo.SelectedItem is not WorkflowDefinitionDto selectedDef)
        {
            StatusText.Text = "נא לבחור תבנית תהליך.";
            return;
        }

        if (_dashboardOrchestrator == null) return;

        try
        {
            StatusText.Text = "מפעיל תהליך...";

            var userId = CurrentUserContext.Instance.CurrentUserId ?? 0;
            var result = await _dashboardOrchestrator.StartWorkflowAsync(
                selectedDef.Id,
                _dashboardSelectedProject.Id,
                WorkflowTriggerType.Manual,
                triggerEntityId: null,
                userId,
                notes: null,
                CancellationToken.None);

            var taskCount = result.CreatedTasks.Count;
            StatusText.Text = $"תהליך '{selectedDef.Name}' הופעל בהצלחה ({taskCount} משימות נוצרו).";

            await LoadDashboardInstancesAsync();

            // Open the new instance window
            var window = new WPF_Window.WorkflowInstanceWindow(result.Instance.Id)
            {
                Owner = this
            };
            window.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"שגיאה בהפעלת תהליך: {ex.Message}";
        }
    }

    private void DashboardGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DashboardInstancesGrid.SelectedItem is not WorkflowInstanceDto instance) return;

        var window = new WPF_Window.WorkflowInstanceWindow(instance.Id)
        {
            Owner = this
        };
        window.Show();
    }

    #endregion

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  TAB 4: BEHAVIORS — TaskBehaviorDefinition management               ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    #region Behavior Tab

    private void LoadBehaviorTab()
    {
        _behaviorLoaded = true;
        LoadBehaviorReferenceData();
        RefreshBehaviorList();
    }

    private void LoadBehaviorReferenceData()
    {
        var factory = GetFactory();
        if (factory is null) return;

        using var db = factory.CreateDbContext();

        _behaviorTaskTypes = db.TaskTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .AsNoTracking()
            .ToList();

        _behaviorStatuses = db.ProjectAssignmentStatuses
            .OrderBy(s => s.Id)
            .AsNoTracking()
            .ToList();
    }

    private void RefreshBehaviorList()
    {
        var factory = GetFactory();
        if (factory is null) return;

        using var db = factory.CreateDbContext();

        var definitions = db.TaskBehaviorDefinitions
            .Include(b => b.TaskType)
            .Include(b => b.TriggerRules)
            .Include(b => b.CompletionRules)
            .OrderBy(b => b.Id)
            .AsNoTracking()
            .ToList();

        _behaviorItems = definitions.Select(d => new BehaviorListItem
        {
            Id = d.Id,
            Code = d.Code,
            DisplayName = d.DisplayName,
            ActiveIcon = d.IsActive ? "✅" : "⏸️",
            Summary = $"{d.TaskType?.Name ?? "—"} | {d.TriggerRules.Count} טריגרים, {d.CompletionRules.Count} השלמות",
        }).ToList();

        BehaviorListBox.ItemsSource = _behaviorItems;
        BehaviorDetailPanel.Children.Clear();
        BehaviorDetailHeader.Text = "בחר התנהגות מהרשימה...";

        StatusText.Text = $"🧠 {definitions.Count} התנהגויות מוגדרות";
    }

    private void BehaviorRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadBehaviorReferenceData();
        RefreshBehaviorList();
    }

    private void BehaviorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BehaviorListBox.SelectedItem is not BehaviorListItem item) return;
        ShowBehaviorDetail(item.Id);
    }

    // ─── Show detail for selected behavior ───

    private void ShowBehaviorDetail(int behaviorId)
    {
        var factory = GetFactory();
        if (factory is null) return;

        using var db = factory.CreateDbContext();

        var behavior = db.TaskBehaviorDefinitions
            .Include(b => b.TaskType)
            .Include(b => b.TriggerRules.OrderBy(r => r.SortOrder))
            .Include(b => b.CompletionRules.OrderBy(r => r.SortOrder))
                .ThenInclude(r => r.ResultingStatus)
            .FirstOrDefault(b => b.Id == behaviorId);

        if (behavior is null) return;

        BehaviorDetailHeader.Text = $"🧠 {behavior.DisplayName} ({behavior.Code})";
        var panel = BehaviorDetailPanel;
        panel.Children.Clear();

        // ── Basic fields ──
        AddLabel(panel, "קוד:", behavior.Code);
        AddLabel(panel, "תיאור:", behavior.Description ?? "—");
        AddLabel(panel, "פעיל:", behavior.IsActive ? "✅ כן" : "❌ לא");

        AddSeparator(panel);

        // ── TaskType ──
        AddLabel(panel, "סוג משימה:", "", isBold: true);
        var taskTypeCombo = new ComboBox
        {
            ItemsSource = _behaviorTaskTypes,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 4, 0, 0),
        };
        if (behavior.TaskTypeId.HasValue)
        {
            var current = _behaviorTaskTypes.FirstOrDefault(t => t.Id == behavior.TaskTypeId.Value);
            if (current is not null) taskTypeCombo.SelectedItem = current;
        }
        panel.Children.Add(taskTypeCombo);

        // ── Toggles ──
        var autoCreateCb = new CheckBox
        {
            Content = "יצירה אוטומטית כשטריגר מזוהה",
            IsChecked = behavior.AutoCreateOnTrigger,
            Margin = new Thickness(0, 8, 0, 0),
        };
        panel.Children.Add(autoCreateCb);

        var autoCloseCb = new CheckBox
        {
            Content = "סגירה אוטומטית כשתנאי השלמה מתקיים",
            IsChecked = behavior.AutoCloseOnCompletion,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(autoCloseCb);

        var isActiveCb = new CheckBox
        {
            Content = "התנהגות פעילה",
            IsChecked = behavior.IsActive,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(isActiveCb);

        // ── DisplayName ──
        AddSeparator(panel);
        AddLabel(panel, "שם תצוגה:", "", isBold: true);
        var displayNameBox = new TextBox
        {
            Text = behavior.DisplayName,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(displayNameBox);

        AddLabel(panel, "תיאור:", "", isBold: true);
        var descBox = new TextBox
        {
            Text = behavior.Description ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 50,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(descBox);

        // ── Save button ──
        var saveBtn = new Button
        {
            Content = "💾 שמור שינויים",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 8, 0, 0),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
        };
        saveBtn.Click += (_, _) =>
        {
            var selectedType = taskTypeCombo.SelectedItem as TaskType;
            SaveBehavior(behavior.Id,
                displayNameBox.Text.Trim(),
                descBox.Text.Trim(),
                selectedType?.Id,
                autoCreateCb.IsChecked == true,
                autoCloseCb.IsChecked == true,
                isActiveCb.IsChecked == true);
        };
        panel.Children.Add(saveBtn);

        // ════════════════════════════════════════════════════════════════
        //  TRIGGER RULES
        // ════════════════════════════════════════════════════════════════

        AddSeparator(panel);
        AddLabel(panel, $"⚡ טריגרים ({behavior.TriggerRules.Count}):", "", isBold: true);

        foreach (var trigger in behavior.TriggerRules)
        {
            var triggerPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 4, 0, 0),
            };

            var trigGrid = new Grid();
            trigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            trigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var trigInfo = new StackPanel();
            trigInfo.Children.Add(new TextBlock
            {
                Text = $"{GetTriggerTypeName(trigger.TriggerType)}",
                FontWeight = FontWeights.SemiBold,
            });
            trigInfo.Children.Add(new TextBlock
            {
                Text = $"{trigger.Description ?? "—"}{(trigger.ConditionJson is not null ? $" | תנאי: {trigger.ConditionJson}" : "")}",
                Foreground = Brushes.Gray,
                FontSize = 11,
            });
            Grid.SetColumn(trigInfo, 0);
            trigGrid.Children.Add(trigInfo);

            var capturedTriggerId = trigger.Id;
            var removeBtn = new Button
            {
                Content = "🗑",
                ToolTip = "הסר טריגר",
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            removeBtn.Click += (_, _) =>
            {
                if (MessageBox.Show("להסיר טריגר זה?", "אישור",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    RemoveTriggerRule(capturedTriggerId, behaviorId);
                }
            };
            Grid.SetColumn(removeBtn, 1);
            trigGrid.Children.Add(removeBtn);

            triggerPanel.Child = trigGrid;
            panel.Children.Add(triggerPanel);
        }

        // Add trigger button + combo
        var addTriggerGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        addTriggerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addTriggerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addTriggerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var triggerTypeCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<TaskBehaviorTriggerType>()
                .Select(t => new { Value = t, Display = GetTriggerTypeName(t) })
                .ToList(),
            DisplayMemberPath = "Display",
            SelectedValuePath = "Value",
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(triggerTypeCombo, 0);
        addTriggerGrid.Children.Add(triggerTypeCombo);

        var triggerDescBox = new TextBox
        {
            Tag = "תיאור...",
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(triggerDescBox, 1);
        addTriggerGrid.Children.Add(triggerDescBox);

        var addTriggerBtn = new Button
        {
            Content = "➕",
            ToolTip = "הוסף טריגר",
            Padding = new Thickness(8, 4, 8, 4),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
        };
        addTriggerBtn.Click += (_, _) =>
        {
            if (triggerTypeCombo.SelectedValue is not TaskBehaviorTriggerType selectedTriggerType)
            {
                MessageBox.Show("נא לבחור סוג טריגר.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AddTriggerRule(behaviorId, selectedTriggerType, triggerDescBox.Text.Trim());
        };
        Grid.SetColumn(addTriggerBtn, 2);
        addTriggerGrid.Children.Add(addTriggerBtn);
        panel.Children.Add(addTriggerGrid);

        // ════════════════════════════════════════════════════════════════
        //  COMPLETION RULES
        // ════════════════════════════════════════════════════════════════

        AddSeparator(panel);
        AddLabel(panel, $"🏁 כללי השלמה ({behavior.CompletionRules.Count}):", "", isBold: true);

        foreach (var completion in behavior.CompletionRules)
        {
            var compPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 4, 0, 0),
            };

            var compGrid = new Grid();
            compGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            compGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var compInfo = new StackPanel();
            compInfo.Children.Add(new TextBlock
            {
                Text = $"{GetCompletionTypeName(completion.CompletionType)} → {completion.ResultingStatus?.Name ?? $"סטטוס #{completion.ResultingStatusId}"}",
                FontWeight = FontWeights.SemiBold,
            });
            compInfo.Children.Add(new TextBlock
            {
                Text = $"{completion.Description ?? "—"}{(completion.ConditionJson is not null ? $" | תנאי: {completion.ConditionJson}" : "")}",
                Foreground = Brushes.Gray,
                FontSize = 11,
            });
            Grid.SetColumn(compInfo, 0);
            compGrid.Children.Add(compInfo);

            var capturedCompId = completion.Id;
            var removeCompBtn = new Button
            {
                Content = "🗑",
                ToolTip = "הסר כלל השלמה",
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            removeCompBtn.Click += (_, _) =>
            {
                if (MessageBox.Show("להסיר כלל השלמה זה?", "אישור",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    RemoveCompletionRule(capturedCompId, behaviorId);
                }
            };
            Grid.SetColumn(removeCompBtn, 1);
            compGrid.Children.Add(removeCompBtn);

            compPanel.Child = compGrid;
            panel.Children.Add(compPanel);
        }

        // Add completion button + combos
        var addCompGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        addCompGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addCompGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addCompGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var compTypeCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<TaskBehaviorCompletionType>()
                .Select(t => new { Value = t, Display = GetCompletionTypeName(t) })
                .ToList(),
            DisplayMemberPath = "Display",
            SelectedValuePath = "Value",
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(compTypeCombo, 0);
        addCompGrid.Children.Add(compTypeCombo);

        var compStatusCombo = new ComboBox
        {
            ItemsSource = _behaviorStatuses,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(compStatusCombo, 1);
        addCompGrid.Children.Add(compStatusCombo);

        var addCompBtn = new Button
        {
            Content = "➕",
            ToolTip = "הוסף כלל השלמה",
            Padding = new Thickness(8, 4, 8, 4),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
        };
        addCompBtn.Click += (_, _) =>
        {
            if (compTypeCombo.SelectedValue is not TaskBehaviorCompletionType selectedCompType)
            {
                MessageBox.Show("נא לבחור סוג השלמה.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (compStatusCombo.SelectedValue is not int selectedStatusId)
            {
                MessageBox.Show("נא לבחור סטטוס תוצאה.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AddCompletionRule(behaviorId, selectedCompType, selectedStatusId);
        };
        Grid.SetColumn(addCompBtn, 2);
        addCompGrid.Children.Add(addCompBtn);
        panel.Children.Add(addCompGrid);

        // ── Delete behavior ──
        AddSeparator(panel);
        var deleteBtn = new Button
        {
            Content = "🗑 מחק התנהגות",
            Padding = new Thickness(10, 4, 10, 4),
            Foreground = Brushes.DarkRed,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        deleteBtn.Click += (_, _) =>
        {
            if (MessageBox.Show($"למחוק את ההתנהגות '{behavior.DisplayName}' וכל הכללים שלה?",
                "אישור מחיקה", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                DeleteBehavior(behaviorId);
            }
        };
        panel.Children.Add(deleteBtn);
    }

    // ─── Behavior CRUD ───

    private void SaveBehavior(int id, string displayName, string description,
        int? taskTypeId, bool autoCreate, bool autoClose, bool isActive)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                MessageBox.Show("שם תצוגה לא יכול להיות ריק.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var entity = db.TaskBehaviorDefinitions.Find(id);
            if (entity is null) return;

            entity.DisplayName = displayName;
            entity.Description = string.IsNullOrWhiteSpace(description) ? null : description;
            entity.TaskTypeId = taskTypeId;
            entity.AutoCreateOnTrigger = autoCreate;
            entity.AutoCloseOnCompletion = autoClose;
            entity.IsActive = isActive;

            db.SaveChanges();
            RefreshBehaviorList();
            StatusText.Text = "✅ התנהגות נשמרה";

            // Re-select to refresh detail
            var reselect = _behaviorItems.FirstOrDefault(b => b.Id == id);
            if (reselect is not null)
            {
                BehaviorListBox.SelectedItem = reselect;
                ShowBehaviorDetail(id);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BehaviorAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var code = $"NewBehavior_{DateTime.UtcNow:yyyyMMddHHmmss}";

            var entity = new TaskBehaviorDefinition
            {
                Code = code,
                DisplayName = "התנהגות חדשה",
                Description = null,
                AutoCreateOnTrigger = true,
                AutoCloseOnCompletion = true,
                IsActive = false,
                CreatedAtUtc = DateTime.UtcNow,
            };

            db.TaskBehaviorDefinitions.Add(entity);
            db.SaveChanges();

            RefreshBehaviorList();

            // Select the new item
            var newItem = _behaviorItems.FirstOrDefault(b => b.Id == entity.Id);
            if (newItem is not null)
            {
                BehaviorListBox.SelectedItem = newItem;
                ShowBehaviorDetail(entity.Id);
            }

            StatusText.Text = "✅ התנהגות חדשה נוצרה — יש לערוך את הפרטים";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה ביצירת התנהגות: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteBehavior(int behaviorId)
    {
        try
        {
            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var entity = db.TaskBehaviorDefinitions
                .Include(b => b.TriggerRules)
                .Include(b => b.CompletionRules)
                .FirstOrDefault(b => b.Id == behaviorId);

            if (entity is null) return;

            db.TaskCompletionRules.RemoveRange(entity.CompletionRules);
            db.TaskTriggerRules.RemoveRange(entity.TriggerRules);
            db.TaskBehaviorDefinitions.Remove(entity);
            db.SaveChanges();

            RefreshBehaviorList();
            StatusText.Text = "✅ התנהגות נמחקה";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה במחיקה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddTriggerRule(int behaviorId, TaskBehaviorTriggerType triggerType, string? description)
    {
        try
        {
            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var maxSort = db.TaskTriggerRules
                .Where(r => r.BehaviorDefinitionId == behaviorId)
                .Select(r => (int?)r.SortOrder)
                .Max() ?? 0;

            db.TaskTriggerRules.Add(new TaskTriggerRule
            {
                BehaviorDefinitionId = behaviorId,
                TriggerType = triggerType,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                SortOrder = maxSort + 1,
                IsActive = true,
            });

            db.SaveChanges();
            ShowBehaviorDetail(behaviorId);
            StatusText.Text = "✅ טריגר נוסף";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהוספת טריגר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveTriggerRule(int triggerId, int behaviorId)
    {
        try
        {
            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var entity = db.TaskTriggerRules.Find(triggerId);
            if (entity is null) return;

            db.TaskTriggerRules.Remove(entity);
            db.SaveChanges();
            ShowBehaviorDetail(behaviorId);
            StatusText.Text = "✅ טריגר הוסר";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהסרת טריגר: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddCompletionRule(int behaviorId, TaskBehaviorCompletionType compType, int resultingStatusId)
    {
        try
        {
            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var maxSort = db.TaskCompletionRules
                .Where(r => r.BehaviorDefinitionId == behaviorId)
                .Select(r => (int?)r.SortOrder)
                .Max() ?? 0;

            db.TaskCompletionRules.Add(new TaskCompletionRule
            {
                BehaviorDefinitionId = behaviorId,
                CompletionType = compType,
                ResultingStatusId = resultingStatusId,
                SortOrder = maxSort + 1,
                IsActive = true,
            });

            db.SaveChanges();
            ShowBehaviorDetail(behaviorId);
            StatusText.Text = "✅ כלל השלמה נוסף";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהוספת כלל השלמה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveCompletionRule(int completionId, int behaviorId)
    {
        try
        {
            var factory = GetFactory();
            if (factory is null) return;

            using var db = factory.CreateDbContext();

            var entity = db.TaskCompletionRules.Find(completionId);
            if (entity is null) return;

            db.TaskCompletionRules.Remove(entity);
            db.SaveChanges();
            ShowBehaviorDetail(behaviorId);
            StatusText.Text = "✅ כלל השלמה הוסר";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בהסרת כלל השלמה: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── Enum display helpers ───

    private static string GetTriggerTypeName(TaskBehaviorTriggerType type) => type switch
    {
        TaskBehaviorTriggerType.EmailAssignedToProject => "📧 מייל שויך לפרויקט",
        TaskBehaviorTriggerType.AttachmentTagged => "🏷️ קובץ מצורף תויק",
        TaskBehaviorTriggerType.WorkflowStageEntered => "🔄 כניסה לשלב Workflow",
        TaskBehaviorTriggerType.Manual => "✋ ידני",
        _ => type.ToString(),
    };

    private static string GetCompletionTypeName(TaskBehaviorCompletionType type) => type switch
    {
        TaskBehaviorCompletionType.AllAttachmentsTagged => "🏷️ כל הקבצים תויקו",
        TaskBehaviorCompletionType.EmailReplySent => "📧 נשלחה תשובה",
        TaskBehaviorCompletionType.Manual => "✋ ידני",
        TaskBehaviorCompletionType.AllRequiredStageTasksClosed => "✅ כל משימות חובה הושלמו",
        _ => type.ToString(),
    };

    #endregion
    // ═══════════════════════════════════════════════════════════════════════

    private static void AddLabel(StackPanel panel, string label, string value, bool isBold = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        sp.Children.Add(new TextBlock
        {
            Text = label + " ",
            FontWeight = isBold ? FontWeights.Bold : FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
        });
        sp.Children.Add(new TextBlock
        {
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(sp);
    }

    private static void AddSeparator(StackPanel panel)
    {
        panel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
    }

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  TAB 5: HELP — System reference guide                               ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    #region Help Tab

    private void LoadHelpTab()
    {
        _helpLoaded = true;
        BuildHelpContent();
    }

    private void BuildHelpContent()
    {
        var panel = HelpContentPanel;
        panel.Children.Clear();

        // ── Title ──
        AddHelpTitle(panel, "❓ מדריך — חלון ניהול תהליכי עבודה");
        AddHelpParagraph(panel,
            "חלון זה מרכז את כל ניהול תהליכי העבודה (Workflow) ומשימות אוטומטיות במערכת. " +
            "כאן מגדירים תהליכים, שלבים, מעברים, משימות, שיוך לסוגי פרויקט, והתנהגויות אוטומטיות.");

        AddHelpDivider(panel);

        // ══════════════════════════════════════════════════════
        //  TAB 1: Builder
        // ══════════════════════════════════════════════════════
        AddHelpSection(panel, "🏗️ טאב 1: בונה תהליכים ומשימות");

        AddHelpSubSection(panel, "מה זה?");
        AddHelpParagraph(panel,
            "בונה התהליכים מציג עץ היררכי של כל תהליכי העבודה במערכת. " +
            "כל תהליך (Workflow) מכיל שלבים (Stages), כל שלב מכיל מעברים (Transitions) ומשימות (Tasks).");

        AddHelpSubSection(panel, "מבנה העץ:");
        AddHelpBullet(panel, "📋 תהליך", "השורש — מגדיר שם, קוד ייחודי, תיאור, וסוג טריגר (ידני/אוטומטי).");
        AddHelpBullet(panel, "📌 שלב", "נקודת ציון בתהליך — למשל \"קליטת חומר\", \"בדיקה מקצועית\". לכל שלב יש שם, סדר, וסימון האם הוא ראשוני/סופי.");
        AddHelpBullet(panel, "➡️ מעברי קדימה", "כללי מעבר לשלב הבא — מגדירים לאן אפשר להתקדם ובאילו תנאים.");
        AddHelpBullet(panel, "↩️ מעברי חזרה", "מעברים לשלבים קודמים — למשל \"החזר לתיקון\". מאפשרים מחזוריות.");
        AddHelpBullet(panel, "📋 משימות שלב", "תבניות משימה — כשתהליך מגיע לשלב, נוצרות משימות מהתבניות האלה אוטומטית.");

        AddHelpSubSection(panel, "פעולות זמינות:");
        AddHelpBullet(panel, "➕ תהליך חדש", "יוצר תהליך ריק עם קוד ייחודי.");
        AddHelpBullet(panel, "➕ שלב חדש", "מוסיף שלב לתהליך הנבחר.");
        AddHelpBullet(panel, "➕ מעבר חדש", "מגדיר כלל מעבר בין שלבים (קדימה או חזרה).");
        AddHelpBullet(panel, "➕ משימה חדשה", "מוסיף תבנית משימה לשלב — עם סוג משימה, עובד ברירת מחדל, וסימון חובה.");

        AddHelpSubSection(panel, "איך זה עובד בקוד:");
        AddHelpCode(panel,
            "WorkflowSeedService — מגדיר תהליכים ושלבים בהרצה ראשונה\n" +
            "WorkflowEngine.AdvanceStageAsync() — מקדם תהליך לשלב הבא לפי כללי מעבר\n" +
            "WorkflowTaskOrchestrator.CreateStageTasksAsync() — יוצר משימות (ProjectAssignment) מתבניות השלב");

        AddHelpDivider(panel);

        // ══════════════════════════════════════════════════════
        //  TAB 2: Policy
        // ══════════════════════════════════════════════════════
        AddHelpSection(panel, "⚙️ טאב 2: שיוך תהליכים לסוגי פרויקט");

        AddHelpSubSection(panel, "מה זה?");
        AddHelpParagraph(panel,
            "מגדיר אילו תהליכי עבודה זמינים לכל סוג פרויקט. " +
            "למשל: פרויקט מסוג \"תנועתי\" יכול להפעיל תהליך \"עיצוב\" ותהליך \"בדיקת תוכנית\", " +
            "אבל לא תהליך \"ייעוץ\".");

        AddHelpSubSection(panel, "איך עובדים:");
        AddHelpBullet(panel, "בצד ימין", "בוחרים סוג פרויקט מהרשימה.");
        AddHelpBullet(panel, "בצד שמאל", "מסמנים ✅ אילו תהליכים מותרים לסוג הזה.");
        AddHelpBullet(panel, "שמירה", "כפתור שמירה שומר את המיפוי לטבלת ProjectTypeWorkflowDefinition.");

        AddHelpSubSection(panel, "איך זה עובד בקוד:");
        AddHelpCode(panel,
            "ProjectWorkflowPolicyService.GetAllowedDefinitionsAsync() — שולף תהליכים מותרים לפרויקט\n" +
            "טבלת ProjectTypeWorkflowDefinition — מיפוי Many-to-Many בין סוגי פרויקט לתהליכים");

        AddHelpDivider(panel);

        // ══════════════════════════════════════════════════════
        //  TAB 3: Dashboard
        // ══════════════════════════════════════════════════════
        AddHelpSection(panel, "📊 טאב 3: לוח תהליכים");

        AddHelpSubSection(panel, "מה זה?");
        AddHelpParagraph(panel,
            "לוח בקרה שמציג את כל מופעי התהליכים (Workflow Instances) הפעילים במערכת. " +
            "מאפשר לראות באיזה שלב כל תהליך נמצא, מי יצר אותו, ומה הסטטוס.");

        AddHelpSubSection(panel, "פעולות זמינות:");
        AddHelpBullet(panel, "סינון לפי פרויקט", "בוחרים פרויקט ← רואים את כל התהליכים הפעילים שלו.");
        AddHelpBullet(panel, "הפעלת תהליך חדש", "בוחרים תהליך מותר (לפי Policy) ← מפעילים מופע חדש.");
        AddHelpBullet(panel, "קידום שלב", "מקדמים מופע לשלב הבא (בתנאי שכל משימות חובה הושלמו).");

        AddHelpSubSection(panel, "איך זה עובד בקוד:");
        AddHelpCode(panel,
            "WorkflowQueryService — שולף מופעים, שלבים, סטטיסטיקות\n" +
            "WorkflowEngine.StartAsync() — מפעיל מופע חדש\n" +
            "WorkflowEngine.AdvanceStageAsync() — מקדם שלב (בודק מעברים + יוצר משימות)");

        AddHelpDivider(panel);

        // ══════════════════════════════════════════════════════
        //  TAB 4: Behaviors
        // ══════════════════════════════════════════════════════
        AddHelpSection(panel, "🧠 טאב 4: התנהגויות משימה");

        AddHelpSubSection(panel, "מה זה?");
        AddHelpParagraph(panel,
            "מנגנון אוטומציה שמגדיר כיצד משימות נוצרות ונסגרות אוטומטית בתגובה לאירועים במערכת. " +
            "כל התנהגות (Behavior) מקושרת לסוג משימה (TaskType) ומכילה טריגרים (יצירה) וכללי השלמה (סגירה).");

        AddHelpSubSection(panel, "מרכיבי התנהגות:");
        AddHelpBullet(panel, "קוד (Code)", "מזהה ייחודי ויציב — האפליקציה משתמשת בו. לעולם לא משתנה.");
        AddHelpBullet(panel, "שם תצוגה", "שם בעברית — ניתן לשינוי חופשי.");
        AddHelpBullet(panel, "סוג משימה (TaskType)", "הסוג שהמשימה האוטומטית תקבל.");
        AddHelpBullet(panel, "יצירה אוטומטית", "האם ליצור משימה אוטומטית כשהטריגר מתרחש.");
        AddHelpBullet(panel, "סגירה אוטומטית", "האם לסגור/לעדכן סטטוס כשכלל השלמה מתקיים.");

        AddHelpSubSection(panel, "סוגי טריגרים (יצירת משימה):");
        AddHelpBullet(panel, "📧 מייל הוקצה לפרויקט", "כשמייל מקבל ProjectId ← נוצרת משימה (למשל: תיוק חומר).");
        AddHelpBullet(panel, "📎 קובץ מצורף תויק", "כשקובץ מצורף מקבל ProjectFileId ← נוצרת משימה (למשל: בדיקה מקצועית).");
        AddHelpBullet(panel, "✋ ידני", "משימה נוצרת ידנית — ללא טריגר אוטומטי.");

        AddHelpSubSection(panel, "סוגי השלמה (סגירת משימה):");
        AddHelpBullet(panel, "📎 כל הקבצים תויקו", "כל הקבצים המצורפים למייל קיבלו ProjectFileId ← המשימה נסגרת.");
        AddHelpBullet(panel, "📧 נשלחה תגובה", "תגובה למייל נשלחה — עם סוג (הערות/אישור) שקובע את הסטטוס החדש.");
        AddHelpBullet(panel, "✅ משימות חובה הושלמו", "כל משימות החובה בשלב הנוכחי הושלמו.");
        AddHelpBullet(panel, "✋ ידני", "משימה נסגרת ידנית.");

        AddHelpSubSection(panel, "איך זה עובד בקוד:");
        AddHelpCode(panel,
            "TaskLifecycleService — השירות המרכזי:\n" +
            "  .OnEmailAssignedToProjectAsync() — מייל הוקצה → בודק טריגרים → יוצר משימות\n" +
            "  .OnAttachmentTaggedAsync() — קובץ תויק → יוצר משימות + בודק השלמה\n" +
            "  .OnEmailReplySentAsync() — תגובה נשלחה → בודק כללי השלמה → מעדכן סטטוס\n\n" +
            "TaskBehaviorSeedService — סיד התנהגויות:\n" +
            "  Seeds MaterialFiling + ProfessionalReview behaviors on startup");

        AddHelpDivider(panel);

        // ══════════════════════════════════════════════════════
        //  TaskType Reference
        // ══════════════════════════════════════════════════════
        AddHelpSection(panel, "📑 סוגי משימה (TaskType) — מפתח");

        AddHelpParagraph(panel,
            "כל סוג משימה מחבר 3 שכבות: סיווג (ProjectAssignment), תבנית Workflow (WorkflowStageTask), ואוטומציה (TaskBehaviorDefinition). " +
            "שדה Code הוא מזהה יציב לשימוש פנימי; שם התצוגה בעברית ניתן לשינוי חופשי.");

        AddHelpTaskTypeRow(panel, "General", "כללי",
            "סיווג ברירת מחדל. משימה ידנית רגילה — ללא אוטומציה.");
        AddHelpTaskTypeRow(panel, "OfficePlanning", "תכנון במשרד",
            "סיווג עבודת תכנון. נוצר ידנית או דרך Workflow — ללא אוטומציה.");
        AddHelpTaskTypeRow(panel, "PlanReview", "בדיקת תוכנית",
            "סיווג בדיקת תוכנית. תבנית לשלבי בדיקה ב-Workflow — ללא אוטומציה.");
        AddHelpTaskTypeRow(panel, "MaterialFiling", "תיוק חומר",
            "⚡ אוטומטי: מייל הוקצה לפרויקט → נוצרת משימת תיוק. כל הקבצים תויקו → המשימה נסגרת.");
        AddHelpTaskTypeRow(panel, "ProfessionalReview", "בדיקה מקצועית",
            "⚡ אוטומטי: קובץ תויק → נוצרת משימת בדיקה. תגובת הערות → \"ממתין לתיקון\". תגובת אישור → \"מאושר\".");

        AddHelpDivider(panel);

        // ══════════════════════════════════════════════════════
        //  Flow Diagram
        // ══════════════════════════════════════════════════════
        AddHelpSection(panel, "🔄 זרימת תהליך — מ-A עד Z");

        AddHelpCode(panel,
            "1. מנהל מגדיר תהליך + שלבים + מעברים + משימות שלב      ← טאב 1 (בונה)\n" +
            "2. מנהל משייך תהליכים לסוגי פרויקט                      ← טאב 2 (שיוך)\n" +
            "3. מנהל מפעיל מופע תהליך על פרויקט ספציפי               ← טאב 3 (לוח)\n" +
            "4. WorkflowEngine מעביר את המופע לשלב הראשון\n" +
            "5. WorkflowTaskOrchestrator יוצר משימות מתבניות השלב\n" +
            "6. עובדים מבצעים את המשימות\n" +
            "7. כשכל משימות חובה הושלמו ← ניתן לקדם לשלב הבא\n" +
            "8. WorkflowEngine בודק כללי מעבר ← מתקדם\n" +
            "9. חוזר לשלב 5 עד שהשלב הסופי מושלם\n\n" +
            "במקביל — התנהגויות אוטומטיות (טאב 4):\n" +
            "  מייל/קובץ/תגובה → TaskLifecycleService → יצירה/סגירה אוטומטית של משימות");
    }

    // ── Help UI Builders ──

    private static void AddHelpTitle(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
        });
    }

    private static void AddHelpSection(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 6),
        });
    }

    private static void AddHelpSubSection(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 6, 0, 2),
            Foreground = Brushes.DimGray,
        });
    }

    private static void AddHelpParagraph(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 8, 6),
            LineHeight = 22,
        });
    }

    private static void AddHelpBullet(StackPanel panel, string label, string description)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 2, 0, 2) };
        sp.Children.Add(new TextBlock
        {
            Text = "• " + label + "  ",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top,
        });
        sp.Children.Add(new TextBlock
        {
            Text = "— " + description,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 700,
        });
        panel.Children.Add(sp);
    }

    private static void AddHelpCode(StackPanel panel, string code)
    {
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(8, 4, 8, 8),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = FlowDirection.LeftToRight,
                LineHeight = 20,
            },
        });
    }

    private static void AddHelpTaskTypeRow(StackPanel panel, string code, string hebrewName, string description)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(8, 3, 8, 3),
        };

        var sp = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0, 0, 8, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = "(" + hebrewName + ")",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
        });
        sp.Children.Add(header);
        sp.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            LineHeight = 20,
        });
        border.Child = sp;
        panel.Children.Add(border);
    }

    private static void AddHelpDivider(StackPanel panel)
    {
        panel.Children.Add(new Separator
        {
            Margin = new Thickness(0, 10, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
        });
    }

    #endregion
}
