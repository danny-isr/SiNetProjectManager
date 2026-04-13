using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Dialogs;

// ═══════════════════════════════════════════════════════════════════════════
// Tree node data wrappers
// ═══════════════════════════════════════════════════════════════════════════

public sealed record WorkflowNode(WorkflowDefinition Definition);
public sealed record StageNode(WorkflowStageDefinition Stage, WorkflowDefinition Definition);
public sealed record StageTaskNode(WorkflowStageTask StageTask, WorkflowStageDefinition Stage);

/// <summary>
/// Comprehensive Workflow Builder window.
/// Displays a TreeView: Workflow → Stage → Task (with default assignee).
/// Allows adding/removing tasks to stages and assigning default employees.
/// </summary>
public partial class WorkflowBuilderWindow : Window
{
    private List<Siuser> _employees = [];
    private List<TaskType> _taskTypes = [];

    public WorkflowBuilderWindow()
    {
        InitializeComponent();
        LoadReferenceData();
        BuildTree();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Data loading
    // ═══════════════════════════════════════════════════════════════════════

    private IDbContextFactory<SiNetSQLDbContext>? GetFactory() =>
        App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();

    private void LoadReferenceData()
    {
        var factory = GetFactory();
        if (factory == null) return;

        using var db = factory.CreateDbContext();

        _employees = db.Siusers
            .Where(u => u.IsActive && u.Email != null && u.Email != "")
            .OrderBy(u => u.Name)
            .AsNoTracking()
            .ToList();

        _taskTypes = db.TaskTypes
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
            .Where(d => d.IsActive)
            .OrderBy(d => d.Code)
            .AsNoTracking()
            .ToList();

        int totalTasks = 0;

        foreach (var def in definitions)
        {
            var defItem = new TreeViewItem
            {
                Tag = new WorkflowNode(def),
                IsExpanded = true,
            };
            defItem.Header = BuildWorkflowHeader(def);

            foreach (var stage in def.Stages.OrderBy(s => s.SortOrder))
            {
                var stageItem = new TreeViewItem
                {
                    Tag = new StageNode(stage, def),
                    IsExpanded = true,
                };
                stageItem.Header = BuildStageHeader(stage);

                foreach (var stageTask in stage.StageTasks.OrderBy(st => st.SortOrder))
                {
                    totalTasks++;
                    var taskItem = new TreeViewItem
                    {
                        Tag = new StageTaskNode(stageTask, stage),
                    };
                    taskItem.Header = BuildTaskHeader(stageTask);
                    stageItem.Items.Add(taskItem);
                }

                defItem.Items.Add(stageItem);
            }

            WorkflowTree.Items.Add(defItem);
        }

        StatusText.Text = $"{definitions.Count} תהליכים, {definitions.Sum(d => d.Stages.Count)} שלבים, {totalTasks} משימות";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tree headers — visual representation of each node
    // ═══════════════════════════════════════════════════════════════════════

    private static StackPanel BuildWorkflowHeader(WorkflowDefinition def)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = "📂 ",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{def.Name}",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"  ({def.Code})",
            Foreground = Brushes.Gray,
            FontSize = 11,
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

    private static StackPanel BuildStageHeader(WorkflowStageDefinition stage)
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

        int taskCount = stage.StageTasks.Count;
        if (taskCount > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"  ({taskCount} משימות)",
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

    // ═══════════════════════════════════════════════════════════════════════
    // Tree selection → Detail panel
    // ═══════════════════════════════════════════════════════════════════════

    private void WorkflowTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        DetailPanel.Children.Clear();

        if (e.NewValue is TreeViewItem { Tag: WorkflowNode wn })
            ShowWorkflowDetail(wn.Definition);
        else if (e.NewValue is TreeViewItem { Tag: StageNode sn })
            ShowStageDetail(sn.Stage, sn.Definition);
        else if (e.NewValue is TreeViewItem { Tag: StageTaskNode tn })
            ShowTaskDetail(tn.StageTask, tn.Stage);
    }

    // ─── Workflow detail ───

    private void ShowWorkflowDetail(WorkflowDefinition def)
    {
        DetailHeader.Text = $"📂 תהליך: {def.Name}";
        var panel = DetailPanel;

        AddLabel(panel, "קוד:", def.Code);
        AddLabel(panel, "תיאור:", def.Description ?? "—");
        AddLabel(panel, "שלבים:", def.Stages.Count.ToString());
        AddLabel(panel, "מעברים:", def.TransitionRules.Count.ToString());

        AddSeparator(panel);

        // Transitions summary
        AddLabel(panel, "מעברים מוגדרים:", "", isBold: true);
        foreach (var rule in def.TransitionRules)
        {
            var from = def.Stages.FirstOrDefault(s => s.Id == rule.FromStageId)?.Name ?? "?";
            var to = def.Stages.FirstOrDefault(s => s.Id == rule.ToStageId)?.Name ?? "?";
            AddLabel(panel, "    ➡", $"{from}  →  {to}");
        }
    }

    // ─── Stage detail ───

    private void ShowStageDetail(WorkflowStageDefinition stage, WorkflowDefinition def)
    {
        DetailHeader.Text = $"🔵 שלב: {stage.Name}";
        var panel = DetailPanel;

        AddLabel(panel, "קוד:", stage.Code);
        AddLabel(panel, "סדר:", stage.SortOrder.ToString());
        AddLabel(panel, "תיאור:", stage.Description ?? "—");

        if (stage.IsInitial) AddLabel(panel, "🟢", "שלב התחלתי");
        if (stage.IsFinal) AddLabel(panel, "🔴", "שלב סופי");

        AddLabel(panel, "משימות:", stage.StageTasks.Count.ToString());

        AddSeparator(panel);

        // "Add Task" section
        AddLabel(panel, "הוסף משימה לשלב:", "", isBold: true);

        var addGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var taskTypeCombo = new ComboBox
        {
            ItemsSource = _taskTypes,
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

    // ─── Stage Task detail ───

    private void ShowTaskDetail(WorkflowStageTask stageTask, WorkflowStageDefinition stage)
    {
        DetailHeader.Text = $"📋 משימה: {stageTask.TaskType?.Name ?? "—"}";
        var panel = DetailPanel;

        AddLabel(panel, "שלב:", stage.Name);
        AddLabel(panel, "סוג משימה:", stageTask.TaskType?.Name ?? "—");
        AddLabel(panel, "סדר:", stageTask.SortOrder.ToString());
        AddLabel(panel, "חובה:", stageTask.IsRequired ? "✅ כן" : "❌ לא");
        AddLabel(panel, "הערות:", stageTask.Notes ?? "—");

        AddSeparator(panel);

        // Assignee picker
        AddLabel(panel, "שיוך עובד ברירת מחדל:", "", isBold: true);

        var assigneeCombo = new ComboBox
        {
            ItemsSource = _employees,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 4, 0, 0),
        };

        if (stageTask.DefaultAssigneeId.HasValue)
        {
            var current = _employees.FirstOrDefault(e => e.Id == stageTask.DefaultAssigneeId.Value);
            if (current != null) assigneeCombo.SelectedItem = current;
        }

        panel.Children.Add(assigneeCombo);

        // Required checkbox
        var requiredCb = new CheckBox
        {
            Content = "משימה חובה (נדרש השלמה לפני קידום שלב)",
            IsChecked = stageTask.IsRequired,
            Margin = new Thickness(0, 8, 0, 0),
        };
        panel.Children.Add(requiredCb);

        // Notes
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

        // Action buttons
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

        panel.Children.Add(btnPanel);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRUD operations
    // ═══════════════════════════════════════════════════════════════════════

    private void AddTaskToStage(int stageDefinitionId, int taskTypeId)
    {
        try
        {
            var factory = GetFactory();
            if (factory == null) return;

            using var db = factory.CreateDbContext();

            // Check if already exists
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

    // ═══════════════════════════════════════════════════════════════════════
    // UI helpers
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

    // ═══════════════════════════════════════════════════════════════════════
    // Button handlers
    // ═══════════════════════════════════════════════════════════════════════

    private void Refresh_Click(object sender, RoutedEventArgs e) => BuildTree();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
