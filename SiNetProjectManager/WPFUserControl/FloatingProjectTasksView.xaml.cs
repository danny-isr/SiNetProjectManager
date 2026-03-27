using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetProjectManager.Dialogs;

namespace SiNetProjectManager.WPFUserControl;

/// <summary>
/// Interaction logic for FloatingProjectTasksView.xaml
/// Floating ToolWindow showing tasks for the currently active project.
/// Persists window position via AppSettings/SettingsManager (JSON file).
/// </summary>
public partial class FloatingProjectTasksView : Window
{
    private bool _isSaving;
    private bool _isMouseOver;

    // Collapse/expand: store previous dimensions
    private double _expandedWidth;
    private double _expandedHeight;
    private double _expandedMinWidth;
    private double _expandedMinHeight;

    public FloatingProjectTasksView()
    {
        InitializeComponent();

        var viewModel = App.ServiceProvider.GetRequiredService<FloatingProjectTasksViewModel>();
        DataContext = viewModel;

        // Subscribe to ViewModel property changes for collapse handling
        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Subscribe to email navigation requests
        viewModel.NavigateToEmailRequested += OnNavigateToEmailRequested;

        // Apply opacity settings from AppSettings
        var settings = App.AppSettings;
        if (settings != null)
        {
            viewModel.ActiveOpacity = settings.FloatingWindowActiveOpacity;
            viewModel.IdleOpacity = settings.FloatingWindowIdleOpacity;
            settings.PropertyChanged += Settings_PropertyChanged;
        }

        ContentBorder.Opacity = viewModel.IdleOpacity;
    }

    /// <summary>
    /// Gets the ViewModel for external access.
    /// </summary>
    public FloatingProjectTasksViewModel ViewModel => (FloatingProjectTasksViewModel)DataContext;

    /// <summary>
    /// Restores saved window position on load.
    /// Falls back to CenterScreen if no saved position or if saved bounds are off-screen.
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = App.AppSettings;
        if (settings == null)
            return;

        var top = settings.FloatingTasksTop;
        var left = settings.FloatingTasksLeft;
        var width = settings.FloatingTasksWidth;
        var height = settings.FloatingTasksHeight;

        // Validate that we have a saved position (not NaN) and dimensions are reasonable
        if (!double.IsNaN(top) && !double.IsNaN(left) && width > 0 && height > 0)
        {
            // Ensure the window is at least partially visible on any monitor
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;

            if (left >= virtualLeft - width + 50 &&
                left <= virtualLeft + virtualWidth - 50 &&
                top >= virtualTop - height + 50 &&
                top <= virtualTop + virtualHeight - 50)
            {
                Top = top;
                Left = left;
                Width = width;
                Height = height;
                return;
            }
        }

