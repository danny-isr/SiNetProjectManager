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
    // #region agent log
    private static int _debugInstanceCount;
    // #endregion

    public FloatingProjectTasksView()
    {
        InitializeComponent();

        // #region agent log
        var instanceCount = System.Threading.Interlocked.Increment(ref _debugInstanceCount);
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
            "Tasks.FloatWindow",
            $"ctor liveInstanceCount={instanceCount}");
        Closed += (_, _) =>
        {
            var remaining = System.Threading.Interlocked.Decrement(ref _debugInstanceCount);
            SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
                "Tasks.FloatWindow",
                $"closed remainingInstanceCount={remaining}");
        };
        // #endregion

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
            case SiNetSQL.Services.Tasks.TaskComponentKeys.InspectionReport:
            case SiNetSQL.Services.Tasks.TaskComponentKeys.ManagerReviewApproval:
                OpenInspectionReportTask(mainWindow, request);
                break;

            case SiNetSQL.Services.Tasks.TaskComponentKeys.PoliceSubmission:
            case SiNetSQL.Services.Tasks.TaskComponentKeys.MaterialChecklist:
            case SiNetSQL.Services.Tasks.TaskComponentKeys.ProjectWork:
                // IdentifyQuoteRequest (and other classification-only tasks) are registered
                // with ComponentKey=ProjectWork but must open QuoteClassificationDialog /
                // TaskResultPicker — not Project Work. Otherwise Intake never advances.
                if (request.ComponentKey == SiNetSQL.Services.Tasks.TaskComponentKeys.ProjectWork
                    && TryHandleClassificationNavigation(mainWindow, request, primaryEmailId))
                {
                    break;
                }

                OpenProjectWorkTask(mainWindow, request);
                break;

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

            case SiNetSQL.Services.Tasks.TaskComponentKeys.EmailComposeToPlanner:
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var composerService = App.ServiceProvider.GetRequiredService<SiNetSQL.Services.EmailOutbound.IEmailComposerService>();
                            var dbFactory = App.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                            await using var db = await dbFactory.CreateDbContextAsync();

                            int? emailIdForCompose = primaryEmailId;
                            if (emailIdForCompose == null || emailIdForCompose == 0)
                            {
                                var link = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                                    Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.TaskLinks)
                                        .Where(l => l.TaskId == request.TaskId && l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage));
                                emailIdForCompose = (int?)link?.LinkedEntityId;
                            }

                            if (emailIdForCompose is int validEmailId && validEmailId > 0)
                            {
                                var email = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                                    Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.EmailInboxMessages), e => e.Id == validEmailId);
                                if (email != null)
                                {
                                    var subject = email.Subject ?? "";
                                    if (!subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        subject = "Re: " + subject;
                                    }

                                    string toAddress = "";
                                    if (!string.IsNullOrWhiteSpace(email.FromAddress))
                                    {
                                        try
                                        {
                                            var addr = new System.Net.Mail.MailAddress(email.FromAddress);
                                            toAddress = addr.Address;
                                        }
                                        catch
                                        {
                                            var match = System.Text.RegularExpressions.Regex.Match(email.FromAddress, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                                            toAddress = match.Success ? match.Value : email.FromAddress;
                                        }
                                    }

                                    var ccAddresses = new System.Collections.Generic.List<string>();
                                    var bccAddresses = new System.Collections.Generic.List<string>();
                                    SiOffice.GoogleConnector.EmailInfo? originalEmailInfo = null;
                                    var googleService = App.ServiceProvider.GetService<SiOffice.GoogleConnector.GoogleService>();
                                    if (googleService != null)
                                    {
                                        string? gmailMessageId = null;
                                        if (!string.IsNullOrEmpty(email.InternetMessageId))
                                        {
                                            gmailMessageId = await googleService.ResolveLocalMessageIdByRfc822Async(email.InternetMessageId);
                                        }

                                        if (string.IsNullOrEmpty(gmailMessageId) && email.MessageUniqueId != null && email.MessageUniqueId.StartsWith("gmail:"))
                                        {
                                            gmailMessageId = email.MessageUniqueId.Substring(6);
                                        }

                                        if (!string.IsNullOrEmpty(gmailMessageId))
                                        {
                                            try
                                            {
                                                originalEmailInfo = await googleService.LoadFullEmailBodyAsync(gmailMessageId);
                                                if (originalEmailInfo != null)
                                                {
                                                    if (!string.IsNullOrWhiteSpace(originalEmailInfo.Cc))
                                                    {
                                                        var parts = originalEmailInfo.Cc.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                                        foreach (var part in parts)
                                                        {
                                                            string cleanCc = "";
                                                            try
                                                            {
                                                                var addr = new System.Net.Mail.MailAddress(part);
                                                                cleanCc = addr.Address;
                                                            }
                                                            catch
                                                            {
                                                                var match = System.Text.RegularExpressions.Regex.Match(part, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                                                                cleanCc = match.Success ? match.Value : part;
                                                            }
                                                            if (!string.IsNullOrWhiteSpace(cleanCc))
                                                            {
                                                                ccAddresses.Add(cleanCc);
                                                            }
                                                        }
                                                    }

                                                    if (!string.IsNullOrWhiteSpace(originalEmailInfo.Bcc))
                                                    {
                                                        var parts = originalEmailInfo.Bcc.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                                        foreach (var part in parts)
                                                        {
                                                            string cleanBcc = "";
                                                            try
                                                            {
                                                                var addr = new System.Net.Mail.MailAddress(part);
                                                                cleanBcc = addr.Address;
                                                            }
                                                            catch
                                                            {
                                                                var match = System.Text.RegularExpressions.Regex.Match(part, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                                                                cleanBcc = match.Success ? match.Value : part;
                                                            }
                                                            if (!string.IsNullOrWhiteSpace(cleanBcc))
                                                            {
                                                                bccAddresses.Add(cleanBcc);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[FloatingTasks] Load CC/BCC error: {ex}");
                                            }
                                        }
                                    }

                                    if (originalEmailInfo != null)
                                    {
                                        if (string.IsNullOrEmpty(originalEmailInfo.ThreadId)) originalEmailInfo.ThreadId = email.GmailThreadId ?? "";
                                        if (string.IsNullOrEmpty(originalEmailInfo.InternetMessageId)) originalEmailInfo.InternetMessageId = email.InternetMessageId;
                                        if (string.IsNullOrEmpty(originalEmailInfo.References)) originalEmailInfo.References = email.References;
                                    }
                                    else
                                    {
                                        originalEmailInfo = new SiOffice.GoogleConnector.EmailInfo
                                        {
                                            ThreadId = email.GmailThreadId ?? "",
                                            InternetMessageId = email.InternetMessageId,
                                            References = email.References
                                        };
                                    }

                                    var context = new SiNetSQL.DTOs.Email.EmailComposerContext
                                    {
                                        EntityType = "Task",
                                        EntityId = request.TaskId,
                                        TaskId = request.TaskId,
                                        WorkflowId = request.WorkflowInstanceId,
                                        To = new System.Collections.Generic.List<string> { toAddress },
                                        Cc = ccAddresses,
                                        Bcc = bccAddresses,
                                        Subject = subject,
                                        OriginalEmail = originalEmailInfo,
                                        Body = "\u05e9\u05dc\u05d5\u05dd, \n\n\u05d1\u05d4\u05de\u05e9\u05da \u05dc\u05e4\u05e0\u05d9\u05d9\u05ea\u05da, \u05e0\u05e9\u05de\u05d7 \u05dc\u05e7\u05d1\u05dc\u05ea \u05d1\u05e7\u05e9\u05d4/\u05d4\u05d6\u05de\u05e0\u05d4 \u05e8\u05e9\u05de\u05d9\u05ea \u05de\u05d4\u05e8\u05e9\u05d5\u05ea \u05e2\u05dc \u05de\u05e0\u05ea \u05dc\u05d4\u05ea\u05d7\u05d9\u05dc \u05d1\u05ea\u05d4\u05dc\u05d9\u05da \u05d4\u05d1\u05d3\u05d9\u05e7\u05d4. \n\n\u05d1\u05d1\u05e8\u05db\u05d4,\n\u05e6\u05d5\u05d5\u05ea \u05d4\u05de\u05e9\u05e8\u05d3"
                                    };

                                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                                    {
                                        var result = await composerService.ComposeAndSendAsync(context);
                                        if (result != null && result.Success)
                                        {
                                            var completionEventCode = request.TaskTypeCode switch
                                            {
                                                SiNetSQL.Constants.TaskTypeCodes.SendInternalApproval => SiNetSQL.Services.Tasks.ReviewCompletionEvents.ReviewPrincipallyApproved,
                                                SiNetSQL.Constants.TaskTypeCodes.SendReportToPlanner => SiNetSQL.Services.Tasks.ReviewCompletionEvents.ReviewCommentsSentToPlanner,
                                                SiNetSQL.Constants.TaskTypeCodes.ForwardPoliceCommentsToPlanner => SiNetSQL.Services.Tasks.ReviewCompletionEvents.ReviewCommentsSentToPlanner,
                                                SiNetSQL.Constants.TaskTypeCodes.RequestMunicipalityInvitation => SiNetSQL.Services.Tasks.ReviewCompletionEvents.RequestSourceClassified,
                                                _ => null
                                            };

                                            var taskResultCode = request.TaskTypeCode switch
                                            {
                                                SiNetSQL.Constants.TaskTypeCodes.SendInternalApproval => SiNetSQL.Constants.TaskResultCodes.PrincipallyApproved,
                                                SiNetSQL.Constants.TaskTypeCodes.SendReportToPlanner => SiNetSQL.Constants.TaskResultCodes.CommentsSentToPlanner,
                                                SiNetSQL.Constants.TaskTypeCodes.ForwardPoliceCommentsToPlanner => SiNetSQL.Constants.TaskResultCodes.CommentsSentToPlanner,
                                                SiNetSQL.Constants.TaskTypeCodes.RequestMunicipalityInvitation => SiNetSQL.Constants.TaskResultCodes.RequestFromPlanner,
                                                _ => null
                                            };

                                            if (completionEventCode != null && taskResultCode != null)
                                            {
                                                await ViewModel.CompleteClassificationTaskAsync(request.TaskId, completionEventCode, taskResultCode);
                                            }
                                            else
                                            {
                                                ViewModel.RefreshCommand.Execute(null);
                                            }
                                        }
                                    });
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[FloatingTasks] Error opening EmailComposer: {ex}");
                        }
                    });
                }
                break;

            default:
                // Classification-only tasks may also arrive with an unknown ComponentKey;
                // Prefer the shared host (QuoteClassificationDialog / TaskResultPicker).
                if (TryHandleClassificationNavigation(mainWindow, request, primaryEmailId))
                {
                    break;
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
    /// Opens a workflow inspection-report task (e.g. <c>PerformProfessionalReview</c>)
    /// on the EXACT report resolved by <see cref="SiNetSQL.Services.Tasks.TaskNavigationResolver"/>.
    /// Reuses the project-centric inspection window but drives it through
    /// <see cref="SiNetSQL.MVVM.FloatingInspectionViewModel.OpenForTaskAsync"/>; it does
    /// NOT introduce a parallel navigation router and NEVER auto-picks a report.
    /// </summary>
    private void OpenInspectionReportTask(
        MainWindow? mainWindow,
        SiNetSQL.Services.Tasks.TaskNavigationRequest request)
    {
        // Resolver already failed (e.g. missing project / no work target where one
        // is required). Surface the reason instead of opening a generic window.
        if (!request.IsSuccess)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FloatingTasksView] InspectionReport task not resolvable. TaskId={request.TaskId}, " +
                $"Reason={request.FailureReason}, Message={request.FailureMessage}");
            MessageBox.Show(
                string.IsNullOrWhiteSpace(request.FailureMessage)
                    ? "המשימה לא מקושרת לדוח בדיקה ולכן לא ניתן לפתוח אותה מתוך תהליך העבודה."
                    : request.FailureMessage,
                "פתיחת משימת דוח",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (request.ProjectId is not int inspectionProjectId)
        {
            MessageBox.Show(
                "המשימה אינה מקושרת לפרויקט ולכן לא ניתן לפתוח אותה.",
                "פתיחת משימת דוח",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var reportId = request.PrimaryWorkTargetEntityId is long rid && rid > 0 ? (int?)rid : null;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            // Set the active project so the rest of the app (Work Window, file
            // providers) stays in sync with the task being opened.
            try
            {
                var dbFactory = App.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var project = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Projects),
                    p => p.Id == inspectionProjectId);
                if (project != null)
                {
                    SiNetSQL.Services.ActiveProjectContext.Instance.SetActiveProject(project);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FloatingTasksView] Error setting active project for InspectionReport task: {ex}");
            }

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                // Prefer New System WorkSurfaceLauncher (InspectionWindow + exact report load).
                if (App.ServiceProvider.GetService<SiNet.App.Wpf.WorkSurfaces.IWorkSurfaceLauncher>() is { } launcher)
                {
                    var opened = await launcher.TryOpenFromTaskAsync(request.TaskId).ConfigureAwait(true);
                    if (opened)
                    {
                        mainWindow?.Activate();
                        return;
                    }

                    MessageBox.Show(
                        $"לא ניתן לפתוח את משימה #{request.TaskId} דרך WorkSurfaceLauncher.\n" +
                        "אין fallback לחלון הביקורת הישן מנתיב משימה כשה-launcher רשום.",
                        "פתיחת משימת דוח",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // TEMPORARY — legacy FloatingInspection when launcher is not registered.
                // REMOVAL WHEN: IWorkSurfaceLauncher is always registered in V2 DI.
                var window = mainWindow?.ShowFloatingInspectionWindow();
                mainWindow?.Activate();
                if (window == null) return;

                var context = new SiNetSQL.Services.Tasks.InspectionReportTaskContext(
                    TaskId: request.TaskId,
                    ComponentKey: request.ComponentKey,
                    TaskTypeCode: request.TaskTypeCode,
                    ProjectId: request.ProjectId,
                    PrimaryReportId: reportId,
                    WorkflowInstanceId: request.WorkflowInstanceId,
                    CurrentStageId: request.CurrentStageId,
                    AllowedTaskResultCodes: request.AllowedTaskResultCodes,
                    CompletionPolicy: request.CompletionPolicy,
                    OnTaskRefreshRequested: null);

                var ok = await window.ViewModel.OpenForTaskAsync(context);
                if (!ok)
                {
                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(window.ViewModel.StatusMessage)
                            ? "לא ניתן לפתוח את הדוח עבור המשימה."
                            : window.ViewModel.StatusMessage,
                        "פתיחת משימת דוח",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        });
    }


    /// <summary>
    /// Opens the project-scoped task surface (ProjectWork / MaterialChecklist / PoliceSubmission).
    /// Prefers the New System <see cref="SiNet.App.Wpf.WorkSurfaces.IWorkSurfaceLauncher"/> (native
    /// ProjectWork task surface + completion via ITaskCompletionService); when the launcher is not
    /// registered it falls back to the legacy <c>ShowProjectWork</c> file window. Sets the legacy
    /// ActiveProjectContext first so file providers / legacy windows stay in sync. Classification-only
    /// ProjectWork tasks are intercepted earlier by <see cref="TryHandleClassificationNavigation"/> and
    /// never reach this method.
    /// </summary>
    private void OpenProjectWorkTask(
        MainWindow? mainWindow,
        SiNetSQL.Services.Tasks.TaskNavigationRequest request)
    {
        if (request.ProjectId is not int workProjectId)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FloatingTasksView] ProjectWork task {request.TaskId} (ComponentKey={request.ComponentKey}) has no project; cannot open.");
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            // Keep the legacy ActiveProjectContext in sync (file providers / legacy Work Window).
            try
            {
                var dbFactory = App.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var project = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Projects), p => p.Id == workProjectId);
                if (project != null)
                {
                    SiNetSQL.Services.ActiveProjectContext.Instance.SetActiveProject(project);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FloatingTasksView] Error loading project for PoliceSubmission/MaterialChecklist/ProjectWork: {ex}");
            }

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                // Native ProjectWork task surface via WorkSurfaceLauncher (no legacy ProjectWorkView fallback).
                if (App.ServiceProvider.GetService<SiNet.App.Wpf.WorkSurfaces.IWorkSurfaceLauncher>() is { } launcher)
                {
                    var opened = await launcher.TryOpenFromTaskAsync(request.TaskId).ConfigureAwait(true);
                    if (opened)
                    {
                        mainWindow?.Activate();
                        return;
                    }

                    MessageBox.Show(
                        $"לא ניתן לפתוח את משימה #{request.TaskId} דרך WorkSurfaceLauncher.",
                        "פתיחת משימת עבודה",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show(
                    "IWorkSurfaceLauncher אינו רשום — לא ניתן לפתוח משימת סביבת עבודה.",
                    "פתיחת משימת עבודה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        });
    }

    /// <summary>
    /// Opens QuoteClassificationDialog / TaskResultPicker for classification-only
    /// tasks (IdentifyQuoteRequest, ClassifyRequestSource) and completes via coordinator.
    /// Returns true when this request was handled as classification (even if the user cancelled).
    /// </summary>
    private bool TryHandleClassificationNavigation(
        MainWindow? mainWindow,
        SiNetSQL.Services.Tasks.TaskNavigationRequest request,
        int? primaryEmailId)
    {
        if (request.AllowedTaskResultCodes.Count == 0
            || request.CompletionPolicy != SiNetSQL.Services.Tasks.TaskCompletionPolicy.WorkflowResultRecorded)
        {
            return false;
        }

        var completionEventCode = ResolveClassificationCompletionEventCode(request.TaskTypeCode);
        if (string.IsNullOrEmpty(completionEventCode))
        {
            return false;
        }

        string? pickedCode = null;

        // IdentifyQuoteRequest gets a dedicated host that shows the source email
        // + attachments. Other classification tasks fall back to the generic picker.
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

        return true;
    }

    private static string? ResolveClassificationCompletionEventCode(string taskTypeCode) =>
        taskTypeCode switch
        {
            SiNetSQL.Constants.TaskTypeCodes.IdentifyQuoteRequest =>
                SiNetSQL.Services.Tasks.ReviewCompletionEvents.ReviewQuoteRequestClassified,
            SiNetSQL.Constants.TaskTypeCodes.ClassifyRequestSource =>
                SiNetSQL.Services.Tasks.ReviewCompletionEvents.RequestSourceClassified,
            _ => null,
        };

    /// <summary>Returns a Hebrew prompt explaining the choice for a classification task.</summary>
    private static string BuildClassificationPrompt(string taskTypeCode) => taskTypeCode switch
    {
        SiNetSQL.Constants.TaskTypeCodes.IdentifyQuoteRequest =>
            "האם המייל מהווה פנייה להצעת מחיר? בחר תוצאה כדי לסגור את המשימה.",
        SiNetSQL.Constants.TaskTypeCodes.ClassifyRequestSource =>
            "בחר את מקור הפנייה כדי לסווג את הפרויקט ולסגור את המשימה:",
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
