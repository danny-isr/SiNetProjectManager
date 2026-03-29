using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailContext;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Result of the <see cref="AssignActionDialog"/>.
/// Indicates whether the user chose to execute directly or delegate a task.
/// </summary>
public sealed class AssignActionResult
{
    /// <summary>The user wants to execute the action immediately (no task).</summary>
    public bool ExecuteDirectly { get; init; }

    /// <summary>The user wants to create a task for the selected employee.</summary>
    public bool CreateTask { get; init; }

    /// <summary>The selected employee to assign the task to (null if ExecuteDirectly by self).</summary>
    public Siuser? SelectedEmployee { get; init; }

    /// <summary>Optional note the user entered.</summary>
    public string? Note { get; init; }

    /// <summary>The follow-up action type — routes to the correct dialog when the task is opened later.</summary>
    public ActionFollowUp? FollowUp { get; init; }
}

/// <summary>
/// Dialog for assigning an email-based action to an employee.
/// The employee list is filtered by <see cref="ActionPermission"/> rows for the given action.
/// When no permission rows exist for the action, all active employees are shown (open access).
/// The user can choose to execute immediately (if they are authorized) or delegate a task.
/// </summary>
public partial class AssignActionDialog : Window
{
    private readonly int _currentUserId;
    private readonly bool _currentUserIsAuthorized;
    private readonly ActionFollowUp _followUp;
    private ObservableCollection<Siuser> _employees = [];

    /// <summary>The dialog result containing the user's choice.</summary>
    public AssignActionResult? AssignResult { get; private set; }

    /// <param name="actionDescription">Hebrew description of the action being assigned.</param>
    /// <param name="followUp">The action type — used to filter employees by <see cref="ActionPermission"/>.</param>
    public AssignActionDialog(string actionDescription, ActionFollowUp followUp)
    {
        InitializeComponent();

        ActionDescriptionText.Text = actionDescription;
        _currentUserId = CurrentUserContext.Instance.CurrentUserId ?? 0;
        _followUp = followUp;

        LoadAuthorizedEmployees(followUp);

        // Pre-select current user if they are in the authorized list
        if (_currentUserId > 0)
        {
            var self = _employees.FirstOrDefault(e => e.Id == _currentUserId);
            if (self != null)
                EmployeeComboBox.SelectedItem = self;
        }

        _currentUserIsAuthorized = _employees.Any(e => e.Id == _currentUserId);
    }

    /// <summary>
    /// Loads employees authorized for the given action.
    /// If <see cref="ActionPermission"/> rows exist for this action code, only those users are shown.
    /// Otherwise all active employees are shown (open access — no restrictions configured yet).
    /// </summary>
    private void LoadAuthorizedEmployees(ActionFollowUp followUp)
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var context = dbFactory.CreateDbContext();
            var actionCode = followUp.ToString();

            // Check if any permission rows exist for this action
            var authorizedUserIds = context.ActionPermissions
                .AsNoTracking()
                .Where(p => p.ActionCode == actionCode && p.IsActive)
                .Select(p => p.UserId)
                .ToHashSet();

            List<Siuser> employees;

            if (authorizedUserIds.Count > 0)
            {
                // Restricted: only authorized users
                employees = context.Siusers
                    .Where(u => u.IsActive && authorizedUserIds.Contains(u.Id))
                    .OrderBy(u => u.Name)
                    .AsNoTracking()
                    .ToList();
            }
            else
            {
                // Open access: no restrictions configured — show all active employees
                employees = context.Siusers
                    .Where(u => u.IsActive && u.Email != null && u.Email != "")
                    .OrderBy(u => u.Name)
                    .AsNoTracking()
                    .ToList();
            }

            _employees = new ObservableCollection<Siuser>(employees);
            EmployeeComboBox.ItemsSource = _employees;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת עובדים: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EmployeeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = EmployeeComboBox.SelectedItem as Siuser;
        if (selected == null)
        {
            InfoText.Text = "";
            ExecuteNowButton.IsEnabled = false;
            CreateTaskButton.IsEnabled = false;
            return;
        }

        bool isSelf = selected.Id == _currentUserId;

        // "Execute Now" only if current user is both the selected employee AND authorized
        ExecuteNowButton.IsEnabled = isSelf && _currentUserIsAuthorized;
        CreateTaskButton.IsEnabled = true;

        if (isSelf && _currentUserIsAuthorized)
            InfoText.Text = "אתה מורשה — ניתן לבצע מיד או ליצור משימה לעצמך.";
        else if (isSelf && !_currentUserIsAuthorized)
            InfoText.Text = "אין לך הרשאה לבצע פעולה זו — ניתן רק ליצור משימה.";
        else
            InfoText.Text = $"תיווצר משימה עבור {selected.Name}.";
    }

    private void ExecuteNow_Click(object sender, RoutedEventArgs e)
    {
        AssignResult = new AssignActionResult
        {
            ExecuteDirectly = true,
            SelectedEmployee = EmployeeComboBox.SelectedItem as Siuser,
            Note = string.IsNullOrWhiteSpace(NoteTextBox.Text) ? null : NoteTextBox.Text.Trim(),
            FollowUp = _followUp,
        };
        DialogResult = true;
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        var selected = EmployeeComboBox.SelectedItem as Siuser;
        if (selected == null)
        {
            MessageBox.Show("יש לבחור עובד.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AssignResult = new AssignActionResult
        {
            CreateTask = true,
            SelectedEmployee = selected,
            Note = string.IsNullOrWhiteSpace(NoteTextBox.Text) ? null : NoteTextBox.Text.Trim(),
            FollowUp = _followUp,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