        // Default: center on primary screen
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>
    /// Saves window position and size on closing.
    /// </summary>
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowPosition();
    }

    /// <summary>
    /// Disposes the ViewModel to unsubscribe from ActiveProjectContext.
    /// </summary>
    private void Window_Closed(object sender, EventArgs e)
    {
        // Unsubscribe from settings changes
        var settings = App.AppSettings;
        if (settings != null)
        {
            settings.PropertyChanged -= Settings_PropertyChanged;
        }

        if (DataContext is FloatingProjectTasksViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
            vm.NavigateToEmailRequested -= OnNavigateToEmailRequested;
            vm.Dispose();
        }
    }

    /// <summary>
    /// Reacts to ViewModel property changes — handles collapse/expand transitions.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FloatingProjectTasksViewModel.IsCollapsed))
            return;

        if (ViewModel.IsCollapsed)
            ApplyCollapsedState();
        else
            ApplyExpandedState();
    }

    /// <summary>
    /// Collapses the window to header-only: stores current dimensions, hides body, shrinks.
    /// </summary>
    private void ApplyCollapsedState()
    {
        // Store current dimensions for restore
        _expandedWidth = Width;
        _expandedHeight = Height;
        _expandedMinWidth = MinWidth;
        _expandedMinHeight = MinHeight;

        // Hide body rows
        FilterBar.Visibility = Visibility.Collapsed;
        QuickCreateBar.Visibility = Visibility.Collapsed;
        TaskListBox.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        StatusBarPanel.Visibility = Visibility.Collapsed;

        // Shrink to header-only compact size
        MinWidth = 200;
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        Width = Math.Min(Width, 260);
        ResizeMode = ResizeMode.NoResize;
    }

    /// <summary>
    /// Expands the window back to its previous full size and shows all body rows.
    /// </summary>
    private void ApplyExpandedState()
    {
        // Restore body rows
        FilterBar.Visibility = Visibility.Visible;
        QuickCreateBar.Visibility = Visibility.Visible;
        TaskListBox.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Visible;
        StatusBarPanel.Visibility = Visibility.Visible;

        // Restore dimensions
        SizeToContent = SizeToContent.Manual;
        MinWidth = _expandedMinWidth;
        MinHeight = _expandedMinHeight;
        Width = _expandedWidth;
        Height = _expandedHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
    }

    /// <summary>
    /// Persists current window bounds to AppSettings via SettingsManager.
    /// </summary>
    private void SaveWindowPosition()
    {
        var settings = App.AppSettings;
        if (settings == null)
            return;

        if (WindowState == WindowState.Normal && !ViewModel.IsCollapsed)
        {
            settings.FloatingTasksTop = Top;
            settings.FloatingTasksLeft = Left;
            settings.FloatingTasksWidth = Width;
            settings.FloatingTasksHeight = Height;
        }
        else if (ViewModel.IsCollapsed)
        {
            // Save position only; use stored expanded dimensions for size
            settings.FloatingTasksTop = Top;
            settings.FloatingTasksLeft = Left;
            if (_expandedWidth > 0) settings.FloatingTasksWidth = _expandedWidth;
            if (_expandedHeight > 0) settings.FloatingTasksHeight = _expandedHeight;
        }

        try
        {
            SettingsManager.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FloatingTasks] Failed to save window position: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles Status ComboBox selection change on task cards — saves immediately to DB.
    /// Follows the same _isSaving guard pattern as TaskPanelView.
    /// </summary>
    private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSaving) return;
        if (sender is not ComboBox combo) return;
        if (combo.Tag is not ProjectAssignment task) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ProjectAssignmentStatus newStatus) return;

        // Initial load — no removed items means first population
        if (e.RemovedItems.Count == 0) return;

        // Get original status ID
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

        // Detect Active → Waiting transition (ball leaving our court)
        bool wasActionable = task.AssignmentStatus?.IsActionable ?? false;
        bool willBeWaiting = newStatus.IsOpen && !newStatus.IsActionable;

        string? actionNote = null;
        if (wasActionable && willBeWaiting)
        {
            var dialog = new ActionProofDialog { Owner = this };
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
            System.Diagnostics.Debug.WriteLine($"[FloatingTasks] Status change error: {ex}");
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// Reacts to AppSettings changes (from SettingsWindow sliders) in real time.
    /// Updates ViewModel opacity and animates the ContentBorder to the correct value.
    /// </summary>
    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppSettings.FloatingWindowActiveOpacity)
                              or nameof(AppSettings.FloatingWindowIdleOpacity)))
            return;

        var settings = App.AppSettings;
        if (settings == null) return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Settings_PropertyChanged(sender, e));
            return;
        }

        ViewModel.ActiveOpacity = settings.FloatingWindowActiveOpacity;
        ViewModel.IdleOpacity = settings.FloatingWindowIdleOpacity;
        AnimateOpacity(_isMouseOver ? ViewModel.ActiveOpacity : ViewModel.IdleOpacity);
    }

    /// <summary>
    /// Fades to active (fully visible) opacity when the mouse enters the window.
    /// </summary>
    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOver = true;
        AnimateOpacity(ViewModel.ActiveOpacity);
    }

    /// <summary>
    /// Fades to idle (semi-transparent) opacity when the mouse leaves the window.
    /// </summary>
    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOver = false;
        AnimateOpacity(ViewModel.IdleOpacity);
    }

    /// <summary>
    /// Smoothly animates the window opacity to the target value over 0.3 seconds.
    /// </summary>
    private void AnimateOpacity(double targetOpacity)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        ContentBorder.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// Enables dragging the window from the custom header.
    /// </summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    /// <summary>
    /// Closes the floating window via the custom close button.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
