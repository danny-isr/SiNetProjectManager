using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManager.Dialogs;
using SiNetProjectManager.Services;
using SiNetProjectManager.WPF_Window;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.Services.EmailContext;
using SiOffice.GoogleConnector;
using WpfSiData.WPFUserControl;

namespace SiNetProjectManager.WPFUserControl
{
    /// <summary>
    /// Interaction logic for EmailManagementView.xaml
    /// </summary>
    public partial class EmailManagementView : UserControl
    {
        private EmailManagementViewModel? _subscribedVm;
        private WebView2PdfRenderer? _pdfRenderer;
        private ExternalBrowserWindow? _accViewerWindow;
        private EmailContextViewModel? _emailContextVm;

        public EmailManagementView()
        {
            InitializeComponent();

            // Cleanup WebView2 state when control is unloaded
            Unloaded += OnUnloaded;
            Loaded += OnLoaded;

            // Phase 1: Bridge VM → WebView2Helper for identity sync
            DataContextChanged += OnDataContextChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Register the live email WebView2 with the PDF renderer for WYSIWYG capture
            if (EmailWebView != null)
            {
                _pdfRenderer = App.ServiceProvider?.GetService<WebView2PdfRenderer>();
                _pdfRenderer?.RegisterLiveView(EmailWebView);
            }

            // Initialize Email Context Panel via DI
            if (EmailContextPanel != null && _emailContextVm == null)
            {
                _emailContextVm = App.ServiceProvider?.GetService<EmailContextViewModel>();
                if (_emailContextVm != null)
                {
                    EmailContextPanel.DataContext = _emailContextVm;
                    _emailContextVm.FollowUpRequested = HandleFollowUpAsync;
                }
            }

            // Initialize calendar sidebar if user is already authenticated
            // (e.g., returning to this tab after navigating away)
            if (CalendarWebView != null && !string.IsNullOrEmpty(_subscribedVm?.ConnectedEmail))
            {
                await WebView2Helper.InitializeCalendarAsync(CalendarWebView);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from previous VM
            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
                _subscribedVm.OpenAccViewerRequested = null;
                _subscribedVm.OversizedFileConfirmRequested = null;
                WebView2Helper.ProjectFileDownloaded -= OnProjectFileDownloaded;
                _subscribedVm = null;
            }

            // Subscribe to new VM
            if (e.NewValue is EmailManagementViewModel vm)
            {
                _subscribedVm = vm;
                vm.PropertyChanged += OnViewModelPropertyChanged;

                // Wire up ACC viewer popup callback
                vm.OpenAccViewerRequested = OpenAccViewerPopup;

                // Wire up oversized file confirmation dialog
                vm.OversizedFileConfirmRequested = OnOversizedFileConfirm;
                vm.MaxUploadFileSizeBytes = AppConfiguration.MaxUploadFileSizeBytes;

                // Wire up external download → ACC upload pipeline
                WebView2Helper.ProjectFileDownloaded += OnProjectFileDownloaded;

                // Sync initial state if already logged in
                if (!string.IsNullOrEmpty(vm.ConnectedEmail))
                {
                    WebView2Helper.CurrentUserEmail = vm.ConnectedEmail;
                }
            }
        }

        private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not EmailManagementViewModel vm) return;

