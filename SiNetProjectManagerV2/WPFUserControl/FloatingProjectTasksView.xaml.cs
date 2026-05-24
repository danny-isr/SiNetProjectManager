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
        // LEGACY DISABLED 2026-05-21: subscription to viewModel.OpenWorkflowTaskRequested removed.
        // Reason: stage-code based open path is no longer the open mechanism. New path:
        // OpenSelectedTaskCommand → TaskNavigationResolver → OpenTaskNavigationRequested.
        // Phase: workflow/task navigation cleanup. Candidate for deletion after validation.
        // viewModel.OpenWorkflowTaskRequested += OnOpenWorkflowTaskRequested;
        viewModel.OpenTaskNavigationRequested += OnOpenTaskNavigationRequested;

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
            // LEGACY DISABLED 2026-05-21: matching unsubscribe removed (handler no longer attached).
            // vm.OpenWorkflowTaskRequested -= OnOpenWorkflowTaskRequested;
            vm.OpenTaskNavigationRequested -= OnOpenTaskNavigationRequested;
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
    /// LEGACY DISABLED 2026-05-21: Stage-code based open handler. No longer wired to the
    /// view model event (subscription removed in the constructor). Kept as a stub so any
    /// stray reference compiles but does nothing. Reason: Proposal/Opinion now use
    /// WorkflowStageTask templates with TaskType + ComponentKey. New path:
    /// OnOpenTaskNavigationRequested(TaskNavigationRequest). Phase: workflow/task
    /// navigation cleanup. Candidate for deletion after validation.
    /// </summary>
    private void OnOpenWorkflowTaskRequested(int emailId, string stageCode)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[FloatingTasksView] LEGACY DISABLED 2026-05-21: OnOpenWorkflowTaskRequested called " +
            $"(emailId={emailId}, stageCode='{stageCode}'). This handler is no longer the open path. " +
            $"Use TaskNavigationResolver / OnOpenTaskNavigationRequested instead.");
    }

    /// <summary>
    /// Resolver-driven open handler. Selects the host view based on
    /// <see cref="SiNetSQL.Services.Tasks.TaskNavigationRequest.ComponentKey"/>.
    /// Keeps existing legacy behavior intact for tasks that fall through to
    /// <see cref="OnOpenWorkflowTaskRequested(int, string)"/>.
    /// </summary>
    private void OnOpenTaskNavigationRequested(SiNetSQL.Services.Tasks.TaskNavigationRequest request)
    {
        var mainWindow = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        var primaryEmailId = (int?)request.PrimaryWorkTargetEntityId;

        System.Diagnostics.Debug.WriteLine(
            $"[FloatingTasksView] OnOpenTaskNavigationRequested received. TaskId={request.TaskId}, " +
            $"ComponentKey={request.ComponentKey}, OpenMode={request.OpenMode}, ProjectId={request.ProjectId}, " +
            $"PrimaryWorkTargetEntityId={request.PrimaryWorkTargetEntityId}, MainWindow={(mainWindow != null ? "ok" : "null")}");

        switch (request.ComponentKey)
        {
            case SiNetSQL.Services.Tasks.TaskComponentKeys.ProjectCreationFromEmail:
            case SiNetSQL.Services.Tasks.TaskComponentKeys.ReviewProjectSetupFromEmail:
                if (primaryEmailId is int emailIdForCreate)
                {
                    // Pass the task context so the combined window can drive
                    // MoveToProject after project creation and report task
                    // completion through the existing coordinator path
                    // (event ReviewMaterialFiled). Without this context the
                    // window behaves in standalone mode and the originating
                    // task would never close.
                    var workTargetEmailIdsCreate = request.WorkTargetIds
                        .Select(id => (int)id)
                        .ToList();
                    var pendingWorkTargetEmailIdsCreate = request.PendingWorkTargetIds
                        .Select(id => (int)id)
                        .ToList();

                    var createTaskContext = new SiNetSQL.Services.Tasks.EmailFilingTaskContext(
                        TaskId: request.TaskId,
                        ComponentKey: request.ComponentKey,
                        WorkTargetEmailIds: workTargetEmailIdsCreate,
                        PendingWorkTargetEmailIds: pendingWorkTargetEmailIdsCreate,
                        PrimaryWorkTargetEmailId: emailIdForCreate,
                        OnTaskRefreshRequested: () =>
                        {
                            try { ViewModel.RefreshCommand.Execute(null); }
                            catch { /* best-effort UI refresh */ }
                        },
                        ActiveTaskProjectId: request.ProjectId);

                    var createWindow = new Dialogs.WorkflowCreateProjectWindow(
                        emailIdForCreate,
                        createTaskContext,
                        mainWindow ?? Application.Current.MainWindow);
                    createWindow.ShowDialog();
                }
                break;

            case SiNetSQL.Services.Tasks.TaskComponentKeys.EmailFiling:
                if (primaryEmailId is null)
                {
                    // EmailFiling requires a primary email work target. Surface clearly
                    // instead of silently no-op (per workflow/task navigation cleanup rules).
                    System.Diagnostics.Debug.WriteLine(
                        $"[FloatingTasksView] EmailFiling task {request.TaskId} (TaskTypeCode={request.TaskTypeCode}) has no PrimaryWorkTargetEntityId. " +
                        $"Cannot open EmailFiling host. ProjectId={request.ProjectId}. " +
                        $"This indicates a data/seed issue: the task is mapped to ComponentKey=EmailFiling but has no email work target.");
                    MessageBox.Show(
                        $"לא ניתן לפתוח את המשימה (TaskId={request.TaskId}, סוג={request.TaskTypeCode}).\n" +
                        $"המשימה מסווגת כסיווג מייל (EmailFiling) אך אינה משויכת למייל.\n" +
                        $"יש לבדוק את הגדרת סוג המשימה ב-ReviewTaskInteractionRegistry או את נתוני המשימה.",
                        "שגיאת ניווט משימה",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    break;
                }
                if (primaryEmailId is int emailIdForFiling && mainWindow != null)
                {
                    // Same canonical flow as the inbox email tagging path —
                    // never open a separate preview-only window for filing.
                    // Pass the task context so the email VM can call the
                    // central TaskCompletionCoordinator after a successful
                    // MoveToProject run (event: ReviewMaterialFiled).
                    var workTargetEmailIds = request.WorkTargetIds
                        .Select(id => (int)id)
                        .ToList();
                    var pendingWorkTargetEmailIds = request.PendingWorkTargetIds
                        .Select(id => (int)id)
                        .ToList();

                    var taskContext = new SiNetSQL.Services.Tasks.EmailFilingTaskContext(
                        TaskId: request.TaskId,
                        ComponentKey: request.ComponentKey,
                        WorkTargetEmailIds: workTargetEmailIds,
                        PendingWorkTargetEmailIds: pendingWorkTargetEmailIds,
                        PrimaryWorkTargetEmailId: emailIdForFiling,
                        OnTaskRefreshRequested: () =>
                        {
                            try { ViewModel.RefreshCommand.Execute(null); }
                            catch { /* best-effort UI refresh */ }
                        },
                        ActiveTaskProjectId: request.ProjectId);

                    mainWindow.NavigateToEmail(emailIdForFiling, taskContext);
                    mainWindow.Activate();
                }
                break;

            default:
                // Classification-only tasks (e.g. IdentifyQuoteRequest) ride the
                // ProjectWork host slot but have no dedicated screen — they simply
                // need the operator to pick one of the allowed task results and
                // close via ITaskCompletionCoordinator. Detect this case by the
                // resolver-supplied AllowedTaskResultCodes and route to the
                // shared TaskResultPickerDialog instead of silently no-oping.
                if (request.AllowedTaskResultCodes.Count > 0
                    && request.CompletionPolicy == SiNetSQL.Services.Tasks.TaskCompletionPolicy.WorkflowResultRecorded)
                {
                    var completionEventCode = ResolveClassificationCompletionEventCode(request.TaskTypeCode);
                    if (!string.IsNullOrEmpty(completionEventCode))
                    {
                        string? pickedCode = null;

                        // IdentifyQuoteRequest gets a dedicated host that shows the
                        // source email + attachments so the operator can classify
                        // with full context. Other classification tasks fall back
                        // to the generic result picker.
                        if (request.TaskTypeCode == SiNetSQL.Constants.TaskTypeCodes.IdentifyQuoteRequest
                            && primaryEmailId is int classificationEmailId)
                        {
                            var dialog = new Dialogs.QuoteClassificationDialog(classificationEmailId)
                            {
                                Owner = mainWindow ?? Application.Current.MainWindow
                            };
                            if (dialog.ShowDialog() == true)
                            {
                                pickedCode = dialog.SelectedResultCode;
                            }
                        }
                        else
                        {
                            var picker = new Dialogs.TaskResultPickerDialog(
                                taskTypeId: null,
                                allowedCodes: request.AllowedTaskResultCodes,
                                promptText: BuildClassificationPrompt(request.TaskTypeCode))
                            {
                                Owner = mainWindow ?? Application.Current.MainWindow
                            };
                            if (picker.ShowDialog() == true)
                            {
                                pickedCode = picker.SelectedResult?.Code;
                            }
                        }

                        if (!string.IsNullOrEmpty(pickedCode))
                        {
                            _ = ViewModel.CompleteClassificationTaskAsync(request.TaskId, completionEventCode, pickedCode);
                        }
                        break;
                    }
                }

                // No specialized host yet — surface this clearly instead of silently no-op.
                System.Diagnostics.Debug.WriteLine(
                    $"[FloatingTasksView] No specialized host for ComponentKey='{request.ComponentKey}'. " +
                    $"OpenMode={request.OpenMode}, ProjectId={request.ProjectId}, EmailId={primaryEmailId}. " +
                    $"Activating MainWindow only.");
                if (request.ProjectId is int projectId)
                {
                    mainWindow?.Activate();
                }
                else if (primaryEmailId is int fallbackEmailId)
                {
                    mainWindow?.NavigateToEmail(fallbackEmailId);
                    mainWindow?.Activate();
                }
                else
                {
                    mainWindow?.Activate();
                }
                break;
        }
    }

    /// <summary>
    /// Maps a classification task type code to the canonical completion event it
    /// reports. Returns null when the task type is not a classification task.
    /// </summary>
    private static string? ResolveClassificationCompletionEventCode(string taskTypeCode) =>
        taskTypeCode switch
        {
            SiNetSQL.Constants.TaskTypeCodes.IdentifyQuoteRequest =>
                SiNetSQL.Services.Tasks.ReviewCompletionEvents.ReviewQuoteRequestClassified,
            _ => null,
        };

    /// <summary>Returns a Hebrew prompt explaining the choice for a classification task.</summary>
    private static string BuildClassificationPrompt(string taskTypeCode) => taskTypeCode switch
    {
        SiNetSQL.Constants.TaskTypeCodes.IdentifyQuoteRequest =>
            "האם המייל מהווה פנייה להצעת מחיר? בחר תוצאה כדי לסגור את המשימה.",
        _ => "בחר את תוצאת המשימה כדי לסגור אותה.",
    };

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
