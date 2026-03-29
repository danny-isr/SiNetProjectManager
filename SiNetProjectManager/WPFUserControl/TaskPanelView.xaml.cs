using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.Services.EmailContext;
using SiNetProjectManager.Dialogs;
using SiNetProjectManager.WPF_Window;
using WpfSiData.WPFUserControl;

namespace SiNetProjectManager.WPFUserControl;

/// <summary>
/// Interaction logic for TaskPanelView.xaml
/// Employee-centric task management panel with inline editing.
/// </summary>
public partial class TaskPanelView : UserControl
{
    // Flag to prevent recursive save calls during UI updates
    private bool _isSaving;

    public TaskPanelView()
    {
        InitializeComponent();

        // Resolve ViewModel from DI container with required dependencies
        var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        DataContext = new TaskPanelViewModel(dbContextFactory);

        // Subscribe to ViewModel property changes to refresh star-column sizing after data loads
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Subscribe to email navigation requests
        ViewModel.NavigateToEmailRequested += OnNavigateToEmailRequested;

        // Subscribe to action dialog requests (tasks created from email actions)
        ViewModel.OpenActionRequested += OnOpenActionRequested;
    }

    /// <summary>
    /// Gets the ViewModel for external access.
    /// </summary>
    public TaskPanelViewModel ViewModel => (TaskPanelViewModel)DataContext;

    /// <summary>
    /// Handles the UserControl Loaded event.
    /// Queues an initial column refresh for after the first data bind completes.
    /// </summary>
    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStarColumnSizing();
    }

    /// <summary>
    /// When the Tasks collection changes (data loaded), force DataGrid to recalculate star-column widths.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskPanelViewModel.Tasks))
        {
            RefreshStarColumnSizing();
        }
    }

    /// <summary>
    /// Forces the DataGrid to recalculate star-sized column widths.
    /// Uses a two-phase dispatch:
    ///   Phase 1 (Loaded): Let the DataGrid complete layout with new data.
    ///   Phase 2 (ContextIdle): Force star column recalculation after all rendering is done.
    /// </summary>
    private void RefreshStarColumnSizing()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            TasksGrid.UpdateLayout();

            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                foreach (var column in TasksGrid.Columns)
                {
                    if (column.Width.IsStar)
                    {
                        column.Width = 0;
                        TasksGrid.UpdateLayout();
                        column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    }
                }

                TasksGrid.UpdateLayout();
            });
        });
    }

    /// <summary>
    /// Handles Status ComboBox selection change - saves immediately to DB.
    /// </summary>
    private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSaving) return;
        if (sender is not ComboBox combo) return;
        if (combo.Tag is not ProjectAssignment task) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ProjectAssignmentStatus newStatus) return;

        // Check if this is initial load (no removed items means first load)
        if (e.RemovedItems.Count == 0) return;

        // Get original status ID - could be from removed item or task
        int? oldStatusId;
        if (e.RemovedItems[0] is ProjectAssignmentStatus oldStatus)
        {
            oldStatusId = oldStatus.Id;
        }
        else
        {
            oldStatusId = task.StatusId;
        }

        // Skip if no actual change
        if (oldStatusId == newStatus.Id) return;

        System.Diagnostics.Debug.WriteLine($"Status change: {oldStatusId} -> {newStatus.Id} ({newStatus.Name})");

        // Detect Active → Waiting transition (ball leaving our court)
        bool wasActionable = task.AssignmentStatus?.IsActionable ?? false;
        bool willBeWaiting = newStatus.IsOpen && !newStatus.IsActionable;

        string? actionNote = null;
        if (wasActionable && willBeWaiting)
        {
            var dialog = new ActionProofDialog { Owner = Window.GetWindow(this) };
            var result = dialog.ShowDialog();

            if (result != true || !dialog.Confirmed)
            {
                // User cancelled — revert the status change
                ViewModel.RevertTaskInGrid(task);
                return;
            }

            actionNote = dialog.ActionNote;
        }

        _isSaving = true;
        try
        {
            ViewModel.UpdateTaskStatusInline(task, newStatus, oldStatusId, actionNote);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status change error: {ex}");
            throw;
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// Handles Project ComboBox selection change - saves immediately to DB.
    /// </summary>
    private void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSaving) return;
        if (sender is not ComboBox combo) return;
        if (combo.Tag is not ProjectAssignment task) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not Project newProject) return;

        // Check if this is initial load
        if (e.RemovedItems.Count == 0) return;

        // Get original project from removed items
        var oldProject = e.RemovedItems[0] as Project;
        var oldProjectId = oldProject?.Id ?? task.ProjectId;

        // Skip if no actual change
        if (oldProjectId == newProject.Id) return;

        _isSaving = true;
        try
        {
            ViewModel.UpdateTaskProjectInline(task, newProject, oldProjectId);
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// Handles DueDate DatePicker change - saves immediately to DB.
    /// </summary>
    private void DueDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSaving) return;
        if (sender is not DatePicker picker) return;
        if (picker.Tag is not ProjectAssignment task) return;

        // Get old date from removed items, new from added
        DateTime? oldDate = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as DateTime? : task.DueDate;
        DateTime? newDate = picker.SelectedDate;

        // Skip if no actual change
        if (oldDate == newDate) return;

        _isSaving = true;
        try
        {
            ViewModel.UpdateTaskDueDateInline(task, newDate, oldDate);
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// Handles navigation from a pending email link to the EmailManagement view.
    /// Navigates the MainWindow to EmailManagement and attempts to select the specific email.
    /// </summary>
    private void OnNavigateToEmailRequested(int emailId)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow == null) return;

        mainWindow.NavigateToEmail(emailId);
    }

    /// <summary>
    /// Handles opening the action dialog when the user clicks the action button on an action-sourced task.
    /// Routes to the correct dialog based on the <see cref="ActionFollowUp"/> stored in the task Body.
    /// </summary>
    private void OnOpenActionRequested(ActionFollowUp followUp, int emailMessageId)
    {
        var owner = Window.GetWindow(this);

        switch (followUp)
        {
            case ActionFollowUp.NewProjectDialog:
            {
                var mainWindow = owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
                if (mainWindow?.DataContext is MainWindowViewModel vm)
                {
                    vm.CurrentView = new CreateProjectUserControl();
                    mainWindow.Activate();
                }
                break;
            }

            case ActionFollowUp.TaskCreationDialog:
            {
                var tasksWindow = new FloatingProjectTasksView();
                if (owner != null) tasksWindow.Owner = owner;
                tasksWindow.Show();
                break;
            }

            case ActionFollowUp.DecisionDialog:
            {
                var decisionsWindow = new ProjectDecisionsWindow();
                if (owner != null) decisionsWindow.Owner = owner;
                decisionsWindow.Show();
                break;
            }

            default:
            {
                // For other action types, navigate to the linked email if available
                if (emailMessageId > 0)
                {
                    var mainWindow = owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
                    mainWindow?.NavigateToEmail(emailMessageId);
                }
                break;
            }
        }
    }
}