            switch (e.PropertyName)
            {
                case nameof(EmailManagementViewModel.ConnectedEmail):
                    // Identity sync + OAuth session bridge: after system-browser auth,
                    // inject session hints into WebView2 and attempt Gmail navigation
                    if (!string.IsNullOrEmpty(vm.ConnectedEmail) && EmailWebView != null)
                    {
                        await WebView2Helper.InjectOAuthSessionAsync(EmailWebView, vm.ConnectedEmail);
                    }
                    else if (!string.IsNullOrEmpty(vm.ConnectedEmail))
                    {
                        WebView2Helper.CurrentUserEmail = vm.ConnectedEmail;
                    }
                    // Initialize calendar sidebar after authentication
                    // (shares the same profile so session cookies are reused)
                    if (!string.IsNullOrEmpty(vm.ConnectedEmail) && CalendarWebView != null)
                    {
                        await WebView2Helper.InitializeCalendarAsync(CalendarWebView);
                    }
                    break;

                case nameof(EmailManagementViewModel.IsAuthenticated):
                    // Logout sync: clear WebView2 browsing data when user logs out
                    if (!vm.IsAuthenticated && EmailWebView != null)
                    {
                        await WebView2Helper.ClearSessionAsync(EmailWebView);
                        WebView2Helper.CurrentUserEmail = null;
                    }
                    break;

                case nameof(EmailManagementViewModel.IsCalendarVisible):
                    // When toggled visible, reset the calendar to today's day view.
                    // DOMContentLoaded re-injects CalendarCleanViewJs automatically.
                    if (vm.IsCalendarVisible && CalendarWebView?.CoreWebView2 != null)
                    {
                        CalendarWebView.CoreWebView2.Navigate(WebView2Helper.CalendarDayViewUrl);
                    }
                    break;

                case nameof(EmailManagementViewModel.SelectedEmail):
                    // Trigger Email Context analysis when a new email is selected
                    if (_emailContextVm != null)
                    {
                        var sel = vm.SelectedEmail;
                        if (sel != null && !string.IsNullOrEmpty(sel.MessageId))
                        {
                            _ = _emailContextVm.SetEmailByGmailIdAsync(
                                sel.MessageId,
                                sel.Subject,
                                sel.From,
                                sel.ParsedDate != DateTime.MinValue ? sel.ParsedDate : null);
                        }
                        else
                        {
                            _emailContextVm.Clear();
                        }
                    }
                    break;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from VM to prevent memory leaks
            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
                _subscribedVm.OpenAccViewerRequested = null;
                _subscribedVm.OversizedFileConfirmRequested = null;
                WebView2Helper.ProjectFileDownloaded -= OnProjectFileDownloaded;
                _subscribedVm = null;
            }

            // Disconnect follow-up callback
            if (_emailContextVm != null)
            {
                _emailContextVm.FollowUpRequested = null;
            }

            // Unregister live view from PDF renderer
            _pdfRenderer?.UnregisterLiveView();

