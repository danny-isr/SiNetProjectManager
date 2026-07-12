using SiNetProjectManagerV2.WPF;

using SiNetProjectManagerV2.WPFUserControl;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.WPF_Window;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WpfSiData.WPFUserControl;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiNetSQL.Data;
using SiOffice.GoogleConnector;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2
{
    public partial class MainWindow : BaseWindow
    {
        private static readonly string _appVersion = GetAppVersion();
        private static readonly string _defaultTitle = $"תוכנת ניהול   v{_appVersion}";
        private FloatingProjectTasksView? _floatingTasksWindow;
        private FloatingInspectionView? _floatingInspectionWindow;

        // Cached heavy views — created on first navigation, then reused.
        // The view-models subscribe to ActiveProjectContext.ActiveProjectChanged so
        // they stay in sync with the global active project even while hidden,
        // and any embedded WebView2 instances keep their state across navigation.
        private ProjectWorkView? _cachedProjectWorkView;
        private EmailManagementView? _cachedEmailManagementView;

        // New clean-architecture Current Project context (fake/in-memory this slice).
        // The shell is the single subscriber that renders the Current Project into the window
        // Title (docs/PROJECTS.md §11). Resolved from DI when available; null-safe if unregistered.
        // This COEXISTS with the legacy ActiveProjectContext for now — both feed the same title via
        // ProjectTitleFormatter so there is no conflicting/duplicate title format. ActiveProjectContext
        // is intentionally NOT removed in this slice.
        private readonly ICurrentProjectContext? _currentProjectContext;

        /// <summary>
        /// Returns the cached <see cref="EmailManagementViewModel"/> if it has
        /// already been created by a previous navigation, or <c>null</c> if
        /// the view has not been opened yet. Used by project-creation flows
        /// (e.g. <see cref="Dialogs.WorkflowCreateProjectWindow"/> after
        /// OpenQuoteProject) to reuse the existing "שייך לפרויקט" local
        /// refresh path (<see cref="EmailManagementViewModel.ApplyEmailAssignedToProjectLocalAsync"/>)
        /// without creating a parallel refresh mechanism and without
        /// triggering a Gmail or full-mailbox reload.
        /// </summary>
        public EmailManagementViewModel? TryGetCachedEmailManagementViewModel()
            => _cachedEmailManagementView?.DataContext as EmailManagementViewModel;

        public MainWindow()
        {
            InitializeComponent(); // חובה כדי לטעון את ה־XAML
            Title = _defaultTitle;

            // Dynamic title: reflect the currently active project (legacy source).
            ActiveProjectContext.Instance.ActiveProjectChanged += OnActiveProjectChanged;

            // New clean-architecture source: the shared singleton Current Project context.
            // The shell is the single place that renders the Current Project into the title
            // (docs/PROJECTS.md §11). Resolve defensively — if the fake module is not registered
            // (or the provider is not ready), the window still works and keeps the default title.
            _currentProjectContext = App.ServiceProvider?.GetService<ICurrentProjectContext>();
            if (_currentProjectContext is not null)
            {
                _currentProjectContext.CurrentProjectChanged += OnCurrentProjectChanged;

                // Seed the title from any project already selected before this window opened.
                if (_currentProjectContext.CurrentProject is not null)
                {
                    Title = ProjectTitleFormatter.Format(_defaultTitle, _currentProjectContext.CurrentProject);
                }
            }

#if DEBUG
            // DEBUG-only dev data reset: visible only to allowed Windows users.
            if (SiNetSQL.Services.DevDataResetService.IsCurrentUserAllowed())
            {
                DevResetMenuItem.Visibility = Visibility.Visible;
                DevResetSeparator.Visibility = Visibility.Visible;
            }
#endif
        }

        private static string GetAppVersion()
        {
            // Prefer InformationalVersion (matches csproj <Version>); fall back to FileVersion.
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // Strip "+commitsha" suffix that SourceLink may append.
                var plus = info.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        private void OnActiveProjectChanged(SiNetSQL.Models.Project? project)
        {
            Dispatcher.Invoke(() =>
            {
                // Route through the shared formatter so legacy and new sources produce an identical title.
                Title = ProjectTitleFormatter.Format(_defaultTitle, project?.Title);
            });
        }

        /// <summary>
        /// Renders the new clean-architecture Current Project (<see cref="ICurrentProjectContext"/>) into
        /// the window <see cref="Window.Title"/>. The shell is the single subscriber that owns the title
        /// (docs/PROJECTS.md §11): feature windows only change the Current Project; this reacts.
        /// <para>
        /// <see cref="ICurrentProjectContext.CurrentProjectChanged"/> may fire off the UI thread, so the
        /// update is marshaled to the WPF <see cref="Dispatcher"/>. The context already de-duplicates by
        /// <c>ProjectId</c>, so re-selecting the same project does not re-raise the event and cannot cause
        /// a duplicate title update.
        /// </para>
        /// </summary>
        private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Title = ProjectTitleFormatter.Format(_defaultTitle, e.Project);
            });
        }

        /// <summary>
        /// Intercepts application close. If background ACC operations (ingestion or external
        /// downloads) are in progress, shows a status dialog with count and options to wait,
        /// force-close, or cancel.
        /// <para>
        /// This works correctly with <see cref="ShutdownMode.OnMainWindowClose"/>:
        /// <list type="bullet">
        ///   <item>If user cancels: <c>e.Cancel = true</c> prevents MainWindow from closing → app continues running</item>
        ///   <item>If user confirms: MainWindow closes → app shuts down (ShutdownMode.OnMainWindowClose)</item>
        /// </list>
        /// </para>
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (AccBackgroundWorkMonitor.HasActiveUploads)
            {
                var dialog = new BackgroundUploadsDialog(BackgroundCloseScope.Application) { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Detach the shell title subscriber. The Current Project context is an app-instance singleton
            // that outlives this window, so leaving the handler attached would keep MainWindow alive.
            if (_currentProjectContext is not null)
            {
                _currentProjectContext.CurrentProjectChanged -= OnCurrentProjectChanged;
            }

            base.OnClosing(e);
        }

        // ─────────────────────────────────────────────────────────────
        //  Navigation Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the main content area to the specified view via the ViewModel.
        /// Centralizes the <c>DataContext is MainWindowViewModel</c> check.
        /// </summary>
        private void NavigateToView(FrameworkElement view)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CurrentView = view;
            }
        }

        /// <summary>
        /// AUTH-02: Verifies the current user has Administrator role.
        /// Shows a localized denial message if not authorized.
        /// </summary>
        /// <returns>True if user is Administrator, false if denied.</returns>
        private static bool RequireAdminAccess(string deniedMessage)
        {
            if (CurrentUserContext.Instance.IsAdmin)
                return true;

            MessageBox.Show(deniedMessage, "גישה נדחתה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        /// <summary>
        /// AUTH-02: Verifies the current user has Management or above role.
        /// Shows a localized denial message if not authorized.
        /// </summary>
        /// <returns>True if user is Management or above, false if denied.</returns>
        private static bool RequireManagementAccess(string deniedMessage)
        {
            if (CurrentUserContext.Instance.IsManagement)
                return true;

            MessageBox.Show(deniedMessage, "גישה נדחתה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // ─────────────────────────────────────────────────────────────
        //  Content-View Navigation Handlers
        // ─────────────────────────────────────────────────────────────

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (App.AppSettings == null)
            {
                MessageBox.Show("Settings not loaded.");
                return;
            }
            var win = new SettingsWindow(App.AppSettings!);
            if (win.ShowDialog() == true)
            {
                App.ApplySettings();
            }
        }

        private void OpenManagementSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה להגדרות ניהול."))
                return;

            var dialog = new ManagementSettingsWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenNewProject_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireManagementAccess("אין לך הרשאה ליצירת פרויקט חדש."))
                return;
            NavigateToView(new CreateProjectUserControl());
        }

        private void OpenTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireManagementAccess("אין לך הרשאה לעריכת תבניות."))
                return;
            NavigateToView(new WindowEditProject());
        }

        private void Approve_Click(object sender, RoutedEventArgs e)
            => NavigateToView(new ProjectFolderTreeView());

        private void OpenProjectWork2_Click(object sender, RoutedEventArgs e)
            => NavigateToView(_cachedProjectWorkView ??= new ProjectWorkView());

        private void Control_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("בקרה");
        }

        private void ManageWidth_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("ניהול רוחב");
        }

        private void ManageEmployees_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("ניהול עובדים");
        }

        private void General_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("כללי");
        }

        private void OpenEmailManagement_Click(object sender, RoutedEventArgs e)
        {
            // NOTE: do NOT clear ActiveProjectContext here.
            // The active project must persist across windows; the EmailManagementViewModel
            // auto-selects it once reference data has loaded.
            if (_cachedEmailManagementView == null)
            {
                _cachedEmailManagementView = new EmailManagementView();
                var googleService = App.ServiceProvider.GetRequiredService<GoogleService>();
                _cachedEmailManagementView.DataContext = new EmailManagementViewModel(googleService);
            }
            // Inbox-originated entry: ensure no stale task context lingers from
            // a previous task-driven navigation. MoveToProject must behave as
            // pure inbox filing here (no TaskCompletionCoordinator call).
            if (_cachedEmailManagementView.DataContext is EmailManagementViewModel inboxVm)
            {
                inboxVm.ActiveTaskContext = null;
            }
            NavigateToView(_cachedEmailManagementView);
        }

        /// <summary>
        /// Navigates to EmailManagement view from a pending-email link in the task panel.
        /// Looks up the email subject from DB so the user knows which email to find.
        /// The view is shown immediately; project pre-selection happens after async data loads.
        /// </summary>
        public async void NavigateToEmail(int emailId)
        {
            await NavigateToEmailAsync(emailId, taskContext: null);
        }

        /// <summary>
        /// Task-aware overload. When <paramref name="taskContext"/> is non-null,
        /// the EmailManagementViewModel will report task completion to
        /// <c>ITaskCompletionCoordinator</c> after a successful MoveToProject run
        /// (event: <c>ReviewCompletionEvents.ReviewMaterialFiled</c>). When null,
        /// behavior is identical to the inbox-originated path.
        /// </summary>
        public async void NavigateToEmail(int emailId, SiNetSQL.Services.Tasks.EmailFilingTaskContext? taskContext)
        {
            await NavigateToEmailAsync(emailId, taskContext);
        }

        private async Task NavigateToEmailAsync(int emailId, SiNetSQL.Services.Tasks.EmailFilingTaskContext? taskContext)
        {
            try
            {
                // Task-driven opens prefer the New System work-surface launcher (exact target, no browse fallback).
                if (taskContext is not null
                    && App.ServiceProvider.GetService<SiNet.App.Wpf.WorkSurfaces.IWorkSurfaceLauncher>() is { } launcher)
                {
                    var opened = await launcher.TryOpenFromTaskAsync(taskContext.TaskId).ConfigureAwait(true);
                    if (opened)
                        return;

                    MessageBox.Show(
                        $"לא ניתן לפתוח את משימה #{taskContext.TaskId} דרך WorkSurfaceLauncher.\n" +
                        "אין fallback למסך המייל המלא מנתיב משימה.",
                        "פתיחת משימת מייל",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Browse / non-task path: legacy EmailManagementView.
                string? emailSubject = null;
                string? messageUniqueId = null;
                var dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
                using var ctx = dbFactory.CreateDbContext();
                var email = ctx.EmailInboxMessages.FirstOrDefault(e => e.Id == emailId);
                emailSubject = email?.Subject;
                messageUniqueId = email?.MessageUniqueId;

                if (_cachedEmailManagementView == null)
                {
                    _cachedEmailManagementView = new EmailManagementView();
                    var googleService = App.ServiceProvider.GetRequiredService<GoogleService>();
                    _cachedEmailManagementView.DataContext = new EmailManagementViewModel(googleService);
                }

                var emailVm = (EmailManagementViewModel)_cachedEmailManagementView.DataContext!;
                emailVm.ActiveTaskContext = taskContext;

                if (!string.IsNullOrEmpty(messageUniqueId))
                    emailVm.RequestEmailSelection(messageUniqueId);

                NavigateToView(_cachedEmailManagementView);

                var hint = !string.IsNullOrWhiteSpace(emailSubject)
                    ? $"{_defaultTitle} — 📧 {emailSubject}"
                    : $"{_defaultTitle} — 📧 מייל #{emailId}";
                Title = hint;

                await emailVm.DataLoadedTask;
            }
            catch (Exception ex)
            {
                AppLogger.ErrorWithContext(ex, "NavigateToEmail failed", new { EmailId = emailId });
                MessageBox.Show(
                    $"פתיחת מייל #{emailId} נכשלה: {ex.Message}",
                    "שגיאת ניווט",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenTaskPanel_Click(object sender, RoutedEventArgs e)
        {
            // Active project persists across views; do not clear it here.
            NavigateToView(new TaskPanelView());
        }

        // ─────────────────────────────────────────────────────────────
        //  Floating Window Handlers (singleton pattern)
        // ─────────────────────────────────────────────────────────────

        private void OpenFloatingTaskPanel_Click(object sender, RoutedEventArgs e)
        {
            // Singleton: reuse existing window if still open
            if (_floatingTasksWindow is { IsLoaded: true })
            {
                _floatingTasksWindow.Activate();
                return;
            }

            _floatingTasksWindow = new FloatingProjectTasksView();
            _floatingTasksWindow.Owner = this;
            _floatingTasksWindow.Closed += (_, _) => _floatingTasksWindow = null;
            _floatingTasksWindow.Show();
        }

        private void OpenFloatingInspection_Click(object sender, RoutedEventArgs e)
        {
            // Singleton: reuse existing window if still open
            if (_floatingInspectionWindow is { IsLoaded: true })
            {
                _floatingInspectionWindow.Activate();
                return;
            }

            _floatingInspectionWindow = new FloatingInspectionView();
            _floatingInspectionWindow.Owner = this;
            _floatingInspectionWindow.Closed += (_, _) => _floatingInspectionWindow = null;
            _floatingInspectionWindow.Show();
        }

        private void OpenTemplateValidation_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WPF_Window.TemplateValidationWindow { Owner = this };
            dialog.ShowDialog();
        }

        private void OpenQuickStamp_Click(object sender, RoutedEventArgs e)
        {
            var window = new WPF_Window.QuickStampWindow { Owner = this };
            window.ShowDialog();
        }

        public void ShowFloatingInspection()
        {
            if (_floatingInspectionWindow is { IsLoaded: true })
            {
                _floatingInspectionWindow.Activate();
                return;
            }

            _floatingInspectionWindow = new FloatingInspectionView();
            _floatingInspectionWindow.Owner = this;
            _floatingInspectionWindow.Closed += (_, _) => _floatingInspectionWindow = null;
            _floatingInspectionWindow.Show();
        }

        /// <summary>
        /// Shows (or reuses) the floating inspection window and returns it so a
        /// caller can drive workflow task-mode opening via
        /// <see cref="WPFUserControl.FloatingInspectionView.ViewModel"/>. Mirrors
        /// <see cref="ShowFloatingInspection"/> but exposes the window instance.
        /// </summary>
        public WPFUserControl.FloatingInspectionView ShowFloatingInspectionWindow()
        {
            if (_floatingInspectionWindow is { IsLoaded: true })
            {
                _floatingInspectionWindow.Activate();
                return _floatingInspectionWindow;
            }

            _floatingInspectionWindow = new FloatingInspectionView();
            _floatingInspectionWindow.Owner = this;
            _floatingInspectionWindow.Closed += (_, _) => _floatingInspectionWindow = null;
            _floatingInspectionWindow.Show();
            return _floatingInspectionWindow;
        }

        public void ShowProjectWork()
        {
            NavigateToView(_cachedProjectWorkView ??= new ProjectWorkView());
        }

        // ─────────────────────────────────────────────────────────────
        //  Dialog Handlers
        // ─────────────────────────────────────────────────────────────

        private void OpenProjectTypeRules_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לכללי סוגי פרויקט."))
                return;

            var dialog = new ProjectTypeRulesWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenStatusMapping_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה למיפוי סטטוסים."))
                return;

            var dialog = new StatusMappingWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenActionPermissions_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לניהול הרשאות פעולה."))
                return;

            var dialog = new ActionPermissionWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenMigrationPoc_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לכלי מיגרציה."))
                return;

            var dialog = new MigrationPocWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Developer/preview entry point (Phase 5 of the Inspection migration). Hosts the new
        /// <see cref="SiNet.App.Wpf.Inspection.InspectionShellView"/> (built in the SiNet.App.Wpf
        /// preview harness) inside this production host so it can be exercised with LIVE legacy data
        /// through the already-bound <c>ILegacyInspectionSource</c> seam. This is additive and does
        /// NOT replace or alter the legacy floating Inspection window
        /// (<see cref="OpenFloatingInspection_Click"/>). Read-only: the new shell currently shows
        /// series, reports, and notes only — no editing/generation/sent-locked actions.
        /// </summary>
        private void OpenNewInspectionPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לתצוגה מקדימה."))
                return;

            // Resolve the new shell view from DI so its full view-model graph (and the live
            // IInspectionWorkspace bound in this host) is constructed for us.
            var shellView = App.ServiceProvider.GetRequiredService<SiNet.App.Wpf.Inspection.InspectionShellView>();

            // Ask which project's inspection series to load so the preview shows live data. The new
            // tree only loads when LoadSeriesAsync is called; if no/invalid id is given we still open
            // the shell (empty tree) so the new screen structure is visible.
            var prompt = new Dialogs.TextInputDialog(
                "Inspection (Preview)",
                "מספר פרויקט להצגת סדרות בדיקה (השאר ריק לפתיחת המסך החדש ללא נתונים):")
            {
                Owner = this
            };

            if (prompt.ShowDialog() == true
                && int.TryParse(prompt.ResponseText?.Trim(), out var projectId)
                && projectId > 0
                && shellView.DataContext is SiNet.App.Wpf.Inspection.InspectionShellViewModel shellVm)
            {
                _ = shellVm.Tree.LoadSeriesAsync(projectId);
            }

            var previewWindow = new Window
            {
                Title = "Inspection (Preview) — מסך חדש (קריאה בלבד)",
                Content = shellView,
                Owner = this,
                Width = 1000,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            previewWindow.Show();
        }

        /// <summary>
        /// Developer/preview entry point for the new Inspection VISUAL CLONE
        /// (<see cref="SiNet.App.Wpf.Surfaces.Inspection.InspectionWindowView"/>) — the visual clone of
        /// the legacy floating Inspection window. This opens the window with its built-in
        /// fake/design-time data ONLY so the visual structure (header chrome, create-report strip,
        /// report action row, metadata row, questionnaire tree, report cards, status bar) can be
        /// reviewed. It is intentionally NOT wired to any data or behavior: no DB, no
        /// <c>IInspectionReportService</c>, no <c>IInspectionWorkspace</c>, no workflow/task completion,
        /// no Gmail, no ACC. All buttons are stubbed. This does NOT replace the legacy floating
        /// Inspection window and is not promoted to production UX.
        /// </summary>
        private void OpenInspectionVisualClone_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לתצוגה מקדימה."))
                return;

            // The visual clone is a self-contained Window with a parameterless constructor that loads
            // its own fake/design-time data. No DI, no DataContext wiring, no live services.
            var cloneWindow = new SiNet.App.Wpf.Surfaces.Inspection.InspectionWindowView
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            cloneWindow.Show();
        }

        /// <summary>
        /// Developer/preview entry point for the new Email VISUAL CLONE
        /// (<see cref="SiNet.App.Wpf.Surfaces.Email.EmailWindowView"/>) — the visual clone of the legacy
        /// <c>EmailManagementView</c>. This opens the window with its built-in fake/design-time data ONLY
        /// so the visual structure (status + filters strip, selected-project info strip, email list,
        /// email viewer with attachments + body + action bar, context/calendar placeholder, status bar)
        /// can be reviewed. It is intentionally NOT wired to any data or behavior: no DB, no Gmail/Outlook,
        /// no email services, no ACC, no project linking, no task creation, no workflow. All buttons are
        /// stubbed. This does NOT replace the legacy <c>EmailManagementView</c> and is not promoted to
        /// production UX.
        /// </summary>
        private void OpenEmailVisualClone_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לתצוגה מקדימה."))
                return;

            // Resolve the window through the shared IEmailWindowFactory so every Email window binds to the
            // SAME application-wide ICurrentProjectContext (singleton). Selecting a project in one window is
            // therefore observed by the others. Still fake-data only: no DB, no Gmail, no workflow, no email
            // filtering — the selector uses the fake IProjectQueryService. This does NOT replace the legacy
            // EmailManagementView and is not promoted to production UX.
            var factory = App.ServiceProvider.GetRequiredService<SiNet.App.Wpf.Shared.Projects.IEmailWindowFactory>();
            var cloneWindow = factory.Create();
            cloneWindow.Owner = this;
            cloneWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            cloneWindow.Show();
        }

        /// <summary>
        /// Developer/preview entry: opens Inspection via <c>IWorkSurfaceLauncher</c> (canonical
        /// <c>ITaskNavigationService</c> → <c>WorkSurfaceContext</c> → InspectionWindow).
        /// No first/last fallback; failure shows a clear dialog.
        /// </summary>
        private async void OpenInspectionFromTask_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לתצוגה מקדימה."))
                return;

            var prompt = new Dialogs.TextInputDialog(
                "Inspection (Task)",
                "מספר משימה (taskId) לפתיחת הדוח המדויק דרך תזרים משימות:")
            {
                Owner = this
            };

            if (prompt.ShowDialog() != true
                || !int.TryParse(prompt.ResponseText?.Trim(), out var taskId)
                || taskId <= 0)
            {
                return;
            }

            var launcher = App.ServiceProvider.GetService<SiNet.App.Wpf.WorkSurfaces.IWorkSurfaceLauncher>();
            if (launcher is null)
            {
                MessageBox.Show(
                    "IWorkSurfaceLauncher אינו רשום ב-DI.",
                    "Inspection (Task)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var opened = await launcher.TryOpenFromTaskAsync(taskId).ConfigureAwait(true);
            if (!opened)
            {
                MessageBox.Show(
                    $"לא ניתן לפתוח משימה #{taskId}. אין fallback.",
                    "Inspection (Task)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenProjectDecisions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProjectDecisionsWindow();
            dialog.Owner = this;
            dialog.Show();
        }

        private void OpenR01Report_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireManagementAccess("אין לך הרשאה לדוחות."))
                return;

            var dialog = new R01ReportDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenR02Report_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireManagementAccess("אין לך הרשאה לדוחות."))
                return;

            var dialog = new R02ReportDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenR03Report_Click(object sender, RoutedEventArgs e)
        {
            // R03 is available to all employees (Employee, Management, Administrator)
            // — no Management/Admin guard required per Authorization Principles.
            var dialog = new R03ReportDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        // ─────────────────────────────────────────────────────────────
        //  Admin-Only Handlers
        // ─────────────────────────────────────────────────────────────

        private void OpenDwfAnalysis_Click(object sender, RoutedEventArgs e)
        {
            // Admin-only: DWF Stamp Analysis Tool
            if (!RequireAdminAccess("אין לך הרשאה לכלי ניתוח DWF."))
                return;

            DwfAnalysisHelper.RunInteractiveComparison();
        }

        private void OpenSystemHealth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = App.ServiceProvider;
                var health = sp.GetRequiredService<SiNetSQL.Services.Health.ISystemHealthService>();
                var window = new Views.SystemHealthWindow(health) { Owner = this };
                window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "כשל בפתיחת חלון מצב המערכת:\n" + ex.Message,
                    "מצב מערכת", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            // Double-check authorization (UI should already hide this for non-admins)
            if (!RequireAdminAccess("אין לך הרשאה להוסיף משתמשים."))
                return;

            var dialog = new AddUserWindow();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                MessageBox.Show(
                    "המשתמש נוסף בהצלחה.",
                    "הצלחה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OpenUserManagement_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לניהול משתמשים."))
                return;

            var dialog = new UserManagementWindow();
            dialog.Owner = this;
            dialog.Show();
        }

        private void OpenMasterPlanMapping_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לכלי מיפוי."))
                return;

            var dialog = new MasterPlanMappingWindow();
            dialog.Owner = this;
            dialog.Show();
        }

        private void OpenSecretSetup_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה להגדרת סודות."))
                return;

            var dialog = new SecretSetupWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenWorkflowDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Active project persists across views; do not clear it here.
            NavigateToView(new WorkflowDashboardView());
        }

        private void OpenWorkflowStatusDashboard_Click(object sender, RoutedEventArgs e)
        {
            var window = new WPF_Window.WorkflowStatusMonitorWindow { Owner = this };
            window.Show();
        }

        private void OpenWorkflowManagement_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdminAccess("אין לך הרשאה לניהול תהליכים."))
                return;

            var dialog = new WorkflowManagementWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

