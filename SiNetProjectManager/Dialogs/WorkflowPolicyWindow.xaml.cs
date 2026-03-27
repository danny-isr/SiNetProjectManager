using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Represents a ProjectType row in the left-side list.
/// </summary>
public sealed class ProjectTypeItem : INotifyPropertyChanged
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
/// Represents a WorkflowDefinition row in the right-side checklist.
/// </summary>
public sealed class WorkflowMappingItem : INotifyPropertyChanged
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

/// <summary>
/// Admin window for managing which WorkflowDefinitions are allowed for each ProjectType.
/// Left panel: list of all ProjectTypes (JobType).
/// Right panel: checkboxes for each WorkflowDefinition — checked = allowed.
/// Star toggle = IsDefault.
/// </summary>
public partial class WorkflowPolicyWindow : Window
{
    private ObservableCollection<ProjectTypeItem> _allProjectTypes = [];
    private List<WorkflowDefinition> _allDefinitions = [];

    /// <summary>
    /// In-memory state: ProjectTypeId → list of (DefinitionId, IsEnabled, IsDefault).
    /// Built from DB on load, mutated by checkboxes, persisted on Save.
    /// </summary>
    private readonly Dictionary<int, List<MappingState>> _mappingMap = [];

    private int? _selectedProjectTypeId;

    public WorkflowPolicyWindow()
    {
        InitializeComponent();
        LoadData();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Data loading
    // ═══════════════════════════════════════════════════════════════════════

    private void LoadData()
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory is null) return;

            using var context = dbFactory.CreateDbContext();

            // Load all job types
            var jobTypes = context.JobTypes
                .AsNoTracking()
                .Where(j => j.Title != null && j.Title != "")
                .OrderBy(j => j.Title)
                .ToList();

            // Load all active workflow definitions
            _allDefinitions = context.WorkflowDefinitions
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .ToList();

            // Load all existing mappings
            var mappings = context.ProjectTypeWorkflowDefinitions
                .AsNoTracking()
                .ToList();

            // Build in-memory map
            _mappingMap.Clear();
            foreach (var jt in jobTypes)
            {
                var jtMappings = mappings
                    .Where(m => m.ProjectTypeId == jt.Id)
                    .Select(m => new MappingState
                    {
                        DefinitionId = m.WorkflowDefinitionId,
                        IsEnabled = m.IsEnabled,
                        IsDefault = m.IsDefault,
                    })
                    .ToList();

                _mappingMap[jt.Id] = jtMappings;
            }

            // Build ProjectType list items
            _allProjectTypes = new ObservableCollection<ProjectTypeItem>(
                jobTypes.Select(j => new ProjectTypeItem
                {
                    Id = j.Id,
                    Title = j.Title!,
                    MappingCount = _mappingMap.GetValueOrDefault(j.Id)?.Count(m => m.IsEnabled) ?? 0,
                }));

            ApplyProjectTypeFilter();

            var totalMappings = mappings.Count(m => m.IsEnabled);
            StatusText.Text = $"{jobTypes.Count} סוגי פרויקט, {_allDefinitions.Count} תהליכים, {totalMappings} שיוכים";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת נתונים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ProjectType selection → refresh workflow checklist
    // ═══════════════════════════════════════════════════════════════════════

    private void ProjectTypeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectTypeListBox.SelectedItem is not ProjectTypeItem item) return;

        _selectedProjectTypeId = item.Id;
        SelectedTypeHeader.Text = $"תהליכים עבור: {item.Title}";

        RefreshWorkflowList();
    }

    private void RefreshWorkflowList()
    {
        if (_selectedProjectTypeId is not { } ptId) return;

        var existing = _mappingMap.GetValueOrDefault(ptId) ?? [];

        var items = _allDefinitions.Select(def =>
        {
            var mapping = existing.FirstOrDefault(m => m.DefinitionId == def.Id);
            return new WorkflowMappingItem
            {
                DefinitionId = def.Id,
                DefinitionName = def.Name,
                DefinitionDescription = def.Description,
                IsEnabled = mapping?.IsEnabled ?? false,
                IsDefault = mapping?.IsDefault ?? false,
            };
        }).ToList();

        WorkflowListBox.ItemsSource = items;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ProjectType search filter
    // ═══════════════════════════════════════════════════════════════════════

    private void ProjectTypeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyProjectTypeFilter();
    }

    private void ApplyProjectTypeFilter()
    {
        var search = ProjectTypeSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            ProjectTypeListBox.ItemsSource = _allProjectTypes;
        }
        else
        {
            ProjectTypeListBox.ItemsSource = _allProjectTypes
                .Where(pt => pt.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Checkbox / toggle changes → update in-memory map
    // ═══════════════════════════════════════════════════════════════════════

    private void WorkflowCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectedProjectTypeId is not { } ptId) return;
        if (sender is not CheckBox { DataContext: WorkflowMappingItem item }) return;

        UpdateMapping(ptId, item);
    }

    private void DefaultToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectedProjectTypeId is not { } ptId) return;
        if (sender is not ToggleButton { DataContext: WorkflowMappingItem item }) return;

        UpdateMapping(ptId, item);
    }

    private void UpdateMapping(int projectTypeId, WorkflowMappingItem item)
    {
        if (!_mappingMap.TryGetValue(projectTypeId, out var list))
        {
            list = [];
            _mappingMap[projectTypeId] = list;
        }

        var existing = list.FirstOrDefault(m => m.DefinitionId == item.DefinitionId);
        if (existing is not null)
        {
            existing.IsEnabled = item.IsEnabled;
            existing.IsDefault = item.IsEnabled && item.IsDefault;
        }
        else if (item.IsEnabled)
        {
            list.Add(new MappingState
            {
                DefinitionId = item.DefinitionId,
                IsEnabled = true,
                IsDefault = item.IsDefault,
            });
        }

        // If unchecked, also clear default
        if (!item.IsEnabled)
            item.IsDefault = false;

        // Update count badge
        var ptItem = _allProjectTypes.FirstOrDefault(p => p.Id == projectTypeId);
        if (ptItem is not null)
            ptItem.MappingCount = list.Count(m => m.IsEnabled);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Save — diff in-memory map against DB and apply changes
    // ═══════════════════════════════════════════════════════════════════════

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory is null) return;

            using var context = dbFactory.CreateDbContext();

            // Load all existing mapping rows
            var existing = context.ProjectTypeWorkflowDefinitions.ToList();
            var existingLookup = existing.ToLookup(m => m.ProjectTypeId);

            int added = 0, updated = 0, removed = 0;

            foreach (var (projectTypeId, desiredMappings) in _mappingMap)
            {
                var currentRows = existingLookup[projectTypeId].ToList();

                // Process desired mappings
                foreach (var desired in desiredMappings.Where(m => m.IsEnabled))
                {
                    var row = currentRows.FirstOrDefault(r => r.WorkflowDefinitionId == desired.DefinitionId);
                    if (row is not null)
                    {
                        // Update existing
                        if (row.IsEnabled != desired.IsEnabled || row.IsDefault != desired.IsDefault)
                        {
                            row.IsEnabled = desired.IsEnabled;
                            row.IsDefault = desired.IsDefault;
                            updated++;
                        }
                    }
                    else
                    {
                        // Add new
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

                // Remove mappings that were unchecked
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Internal state class
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class MappingState
    {
        public int DefinitionId { get; init; }
        public bool IsEnabled { get; set; }
        public bool IsDefault { get; set; }
    }
}
