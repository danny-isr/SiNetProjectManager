using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.Services;
using SiNetProjectManagerV2.WPF_Window;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.Services.EmailContext;
using SiOffice.GoogleConnector;
using WpfSiData.WPFUserControl;

namespace SiNetProjectManagerV2.WPFUserControl
{
    /// <summary>
    /// Interaction logic for EmailManagementView.xaml
    /// </summary>
    public partial class EmailManagementView : UserControl
    {
        private EmailManagementViewModel? _subscribedVm;
        private WebView2PdfRenderer? _pdfRenderer;
        // DISABLED LEGACY — Gap 8. Commented out together with
        // GmailVisibleAttachmentsDomExtractor (parked behind `#if false`).
        // Candidate for physical deletion in a future approved cleanup round.
        // private GmailVisibleAttachmentsDomExtractor? _gmailDomProbe;
        private ExternalBrowserWindow? _accViewerWindow;
        private EmailContextViewModel? _emailContextVm;

        public EmailManagementView()
        {
            InitializeComponent();

            // Cleanup WebView2 state when control is unloaded
            Unloaded += OnUnloaded;
            Loaded += OnLoaded;

            // Phase 1: Bridge VM ? WebView2Helper for identity sync
            DataContextChanged += OnDataContextChanged;

            // Wire EmailViewerControl context-menu events
            EmailViewerCtl.OpenLocalFileRequested += OnViewerOpenLocalFileRequested;
            EmailViewerCtl.ShowInAccRequested += OnViewerShowInAccRequested;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Register the live email WebView2 with the PDF renderer for WYSIWYG capture
            if (EmailViewerCtl.WebView != null)
            {
                _pdfRenderer = App.ServiceProvider?.GetService<WebView2PdfRenderer>();
                _pdfRenderer?.RegisterLiveView(EmailViewerCtl.WebView);
            }

            // Gmail DOM extractor is DISABLED LEGACY (Gap 8) — commented out
            // together with the GmailVisibleAttachmentsDomExtractor source
            // (parked behind `#if false`) and the DI registration. Candidate
            // for physical deletion in a future approved cleanup round. Do not
            // re-enable without explicit approval.
            // _gmailDomProbe ??= App.ServiceProvider?.GetService<GmailVisibleAttachmentsDomExtractor>();

            // Initialize Email Context Panel via DI
            if (EmailContextPanel != null && _emailContextVm == null)
            {
                _emailContextVm = App.ServiceProvider?.GetService<EmailContextViewModel>();
                if (_emailContextVm != null)
                {
                    EmailContextPanel.DataContext = _emailContextVm;
                    _emailContextVm.FollowUpRequested = HandleFollowUpAsync;
                    _emailContextVm.ActionExecuted += OnEmailContextActionExecuted;
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
                _subscribedVm.OnLogoutRequested = null;
                _subscribedVm.EmailViewer.AttachmentClicked -= OnViewerAttachmentClicked;
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

                // Wire up external download ? ACC upload pipeline
                WebView2Helper.ProjectFileDownloaded += OnProjectFileDownloaded;

                // Wire up "create new alternative" input dialog
                vm.CreateNewAlternativeRequested = OnCreateNewAlternativeRequestedAsync;

                // Wire up logout coordination: when user logs out of Gmail,
                // also clear GoogleAuthService so all windows use the same account.
                vm.OnLogoutRequested = () =>
                {
                    var authService = App.ServiceProvider.GetService<SiOffice.GoogleConnector.Reports.GoogleAuthService>();
                    authService?.Logout();
                };

                // Sync initial state if already logged in
                if (!string.IsNullOrEmpty(vm.ConnectedEmail))
                {
                    WebView2Helper.CurrentUserEmail = vm.ConnectedEmail;
                }

                // Wire up attachment click from the EmailViewerControl ? open in ACC
                vm.EmailViewer.AttachmentClicked += OnViewerAttachmentClicked;
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
                    if (!string.IsNullOrEmpty(vm.ConnectedEmail) && EmailViewerCtl.WebView != null)
                    {
                        await WebView2Helper.InjectOAuthSessionAsync(EmailViewerCtl.WebView, vm.ConnectedEmail);
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
                    if (!vm.IsAuthenticated && EmailViewerCtl.WebView != null)
                    {
                        await WebView2Helper.ClearSessionAsync(EmailViewerCtl.WebView);
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
                                sel.ParsedDate != DateTime.MinValue ? sel.ParsedDate : null,
                                sel.InternetMessageId);
                        }
                        else
                        {
                            _emailContextVm.Clear();
                        }
                    }

                    // Gmail DOM probe is disabled (see GmailVisibleAttachmentsDomExtractor).
                    // Instead, emit a [GmailOpenUrl] diagnostic log comparing the URL options
                    // we can use to open Gmail in the WebView. No behavior change: the WebView
                    // still navigates via EmailInfo.GmailPopoutUrl (OpenMode=Current).
                    if (vm.SelectedEmail != null)
                    {
                        LogGmailOpenUrlOptions(vm.SelectedEmail);
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
                _subscribedVm.CreateNewAlternativeRequested = null;
                WebView2Helper.ProjectFileDownloaded -= OnProjectFileDownloaded;
                _subscribedVm = null;
            }

            // Disconnect follow-up callback
            if (_emailContextVm != null)
            {
                _emailContextVm.FollowUpRequested = null;
                _emailContextVm.ActionExecuted -= OnEmailContextActionExecuted;
            }

            // Unregister live view from PDF renderer
            _pdfRenderer?.UnregisterLiveView();

            // Cleanup the WebView2 helper state to prevent memory leaks
            if (EmailViewerCtl.WebView != null)
            {
                WebView2Helper.CleanupWebView(EmailViewerCtl.WebView);
            }
            if (CalendarWebView != null)
            {
                WebView2Helper.CleanupWebView(CalendarWebView);
            }
        }

        /// <summary>
        /// Surfaces a prominent notification after an email action is executed,
        /// so the user gets clear feedback even when the action only reports through
        /// the inline status text in <see cref="EmailContextPanel"/>.
        /// </summary>
        private void OnEmailContextActionExecuted(ActionResult result)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnEmailContextActionExecuted(result));
                return;
            }