            // Cleanup the WebView2 helper state to prevent memory leaks
            if (EmailWebView != null)
            {
                WebView2Helper.CleanupWebView(EmailWebView);
            }
            if (CalendarWebView != null)
            {
                WebView2Helper.CleanupWebView(CalendarWebView);
            }
        }

        /// <summary>
        /// Handles ActionFollowUp requests from the EmailContextViewModel.
        /// Delegatable actions (task creation, new project, decisions) are routed
        /// through <see cref="AssignActionDialog"/> so the user can choose to
        /// execute directly or create a task for another employee.
        /// Utility actions (file import, project picker, workflow advance) remain direct.
        /// </summary>
        private async Task HandleFollowUpAsync(ActionFollowUp followUp, ActionResult result, int emailMessageId)
        {
            var owner = Window.GetWindow(this);

            switch (followUp)
            {
                // ─── Utility actions — always direct ───
                case ActionFollowUp.FileImportDialog:
                    var importDialog = new FileImportDialog(emailMessageId) { Owner = owner };
                    importDialog.ShowDialog();
                    break;

                case ActionFollowUp.WorkflowAdvanceDialog:
                    if (result.OutputData.TryGetValue("WorkflowInstanceId", out var wfIdObj) && wfIdObj is int instanceId)
                    {
                        var wfWindow = new WorkflowInstanceWindow(instanceId) { Owner = owner };
                        wfWindow.Show();
                    }
                    break;

                case ActionFollowUp.ProjectPicker:
                    await HandleProjectPickerAsync(owner, emailMessageId);
                    break;

                // ─── Delegatable actions — go through AssignActionDialog ───
                case ActionFollowUp.NewProjectDialog:
                case ActionFollowUp.TaskCreationDialog:
                case ActionFollowUp.DecisionDialog:
                case ActionFollowUp.DisciplineDialog:
                    await HandleDelegatableActionAsync(owner, followUp, result, emailMessageId);
                    break;

                default:
                    MessageBox.Show(
                        result.Message,
                        "פעולה נדרשת", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }

        /// <summary>
        /// Shows the <see cref="AssignActionDialog"/> for a delegatable action.
        /// If the user chooses "Execute Now", routes to the direct dialog.
        /// If the user chooses "Create Task", creates a <see cref="ProjectAssignment"/>
        /// with a <see cref="TaskLink"/> pointing to the source email.
        /// </summary>
        private async Task HandleDelegatableActionAsync(
            Window? owner,
            ActionFollowUp followUp,
            ActionResult result,
            int emailMessageId)
        {
            var assignDialog = new AssignActionDialog(result.Message, followUp);
            if (owner != null) assignDialog.Owner = owner;

            if (assignDialog.ShowDialog() != true || assignDialog.AssignResult is not { } assign)
                return;

            if (assign.ExecuteDirectly)
            {
                // User chose to do it themselves — open the original dialog
                ExecuteDirectAction(owner, followUp, emailMessageId);
                return;
            }

            if (assign is { CreateTask: true, SelectedEmployee: not null })
            {
                await CreateActionTaskAsync(
                    assign.SelectedEmployee,
                    followUp,
                    result.Message,
                    emailMessageId,
                    assign.Note,
                    CancellationToken.None);
            }
        }

        /// <summary>
        /// Opens the appropriate dialog for direct execution (no delegation).
        /// </summary>
        private void ExecuteDirectAction(Window? owner, ActionFollowUp followUp, int emailMessageId)
        {
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
            }
        }

        /// <summary>
        /// Creates a <see cref="ProjectAssignment"/> for the selected employee
        /// and links it to the source <see cref="EmailInboxMessage"/> via <see cref="TaskLink"/>.
        /// The task Body stores the action context so the assignee can open the right dialog.
        /// </summary>
        private async Task CreateActionTaskAsync(
            Siuser assignee,
            ActionFollowUp followUp,
            string actionDescription,
            int emailMessageId,
            string? note,
            CancellationToken ct)
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            var currentUserId = SiNetSQL.Services.CurrentUserContext.Instance.CurrentUserId ?? 0;

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Resolve the project from the email (may be null for unassociated emails)
            var emailProjectId = await db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.Id == emailMessageId)
                .Select(m => (int?)m.ProjectId)
                .FirstOrDefaultAsync(ct);

            // Get the first active task type as default (TODO: map action types to specific task types)
            var taskType = await db.TaskTypes
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .FirstOrDefaultAsync(ct);

            // Get the default actionable status
            var openStatus = await db.ProjectAssignmentStatuses
                .FirstOrDefaultAsync(s => s.IsActionable, ct);

            if (openStatus == null)
            {
                MessageBox.Show("לא נמצא סטטוס פעיל במערכת.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build task body with action context for the assignee
            var bodyParts = new List<string>
            {
                $"[פעולה: {actionDescription}]",
                $"[סוג: {followUp}]",
                $"[מייל: #{emailMessageId}]",
            };
            if (!string.IsNullOrWhiteSpace(note))
                bodyParts.Add(note);

            var task = new ProjectAssignment
            {
                ProjectId = emailProjectId,
                AssignedToId = assignee.Id,
                TaskTypeId = taskType?.Id,
                StatusId = openStatus.Id,
                Title = $"{actionDescription} — מייל #{emailMessageId}",
                Body = string.Join(Environment.NewLine, bodyParts),
                Created = DateTime.Now,
                AuthorId = currentUserId > 0 ? currentUserId : null,
            };

            db.ProjectAssignments.Add(task);
            await db.SaveChangesAsync(ct);

            // Link the task to the source email
            var link = new TaskLink
            {
                TaskId = task.Id,
                LinkedEntityType = TaskLinkEntityType.EmailInboxMessage,
                LinkedEntityId = emailMessageId,
                Role = TaskLinkRole.Trigger,
                Description = actionDescription,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUserId > 0 ? currentUserId : assignee.Id,
            };

            db.TaskLinks.Add(link);
            await db.SaveChangesAsync(ct);

            MessageBox.Show(
                $"✅ משימה נוצרה עבור {assignee.Name}",
                "משימה נוצרה", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Opens the ProjectSelectorDialog. When the user picks a project,
        /// updates the email's ProjectId in the DB and re-triggers context analysis.
        /// </summary>
        private async Task HandleProjectPickerAsync(Window? owner, int emailMessageId)
        {
            var dialog = new ProjectSelectorDialog();
            if (owner != null) dialog.Owner = owner;

            if (dialog.ShowDialog() == true && dialog.SelectedProject is { } project)
            {
                var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
                if (dbFactory != null)
                {
                    await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
                    var msg = await db.EmailInboxMessages.FindAsync(emailMessageId);
                    if (msg != null)
                    {
                        msg.ProjectId = project.Id;
                        msg.UpdatedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                }

                // Re-analyze after association so context chips update
                if (_emailContextVm != null)
                {
                    await _emailContextVm.SetEmailMessageAsync(emailMessageId);
                }
            }
        }

        /// <summary>
        /// Opens or reuses a singleton floating browser window to display the attachment in ACC Docs viewer.
        /// If the window is already open it navigates to the new URL; otherwise creates a new instance.
        /// Called from the ViewModel via the <see cref="EmailManagementViewModel.OpenAccViewerRequested"/> callback.
        /// </summary>
        private void OpenAccViewerPopup(string url, string title)
        {
            // Reuse existing window if it's still alive
            if (_accViewerWindow is { IsLoaded: true })
            {
                _accViewerWindow.NavigateTo(url, title);
                _accViewerWindow.Activate();
                return;
            }

            _accViewerWindow = new ExternalBrowserWindow(url, _subscribedVm?.SelectedEmail)
            {
                Title = title,
                Width = 1200,
                Height = 800
            };
            _accViewerWindow.Closed += (_, _) => _accViewerWindow = null;
            _accViewerWindow.Show();
        }

        /// <summary>
        /// Handles the <see cref="WebView2Helper.ProjectFileDownloaded"/> event.
        /// Forwards to the ViewModel for ACC upload on the UI dispatcher.
        /// </summary>
        private void OnProjectFileDownloaded(string localPath, string fileName, EmailInfo emailInfo)
        {
            if (_subscribedVm == null) return;

            Dispatcher.InvokeAsync(async () =>
            {
                await _subscribedVm.HandleExternalFileDownloadedAsync(localPath, fileName, emailInfo);
            });
        }

        /// <summary>
        /// Shows a confirmation dialog when a file exceeds the configured size limit.
        /// Returns true if the user approves uploading anyway, false to skip.
        /// </summary>
        private bool OnOversizedFileConfirm(string fileName, long fileSizeBytes, long limitMb)
        {
            var sizeMb = fileSizeBytes / (1024.0 * 1024.0);
            var result = MessageBox.Show(
                $"הקובץ \"{fileName}\" גדול מ-{limitMb} MB ({sizeMb:F1} MB).\n\n" +
                $"ניתן לשנות את הגבלת הגודל בהגדרות (MaxUploadFileSizeMb).\n\n" +
                $"האם להעלות בכל זאת ל-ACC?",
                "קובץ גדול",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        /// <summary>
        /// Left-click on an attachment chip opens it in ACC viewer.
        /// Only fires when the click didn't originate from a child control (ComboBox, etc.).
        /// </summary>
        private void AttachmentBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;

            if (sender is not FrameworkElement fe) return;

            if (_subscribedVm?.ShowAttachmentInAccCommand is { } cmd
                && cmd.CanExecute(fe.DataContext))
            {
                cmd.Execute(fe.DataContext);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Converter to check if a group header name starts with "Assigned" or "?? Assigned"
    /// </summary>
    public class StartsWithAssignedConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string text)
                {
                    // Check for both plain "Assigned" and emoji-prefixed "?? Assigned"
                    return text.StartsWith("Assigned", StringComparison.OrdinalIgnoreCase) ||
                           text.StartsWith("??", StringComparison.Ordinal) ||
                           text.Contains("Assigned", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Converter to invert a boolean value
        /// </summary>
        public class InverseBooleanConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is bool boolValue)
                {
                    return !boolValue;
                }
                return true;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is bool boolValue)
                {
                    return !boolValue;
                }
                return false;
                            }
                        }

                    /// <summary>
                    /// Converter to invert a boolean to Visibility
                    /// </summary>
                    public class InverseBoolToVisibilityConverter : IValueConverter
                    {
                        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                        {
                            if (value is bool boolValue)
                            {
                                return boolValue ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                            }
                            return System.Windows.Visibility.Visible;
                        }

                        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                        {
                            throw new NotImplementedException();
                        }
                    }
                }