#if DEBUG
        /// <summary>
        /// DEBUG-only handler that wipes all data from the migration-introduced (new) tables.
        /// Triple-gated:
        ///   1. Compiled only in DEBUG builds.
        ///   2. UI element only visible to allowed Windows users (see DevDataResetService).
        ///   3. Requires explicit double-confirmation by the user.
        /// </summary>
        private async void DevResetData_Click(object sender, RoutedEventArgs e)
        {
            // Hard re-check at click time (defense in depth — the menu item could be made visible by mistake).
            if (!SiNetSQL.Services.DevDataResetService.IsCurrentUserAllowed())
            {
                MessageBox.Show(
                    $"פעולה זו אינה זמינה למשתמש הנוכחי ({SiNetSQL.Services.DevDataResetService.CurrentWindowsUser}).",
                    "גישה נדחתה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Authorization gate: must be FullAccess (Role >= Management). Checked here
            // BEFORE the user is asked to confirm, so they get a clean message.
            if (!SiNetSQL.Services.CurrentUserContext.Instance.IsFullAccess)
            {
                MessageBox.Show(
                    "פעולה זו דורשת הרשאת Full Access (Role >= Management).",
                    "גישה נדחתה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string dbName;
            try
            {
                var factoryPeek = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
                using var ctxPeek = factoryPeek.CreateDbContext();
                dbName = ctxPeek.Database.GetDbConnection().Database;
            }
            catch
            {
                dbName = "(unknown)";
            }

            // ── Consolidated Reset Options Dialog ────────────────────────────
            // LEGACY DISABLED 2026-05-20: reset confirmations were consolidated into
            // ResetOptionsDialog. The previous three sequential MessageBox prompts
            // (initial confirm / final irreversible confirm / wipe SystemSettings)
            // are kept here as commented-out blocks for reference only and are
            // candidates for deletion after validation.
            //
            //   1. var firstConfirm = MessageBox.Show("⚠️ אזהרה ⚠️ ...", default No);
            //   2. var secondConfirm = MessageBox.Show("זו פעולה בלתי הפיכה ...", default No);
            //   3. var wipeBootstrap = MessageBox.Show("האם למחוק גם את טבלת ה-Bootstrap ...", default No);
            // ─────────────────────────────────────────────────────────────────

            var optionsDialog = new SiNetProjectManagerV2.Dialogs.ResetOptionsDialog(
                dbName,
                SiNetSQL.Services.DevDataResetService.CurrentWindowsUser)
            {
                Owner = this,
            };

            var dialogResult = optionsDialog.ShowDialog();
            if (dialogResult != true || !optionsDialog.UserApproved)
                return;

            bool preserveSystemSettings = !optionsDialog.WipeSystemSettings;
            bool resetUserSettings = optionsDialog.ResetUserSettings;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var factory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
                var report = await SiNetSQL.Services.DevDataResetService.ResetAsync(
                    factory,
                    preserveSystemSettings,
                    resetUserSettings);

                var summary =
                    $"איפוס הסתיים.\n\n" +
                    $"מסד נתונים: {report.DatabaseName}\n" +
                    $"טבלאות שעובדו: {report.TableResults.Count}\n" +
                    $"שורות שנמחקו: {report.TotalRowsDeleted}\n" +
                    $"כשלים: {report.FailedTableCount}\n" +
                    $"משך: {report.Duration.TotalSeconds:F1} שניות\n" +
                    $"Bootstrap/SystemSettings: {(report.SystemSettingsPreserved ? "נשמר ✅" : "נמחק")}\n" +
                    $"UserGroups/Memberships: {(report.UserSettingsPreserved ? "נשמרו ✅" : "נמחקו")}";

                if (report.FailedTableCount > 0)
                {
                    var failed = string.Join("\n",
                        report.TableResults.Where(r => r.Error != null).Select(r => $"• {r.Table}: {r.Error}"));
                    summary += $"\n\nטבלאות שנכשלו:\n{failed}";
                }

                if (!string.IsNullOrEmpty(report.PostResetError))
                {
                    summary += $"\n\n⚠️ שגיאה בהפעלת FK מחדש: {report.PostResetError}";
                }

                if (report.SeedApplied)
                {
                    summary += "\n\n✅ נתוני סיד בסיסיים הוטענו מחדש.";
                }
                else if (!string.IsNullOrEmpty(report.SeedError))
                {
                    summary += $"\n\n⚠️ שגיאה בטעינת נתוני סיד: {report.SeedError}";
                }

                if (report.MappingsApplied)
                {
                    summary += "\n✅ מיפויי ProjectType↔TaskType / Status הוטענו מחדש.";
                }
                else if (!string.IsNullOrEmpty(report.MappingsError))
                {
                    summary += $"\n⚠️ שגיאה במיפויי ברירת מחדל: {report.MappingsError}";
                }

                if (report.WorkflowSeedApplied)
                {
                    summary += "\n✅ Workflow + UserGroups + הפעלות שלבים/דיסציפלינות הוטענו.";
                }
                else if (!string.IsNullOrEmpty(report.WorkflowSeedError))
                {
                    summary += $"\n⚠️ שגיאה בטעינת ה-Workflow: {report.WorkflowSeedError}";
                }

                MessageBox.Show(
                    summary,
                    "איפוס נתוני פיתוח — הסתיים",
                    MessageBoxButton.OK,
                    report.FailedTableCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "DevDataResetService failed");
                MessageBox.Show(
                    $"איפוס הנתונים נכשל:\n\n{ex.Message}",
                    "שגיאה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
#else
        // Release-build stub: the menu item is hidden (Visibility="Collapsed" in XAML
        // and never made visible outside DEBUG), but the XAML compiler still requires
        // the Click handler symbol to exist on the partial class.
        private void DevResetData_Click(object sender, RoutedEventArgs e) { }
#endif
    }
}