            var owner = Window.GetWindow(this);
            string title;
            MessageBoxImage icon;

            if (result.IsCompleted)
            {
                title = "הפעולה הושלמה";
                icon = MessageBoxImage.Information;
            }
            else if (result.RequiresFollowUp)
            {
                // Follow-up dialogs handle their own UX - skip the toast.
                return;
            }
            else
            {
                title = "הפעולה לא הושלמה";
                icon = MessageBoxImage.Warning;
            }

            MessageBox.Show(owner, result.Message ?? string.Empty, title,
                MessageBoxButton.OK, icon);

            if (result.IsCompleted)
            {
                TryRefreshAfterAction(result);
            }
        }

        /// <summary>
        /// The Suggested Actions path does not run the optimistic UI updates that
        /// the WPF context-menu File path performs. After a successful
        /// state-changing action (e.g. AssociateToExistingProject), this refreshes
        /// the email list grouping and reloads the Gmail viewer so the new label
        /// is visible in the open message.
        /// </summary>
        private void TryRefreshAfterAction(ActionResult result)
        {
            try
            {
                // ActionExecutor populates OutputData["ActionType"] only for
                // state-changing flows (e.g. AssociateToExistingProject). When
                // present, refresh the email list grouping and reload the Gmail
                // viewer so the new label is visible without a manual refresh.
                if (!result.OutputData.ContainsKey("ActionType")) return;

                if (_subscribedVm?.LoadEmailsCommand?.CanExecute(null) == true)
                {
                    _subscribedVm.LoadEmailsCommand.Execute(null);
                }

                EmailViewerCtl.WebView?.CoreWebView2?.Reload();
            }
            catch
            {
                // UI refresh is best-effort; do not mask the action result.
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
                // ??? Utility actions - always direct ???
                case ActionFollowUp.FileImportDialog:
                    var importDialog = new FileImportDialog(emailMessageId) { Owner = owner };
                    importDialog.ShowDialog();
                    break;

                case ActionFollowUp.WorkflowAdvanceDialog:
                {
                    // Pilot for ApproveOrClose: if the selected action carries an explicit
                    // WorkflowInstanceId in PrefilledData (populated by SuggestedActionsBuilder
                    // only when exactly one active workflow exists), ask the user to confirm
                    // advancing the workflow. On confirm, write ConfirmAdvance=true into
                    // OutputData so EmailContextViewModel can retry ActionExecutor.ExecuteAsync,
                    // which returns Completed and the lifecycle bridge advances the workflow.
                    // If there is no WorkflowInstanceId in PrefilledData, do NOT prompt and do
                    // NOT confirm; workflow must never be inferred here.
                    var selected = _emailContextVm?.SelectedAction;
                    var hasExplicitWorkflow =
                        selected is not null
                        && selected.PrefilledData.TryGetValue("WorkflowInstanceId", out var wfObj)
                        && wfObj is int wfId
                        && wfId > 0;

                    if (hasExplicitWorkflow)
                    {
                        var confirm = MessageBox.Show(
                            owner,
                            result.Message ?? "האם לאשר את קידום התהליך?",
                            "אישור קידום תהליך",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (confirm == MessageBoxResult.Yes)
                        {
                            // Signal EmailContextViewModel to retry the action with
                            // ConfirmAdvance=true added to PrefilledData.
                            result.OutputData["ConfirmAdvance"] = true;
                        }
                        // On Cancel/No: leave OutputData untouched. No retry, no advance.
                    }
                    else if (result.OutputData.TryGetValue("WorkflowInstanceId", out var wfIdObj) && wfIdObj is int instanceId)
                    {
                        // Legacy path: a previously-completed workflow start placed the new
                        // instance id in OutputData -> open the instance window.
                        SiNetSQL.Services.ActiveProjectContext.Instance.NotifyTaskDataChanged();

                        var wfWindow = new WorkflowInstanceWindow(instanceId) { Owner = owner };
                        wfWindow.Show();
                    }
                    break;
                }

                case ActionFollowUp.ProjectPicker:
                    await HandleProjectPickerAsync(owner, emailMessageId, result);
                    break;

                // ??? Delegatable actions - go through AssignActionDialog ???
                case ActionFollowUp.NewProjectDialog:
                case ActionFollowUp.TaskCreationDialog:
                case ActionFollowUp.DecisionDialog:
                case ActionFollowUp.DisciplineDialog:
                    await HandleDelegatableActionAsync(owner, followUp, result, emailMessageId);
                    break;

                default:
                    MessageBox.Show(
                        result.Message,
                        "פעולה במייל", MessageBoxButton.OK, MessageBoxImage.Information);
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
                // User chose to do it themselves - open the original dialog
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
                    // Open floating email preview so the user can see the source email
                    var previewWindow = new EmailPreviewWindow(emailMessageId) { Owner = owner };
                    previewWindow.Show();

                    var mainWindow = owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
                    if (mainWindow?.DataContext is MainWindowViewModel vm)
                    {
                        vm.CurrentView = new CreateProjectUserControl(emailMessageId);
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
                MessageBox.Show("לא נמצא סטטוס משימה תקין.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if a task of this type already exists on this project for ANY employee.
            // Business rule: one task type per project is assigned to one employee only.
            var existingTask = await db.ProjectAssignments
                .Include(a => a.AssignedTo)
                .FirstOrDefaultAsync(a =>
                    a.ProjectId == emailProjectId
                    && a.TaskTypeId == taskType!.Id, ct);

            if (existingTask is not null)
            {
                if (existingTask.AssignedToId == assignee.Id)
                {
                    // Same employee - just link the email to the existing task
                    await LinkEmailToTaskIfNeededAsync(db, existingTask.Id, emailMessageId,
                        actionDescription, currentUserId, assignee.Id, ct);
                    await db.SaveChangesAsync(ct);

                    MessageBox.Show(
                        $"מייל זה כבר משויך למשימה של {assignee.Name} (משימה: {existingTask.Id}).\n" +
                        $"לא נוצרה משימה כפולה.",
                        "משימה קיימת", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Different employee - ask user whether to transfer
                var currentName = existingTask.AssignedTo?.Name ?? $"עובד #{existingTask.AssignedToId}";
                var transferResult = MessageBox.Show(
                    $"משימה זו כבר משויכת ל-{currentName} (משימה: {existingTask.Id}).\n" +
                    $"האם להעביר את המשימה ל-{assignee.Name}?",
                    "העברת משימה", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (transferResult != MessageBoxResult.Yes)
                    return;

                // Transfer: update assignee on the existing task
                existingTask.AssignedToId = assignee.Id;
                existingTask.Modified = DateTime.Now;
                existingTask.EditorId = currentUserId > 0 ? currentUserId : null;

                await LinkEmailToTaskIfNeededAsync(db, existingTask.Id, emailMessageId,
                    actionDescription, currentUserId, assignee.Id, ct);

                await db.SaveChangesAsync(ct);

                MessageBox.Show(
                    $"✓ המשימה הועברה מ-{currentName} ל-{assignee.Name}.",
                    "העברה הצליחה", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Build task body with action context for the assignee
            var bodyParts = new List<string>
            {
                $"[פעולה: {actionDescription}]",
                $"[מעקב: {followUp}]",
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
                Title = $"{actionDescription} – מייל #{emailMessageId}",
                Body = string.Join(Environment.NewLine, bodyParts),
            };

            await SiNetSQL.Services.TaskFactory.CreateAsync(db, task, currentUserId,
                link: new SiNetSQL.Services.TaskFactory.TaskLinkInfo(
                    TaskLinkEntityType.EmailInboxMessage, emailMessageId,
                    Description: actionDescription),
                eventNote: $"נוצרה משימה מפעולה במייל: {actionDescription}",
                ct: ct);

            MessageBox.Show(
                $"✓ נוצרה משימה עבור {assignee.Name}",
                "משימה נוצרה", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Links an <see cref="EmailInboxMessage"/> to a <see cref="ProjectAssignment"/>
        /// via <see cref="TaskLink"/> if such a link doesn't already exist.
        /// Does NOT call <c>SaveChangesAsync</c> - the caller is responsible for saving.
        /// </summary>
        private static async Task LinkEmailToTaskIfNeededAsync(
            SiNetSQLDbContext db,
            int taskId,
            int emailMessageId,
            string description,
            int currentUserId,
            int fallbackUserId,
            CancellationToken ct)
        {
            var alreadyLinked = await db.TaskLinks
                .AnyAsync(l =>
                    l.TaskId == taskId
                    && l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage
                    && l.LinkedEntityId == emailMessageId, ct);

            if (alreadyLinked) return;

            db.TaskLinks.Add(new TaskLink
            {
                TaskId = taskId,
                LinkedEntityType = TaskLinkEntityType.EmailInboxMessage,
                LinkedEntityId = emailMessageId,
                Role = TaskLinkRole.Trigger,
                Description = description,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUserId > 0 ? currentUserId : fallbackUserId,
            });
        }

        /// <summary>
        /// Opens the ProjectSelectorDialog. When the user picks a project,
        /// updates the email's ProjectId in the DB and re-triggers context analysis.
        /// </summary>
        private Task HandleProjectPickerAsync(Window? owner, int emailMessageId, ActionResult? pendingResult = null)
        {
            var dialog = new ProjectSelectorDialog();
            if (owner != null) dialog.Owner = owner;

            if (dialog.ShowDialog() == true && dialog.SelectedProject is { } project)
            {
                SiNetSQL.Services.AppLogger.Info("[EmailContext] ProjectPicker picked ProjectId=" + project.Id + " for EmailMessageId=" + emailMessageId + ".");

                // NOTE: The shared EmailFilingService (invoked by ActionExecutor
                // when the action is retried with ProjectId) owns the
                // EmailInboxMessage.ProjectId write, the Gmail label,
                // ThreadStatusMapping, and TaskLifecycle hook. We only surface
                // the picked ProjectId here.

                // Surface the picked project to the caller so the action can be retried
                // with a known ProjectId (e.g. CreateNewReview ? OpenReviewProject task).
                if (pendingResult != null)
                {
                    pendingResult.OutputData["ProjectId"] = project.Id;
                }

                // IMPORTANT: do NOT call SetEmailMessageAsync(...) here. That would
                // rebuild SuggestedActions and reset the bound SelectedItem to null,
                // wiping _selectedAction in EmailContextViewModel before the retry
                // path can re-execute the action with the picked ProjectId. The
                // retry path itself calls AnalyzeCurrentEmailAsync after completion,
                // which refreshes the context chips.
            }

            return Task.CompletedTask;
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
                $"גודל זה נקבע בהגדרות המערכת (MaxUploadFileSizeMb).\n\n" +
                $"האם להעלות בכל זאת ל-ACC?",
                "קובץ גדול",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Shows the alternative name dialog so the user can enter a new alternative.
        /// The optional second-level field is joined with the first using '~' as a UI
        /// grouping separator. Returns the full name, or null if cancelled/empty.
        /// </summary>
        private Task<string?> OnCreateNewAlternativeRequestedAsync(IReadOnlyList<string> existingNames)
        {
            var owner = Window.GetWindow(this);

            var vm = new AlternativeNameViewModel(initialName: "", existingNames: existingNames);
            var dialog = new AlternativeNameWindow(vm)
            {
                Owner = owner
            };

            var result = dialog.ShowDialog() == true
                ? (string.IsNullOrWhiteSpace(vm.AlternativeName) ? null : vm.AlternativeName)
                : null;

            return Task.FromResult(result);
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

        /// <summary>
        /// Handles attachment clicks from the extracted EmailViewerControl.
        /// Opens the attachment in ACC viewer.
        /// </summary>
        private void OnViewerAttachmentClicked(EmailAttachment attachment)
        {
            if (_subscribedVm?.ShowAttachmentInAccCommand is { } cmd
                && cmd.CanExecute(attachment))
            {
                cmd.Execute(attachment);
            }
        }

        /// <summary>
        /// Handles "פתח קובץ מקומי" from EmailViewerControl context menu.
        /// </summary>
        private void OnViewerOpenLocalFileRequested(object? sender, EmailAttachment attachment)
        {
            if (_subscribedVm?.OpenLocalFileCommand is { } cmd
                && cmd.CanExecute(attachment))
            {
                cmd.Execute(attachment);
            }
        }

        /// <summary>
        /// Handles "הצג ב-ACC" from EmailViewerControl context menu.
        /// </summary>
        private void OnViewerShowInAccRequested(object? sender, EmailAttachment attachment)
        {
            if (_subscribedVm?.ShowAttachmentInAccCommand is { } cmd
                && cmd.CanExecute(attachment))
            {
                cmd.Execute(attachment);
            }
        }

        /// <summary>
        /// Phase 1 diagnostic-only Gmail DOM attachment probe — DISABLED LEGACY (Gap 8).
        /// Commented out together with GmailVisibleAttachmentsDomExtractor.
        /// Candidate for physical deletion in a future approved cleanup round.
        /// </summary>
        // private async System.Threading.Tasks.Task ProbeGmailVisibleAttachmentsAsync(EmailInfo email)
        // {
        //     // Disabled: the extractor itself is now a no-op. Awaiting completed task keeps
        //     // the async signature stable for any legacy call site.
        //     await System.Threading.Tasks.Task.CompletedTask;
        // }

        /// <summary>
        /// Emits a single <c>[GmailOpenUrl]</c> diagnostic log that records the URL
        /// actually used to open Gmail in the WebView for the selected message.
        /// As of this round the default is <b>MessageIdAll</b> (<c>#all/{messageId}</c>)
        /// produced by <see cref="EmailInfo.GmailPopoutUrl"/>; the legacy
        /// <c>#inbox/{ThreadId}</c> URL is logged as <c>ThreadInbox</c> for reference
        /// only and is never navigated to.
        /// <para>RFC822 <c>Message-ID</c> (<c>#search/rfc822msgid:{...}</c>) is not
        /// emitted &#8212; <see cref="EmailInfo"/> does not currently expose that
        /// header in production.</para>
        /// </summary>
        private static void LogGmailOpenUrlOptions(EmailInfo email)
        {
            try
            {
                var messageId = email.MessageId ?? string.Empty;
                var threadId = email.ThreadId ?? string.Empty;
                var urlUsed = email.GmailPopoutUrl ?? "(null)";
                var threadInboxUrl = !string.IsNullOrEmpty(threadId)
                    ? $"https://mail.google.com/mail/u/0/#inbox/{threadId}"
                    : "(no ThreadId)";

                SiNetSQL.Services.AppLogger.Info(
                    "[GmailOpenUrl] " +
                    $"SelectedGmailMessageId={messageId}, ThreadId={threadId}, " +
                    $"OpenMode=MessageIdAll, UrlUsed={urlUsed}");
                SiNetSQL.Services.AppLogger.Info(
                    "[GmailOpenUrl] " +
                    $"SelectedGmailMessageId={messageId}, ThreadId={threadId}, " +
                    $"OpenMode=ThreadInbox (reference-only, not navigated), UrlUsed={threadInboxUrl}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GmailOpenUrl] Failed to emit diagnostic log: {ex.Message}");
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
