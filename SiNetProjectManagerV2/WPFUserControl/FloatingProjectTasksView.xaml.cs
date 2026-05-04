using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetProjectManagerV2.Dialogs;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Interaction logic for FloatingProjectTasksView.xaml
/// Floating ToolWindow showing tasks for the currently active project.
/// Inherits shared behavior (collapse, drag, opacity, position persistence) from <see cref="FloatingWindowBase"/>.
/// </summary>
public partial class FloatingProjectTasksView : FloatingWindowBase
{
    private bool _isSaving;

    public FloatingProjectTasksView()
    {
        InitializeComponent();

        var viewModel = App.ServiceProvider.GetRequiredService<FloatingProjectTasksViewModel>();
        DataContext = viewModel;

        // Derived-specific subscription
        viewModel.NavigateToEmailRequested += OnNavigateToEmailRequested;
        viewModel.OpenWorkflowTaskRequested += OnOpenWorkflowTaskRequested;

        // Initialize common floating behavior (opacity, settings, collapse)
        InitializeFloatingBehavior();
    }

    /// <summary>Gets the ViewModel for external access.</summary>
    public FloatingProjectTasksViewModel ViewModel => (FloatingProjectTasksViewModel)DataContext;

    #region FloatingWindowBase Overrides

    protected override IFloatingWindowViewModel FloatingViewModel => ViewModel;
    protected override FrameworkElement OpacityTarget => ContentBorder;
    protected override string LogPrefix => "[FloatingTasks]";

    protected override (double Top, double Left, double Width, double Height)
        ReadWindowPosition(AppSettings settings) =>
        (settings.FloatingTasksTop, settings.FloatingTasksLeft,
         settings.FloatingTasksWidth, settings.FloatingTasksHeight);

    protected override void WriteWindowPosition(
        AppSettings settings, double top, double left, double width, double height)
    {
        settings.FloatingTasksTop = top;
        settings.FloatingTasksLeft = left;
        settings.FloatingTasksWidth = width;
        settings.FloatingTasksHeight = height;
    }

    protected override void OnBodyCollapsed()
    {
        FilterBar.Visibility = Visibility.Collapsed;
        QuickCreateBar.Visibility = Visibility.Collapsed;
        TaskListBox.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        StatusBarPanel.Visibility = Visibility.Collapsed;
    }

    protected override void OnBodyExpanded()
    {
        FilterBar.Visibility = Visibility.Visible;
        QuickCreateBar.Visibility = Visibility.Visible;
        TaskListBox.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Visible;
        StatusBarPanel.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is FloatingProjectTasksViewModel vm)
        {
            vm.NavigateToEmailRequested -= OnNavigateToEmailRequested;
            vm.OpenWorkflowTaskRequested -= OnOpenWorkflowTaskRequested;
        }

        base.OnClosed(e);
    }

    #endregion

    #region Domain-Specific Handlers

    /// <summary>
    /// Handles Status ComboBox selection change on task cards — saves immediately to DB.
    /// </summary>
    private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSaving) return;
        if (sender is not ComboBox combo) return;

        var result = Helpers.TaskGridEventHelper.ProcessStatusChange(
            e, combo, this,
            out var task, out var newStatus, out var oldStatusId, out var actionNote, out var taskResultId);

        if (result == Helpers.TaskGridEventHelper.StatusChangeResult.NoChange)
            return;

        if (result == Helpers.TaskGridEventHelper.StatusChangeResult.Cancelled)
        {
            ViewModel.RevertTaskInGrid(task!);
            return;
        }

        _isSaving = true;
        try
        {
            ViewModel.UpdateTaskStatusInline(task!, newStatus!, oldStatusId, actionNote, taskResultId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FloatingTasks] Status change error: {ex}");
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// Opens the Task Import window, passing the current active project context.
    /// </summary>
    private void ImportTsvButton_Click(object sender, RoutedEventArgs e)
    {
        var project = ViewModel.ActiveProject;
        var importWindow = new TaskImportWindow(
            activeProjectId: project?.Id,
            activeProjectDisplay: ViewModel.ActiveProjectDisplay);
        importWindow.Owner = this;
        importWindow.ShowDialog();

        // Refresh tasks after import to reflect newly imported items
        ViewModel.RefreshCommand.Execute(null);
    }

    /// <summary>
    /// Handles navigation from a pending email link to the EmailManagement view.
    /// Routes through the MainWindow (Owner) which hosts the main content area.
    /// </summary>
    private void OnNavigateToEmailRequested(int emailId)
    {
        var mainWindow = Owner as MainWindow;
        mainWindow?.NavigateToEmail(emailId);
        mainWindow?.Activate();
    }

    /// <summary>
    /// Handles workflow task action: routes to the appropriate dialog based on stage code.
    /// </summary>
    private void OnOpenWorkflowTaskRequested(int emailId, string stageCode)
    {
        var mainWindow = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;

        switch (stageCode)
        {
            case "CreateProject":
                // Combined window: email preview + project creation in one dialog
                var createWindow = new Dialogs.WorkflowCreateProjectWindow(
                    emailId, mainWindow ?? Application.Current.MainWindow);
                createWindow.ShowDialog();
                break;

            case "FileAttachments":
                // Open a dedicated window with this specific email + its attachments
                // so the user can file the attachments without leaving the task context.
                var fileWindow = new Dialogs.EmailPreviewWindow(emailId)
                {
                    Owner = mainWindow ?? Application.Current.MainWindow,
                    Title = $"📎 תיוק קבצים — מייל #{emailId}"
                };
                fileWindow.Show();
                break;

            default:
                // Fallback: navigate to the source email in the inbox
                mainWindow?.NavigateToEmail(emailId);
                mainWindow?.Activate();
                break;
        }
    }

    #endregion

    #region Priority Inline Editing

    /// <summary>
    /// Allows only digits in the priority TextBox.
    /// </summary>
    private void PriorityTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Commits the priority change when Enter is pressed.
    /// </summary>
    private void PriorityTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            CommitPriorityChange(tb);
            e.Handled = true;

            // Move focus away so LostFocus doesn't fire again
            Keyboard.ClearFocus();
        }
        else if (e.Key == Key.Escape && sender is TextBox escTb)
        {
            // Revert to original value
            if (escTb.Tag is ProjectAssignment task)
            {
                escTb.Text = task.WorkPriority?.ToString() ?? "";
            }
            Keyboard.ClearFocus();
        }
    }

    /// <summary>
    /// Commits the priority change when the TextBox loses focus.
    /// </summary>
    private void PriorityTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            CommitPriorityChange(tb);
        }
    }

    /// <summary>
    /// Parses the new priority value and calls the ViewModel to reorder if changed.
    /// </summary>
    private void CommitPriorityChange(TextBox textBox)
    {
        if (textBox.Tag is not ProjectAssignment task) return;
        if (!int.TryParse(textBox.Text, out var newPriority) || newPriority < 1)
        {
            // Revert to current value on invalid input
            textBox.Text = task.WorkPriority?.ToString() ?? "";
            return;
        }

        if (task.WorkPriority == newPriority) return;

        try
        {
            ViewModel.UpdateTaskPriority(task, newPriority);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FloatingTasks] Priority change error: {ex}");
            textBox.Text = task.WorkPriority?.ToString() ?? "";
        }
    }

    #endregion
}
