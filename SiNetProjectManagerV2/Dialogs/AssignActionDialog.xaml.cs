using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;
using SiNetSQL.Services.Authorization;
using SiNetSQL.Services.EmailContext;

namespace SiNetProjectManagerV2.Dialogs;

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
/// Uses <see cref="IActionPermissionService"/> for authorization.
/// Deny-by-default: when no permission rows exist for the action, no employees are shown.
/// Administrators bypass action-level checks and are always included.
/// The user can choose to execute immediately (if they are authorized) or delegate a task.
/// </summary>
public partial class AssignActionDialog : Window
{
    private readonly int _currentUserId;
    private readonly bool _currentUserIsAuthorized;
    private readonly ActionFollowUp _followUp;
    private readonly IActionPermissionService? _permissionService;
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
        _permissionService = App.ServiceProvider?.GetService<IActionPermissionService>();

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
    /// Loads employees authorized for the given action via <see cref="IActionPermissionService"/>.
    /// Deny-by-default: when no permission rows exist, no users are shown.
    /// Administrators bypass action-level checks and are always included.
    /// </summary>
    private void LoadAuthorizedEmployees(ActionFollowUp followUp)
    {
        try
        {
            var actionCode = followUp.ToString();

            if (_permissionService != null)
            {
                // Use the centralized authorization service
                var authorizedUsers = _permissionService
                    .GetAuthorizedUsersForActionAsync(actionCode)
                    .GetAwaiter().GetResult();

                var employees = authorizedUsers.ToList();

                // Admin override — always include current user if they are admin
                var currentUser = CurrentUserContext.Instance;
                if (currentUser.IsAdmin && currentUser.CurrentUserId.HasValue)
                {
                    var adminId = currentUser.CurrentUserId.Value;
                    if (!employees.Any(e => e.Id == adminId))
                    {
                        // Admin not in authorized list — load from DB for display
                        var dbFactory = App.ServiceProvider?
                            .GetService<IDbContextFactory<SiNetSQLDbContext>>();
                        if (dbFactory != null)
                        {
                            using var ctx = dbFactory.CreateDbContext();
                            var adminUser = ctx.Siusers
                                .AsNoTracking()
                                .FirstOrDefault(u => u.Id == adminId && u.IsActive);
                            if (adminUser != null)
                                employees.Insert(0, adminUser);
                        }
                    }
                }

                _employees = new ObservableCollection<Siuser>(employees);
            }
            else
            {
                // Fallback: service not available — deny by default
                _employees = [];
            }

            EmployeeComboBox.ItemsSource = _employees;

            // Show message when no users are authorized
            if (_employees.Count == 0)
            {
                InfoText.Text = "אין משתמשים מורשים לפעולה זו. יש לפנות למנהל המערכת.";
                ExecuteNowButton.IsEnabled = false;
                CreateTaskButton.IsEnabled = false;
            }
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
        // Defense-in-depth: re-check that the current user is still authorized
        if (_permissionService != null && _currentUserId > 0)
        {
            var actionCode = _followUp.ToString();
            var isAllowed = _permissionService
                .IsUserAllowedForActionAsync(actionCode, _currentUserId)
                .GetAwaiter().GetResult();

            if (!isAllowed)
            {
                MessageBox.Show("ההרשאה שלך לפעולה זו בוטלה. לא ניתן לבצע.",
                    "גישה נדחתה", MessageBoxButton.OK, MessageBoxImage.Warning);
                ExecuteNowButton.IsEnabled = false;
                return;
            }
        }

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

        // Defense-in-depth: re-check that the selected employee is still authorized
        if (_permissionService != null)
        {
            var actionCode = _followUp.ToString();
            var isAllowed = _permissionService
                .IsUserAllowedForActionAsync(actionCode, selected.Id)
                .GetAwaiter().GetResult();

            if (!isAllowed)
            {
                MessageBox.Show($"העובד {selected.Name} אינו מורשה עוד לפעולה זו.",
                    "גישה נדחתה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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
