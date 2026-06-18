using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailContext;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Represents a single action type row in the left-side list.
/// </summary>
public sealed class ActionDefinitionItem : INotifyPropertyChanged
{
    public required string ActionCode { get; init; }
    public required string DisplayName { get; init; }

    private int _authorizedCount;
    public int AuthorizedCount
    {
        get => _authorizedCount;
        set { _authorizedCount = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Represents a user row in the right-side checklist.
/// </summary>
public sealed class UserPermissionItem : INotifyPropertyChanged
{
    public int UserId { get; init; }
    public string UserName { get; init; } = "";
    public string? UserEmail { get; init; }

    private bool _isAuthorized;
    public bool IsAuthorized
    {
        get => _isAuthorized;
        set { _isAuthorized = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Admin window for managing which users are authorized to perform each action type.
/// Left panel: list of all action types (<see cref="ActionFollowUp"/>).
/// Right panel: checkboxes for each active employee — checked = authorized.
/// </summary>
public partial class ActionPermissionWindow : Window
{
    /// <summary>
    /// Static mapping of <see cref="ActionFollowUp"/> enum values to Hebrew display names.
    /// </summary>
    private static readonly (string Code, string Display)[] ActionDefinitions =
    [
        (nameof(ActionFollowUp.NewProjectDialog),      "יצירת פרויקט חדש"),
        (nameof(ActionFollowUp.ProjectPicker),         "שיוך לפרויקט קיים"),
        (nameof(ActionFollowUp.TaskCreationDialog),    "יצירת / שיוך משימה"),
        (nameof(ActionFollowUp.FileImportDialog),      "ייבוא קבצים"),
        (nameof(ActionFollowUp.DecisionDialog),        "העברה להחלטה"),
        (nameof(ActionFollowUp.DisciplineDialog),      "הוספת תחום"),
        (nameof(ActionFollowUp.WorkflowAdvanceDialog), "קידום תהליך"),
    ];

    private ObservableCollection<ActionDefinitionItem> _actionItems = [];
    private List<Siuser> _allEmployees = [];

    /// <summary>
    /// In-memory state: ActionCode → set of authorized user IDs.
    /// Built from DB on load, mutated by checkboxes, persisted on Save.
    /// </summary>
    private readonly Dictionary<string, HashSet<int>> _permissionMap = new(StringComparer.Ordinal);

    private string? _selectedActionCode;

    public ActionPermissionWindow()
    {
        InitializeComponent();

        // AUTH-06: Only administrators may manage action permissions
        if (!CurrentUserContext.Instance.IsAdmin)
        {
            MessageBox.Show("אין לך הרשאה לניהול הרשאות פעולה.", "גישה נדחתה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Loaded += (_, _) => Close();
            return;
        }

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
            if (dbFactory == null) return;

            using var context = dbFactory.CreateDbContext();

            // Load all active employees with valid role (exclude Unauthorized)
            _allEmployees = context.Siusers
                .Where(u => u.IsActive && u.Email != null && u.Email != ""
                         && u.Role >= AppUserRole.Employee)
                .OrderBy(u => u.Name)
                .AsNoTracking()
                .ToList();

            // Load all active permissions grouped by action code
            var permissions = context.ActionPermissions
                .AsNoTracking()
                .Where(p => p.IsActive)
                .ToList();

            // Build the in-memory permission map
            _permissionMap.Clear();
            foreach (var (code, _) in ActionDefinitions)
                _permissionMap[code] = [];

            foreach (var p in permissions)
            {
                if (_permissionMap.TryGetValue(p.ActionCode, out var set))
                    set.Add(p.UserId);
            }

            // Build action items with authorized count
            _actionItems = new ObservableCollection<ActionDefinitionItem>(
                ActionDefinitions.Select(a => new ActionDefinitionItem
                {
                    ActionCode = a.Code,
                    DisplayName = a.Display,
                    AuthorizedCount = _permissionMap.GetValueOrDefault(a.Code)?.Count ?? 0,
                }));

            ActionListBox.ItemsSource = _actionItems;
            StatusText.Text = $"{_allEmployees.Count} עובדים, {permissions.Count} הרשאות";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת נתונים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Action selection → refresh user checklist
    // ═══════════════════════════════════════════════════════════════════════

    private void ActionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionListBox.SelectedItem is not ActionDefinitionItem item) return;

        _selectedActionCode = item.ActionCode;
        SelectedActionHeader.Text = $"מורשים ל: {item.DisplayName}";

        var authorizedIds = _permissionMap.GetValueOrDefault(item.ActionCode) ?? [];

        var userItems = _allEmployees.Select(emp => new UserPermissionItem
        {
            UserId = emp.Id,
            UserName = emp.Name ?? "(ללא שם)",
            UserEmail = emp.Email,
            IsAuthorized = authorizedIds.Contains(emp.Id),
        }).ToList();

        ApplyUserFilter(userItems);
    }

    private void UserSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_selectedActionCode == null) return;

        // Re-trigger the list with the current search filter
        var authorizedIds = _permissionMap.GetValueOrDefault(_selectedActionCode) ?? [];

        var userItems = _allEmployees.Select(emp => new UserPermissionItem
        {
            UserId = emp.Id,
            UserName = emp.Name ?? "(ללא שם)",
            UserEmail = emp.Email,
            IsAuthorized = authorizedIds.Contains(emp.Id),
        }).ToList();

        ApplyUserFilter(userItems);
    }

    private void ApplyUserFilter(List<UserPermissionItem> items)
    {
        var search = UserSearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            items = items.Where(u =>
                (u.UserName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.UserEmail?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }

        UserListBox.ItemsSource = items;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Checkbox toggling → update in-memory map
    // ═══════════════════════════════════════════════════════════════════════

    private void UserCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectedActionCode == null) return;
        if (sender is not CheckBox { DataContext: UserPermissionItem item }) return;

        if (!_permissionMap.TryGetValue(_selectedActionCode, out var set))
        {
            set = [];
            _permissionMap[_selectedActionCode] = set;
        }

        if (item.IsAuthorized)
            set.Add(item.UserId);
        else
            set.Remove(item.UserId);

        // Update the count badge in the action list
        var actionItem = _actionItems.FirstOrDefault(a => a.ActionCode == _selectedActionCode);
        if (actionItem != null)
            actionItem.AuthorizedCount = set.Count;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Save — diff in-memory map against DB and apply changes
    // ═══════════════════════════════════════════════════════════════════════

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // AUTH-06: Double-check admin at save time (defense in depth)
        if (!CurrentUserContext.Instance.IsAdmin)
        {
            MessageBox.Show("אין לך הרשאה לשמור הרשאות.", "גישה נדחתה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var dbFactory = App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var context = dbFactory.CreateDbContext();

            // Load all existing permission rows
            var existing = context.ActionPermissions.ToList();
            var existingLookup = existing
                .ToLookup(p => p.ActionCode, StringComparer.Ordinal);

            int added = 0, removed = 0;

            foreach (var (actionCode, desiredUserIds) in _permissionMap)
            {
                var currentRows = existingLookup[actionCode].ToList();
                var currentUserIds = currentRows
                    .Where(r => r.IsActive)
                    .Select(r => r.UserId)
                    .ToHashSet();

                // Users to ADD (in desired but not in current)
                foreach (var userId in desiredUserIds.Except(currentUserIds))
                {
                    // Check if inactive row exists — reactivate it
                    var inactive = currentRows.FirstOrDefault(r => r.UserId == userId && !r.IsActive);
                    if (inactive != null)
                    {
                        inactive.IsActive = true;
                    }
                    else
                    {
                        var displayName = ActionDefinitions
                            .FirstOrDefault(a => a.Code == actionCode).Display ?? actionCode;

                        context.ActionPermissions.Add(new ActionPermission
                        {
                            ActionCode = actionCode,
                            ActionDisplayName = displayName,
                            UserId = userId,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                        });
                    }
                    added++;
                }

                // Users to REMOVE (in current but not in desired) — soft delete
                foreach (var userId in currentUserIds.Except(desiredUserIds))
                {
                    var row = currentRows.First(r => r.UserId == userId && r.IsActive);
                    row.IsActive = false;
                    removed++;
                }
            }

            context.SaveChanges();

            StatusText.Text = $"✅ נשמר — {added} נוספו, {removed} הוסרו";
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
}
