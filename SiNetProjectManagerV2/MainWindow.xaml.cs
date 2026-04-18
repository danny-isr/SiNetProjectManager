using SiNetProjectManagerV2.WPF;

using SiNetProjectManagerV2.WPFUserControl;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.WPF_Window;
using System.ComponentModel;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WpfSiData.WPFUserControl;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiNetSQL.Data;
using SiOffice.GoogleConnector;

namespace SiNetProjectManagerV2
{
    public partial class MainWindow : BaseWindow
    {
        private const string _defaultTitle = "תוכנת ניהול";
        private FloatingProjectTasksView? _floatingTasksWindow;
        private FloatingInspectionView? _floatingInspectionWindow;

        public MainWindow()
        {
            InitializeComponent(); // חובה כדי לטעון את ה־XAML

            // Dynamic title: reflect the currently active project
            ActiveProjectContext.Instance.ActiveProjectChanged += OnActiveProjectChanged;
        }

        private void OnActiveProjectChanged(SiNetSQL.Models.Project? project)
        {
            Dispatcher.Invoke(() =>
            {
                Title = project != null
                    ? $"{_defaultTitle} - {project.Title}"
                    : _defaultTitle;
            });
        }

        /// <summary>
        /// Intercepts application close. If background ACC operations (ingestion or external
        /// downloads) are in progress, shows a status dialog with count and options to wait,
        /// force-close, or cancel.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (EmailManagementViewModel.HasActiveUploads)
            {
                var dialog = new BackgroundUploadsDialog { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    e.Cancel = true;
                    return;
                }
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
        /// Verifies the current user has full (admin) access.
        /// Shows a localized denial message if not authorized.
        /// </summary>
        /// <returns>True if user has admin access, false if denied.</returns>
        private static bool RequireAdminAccess(string deniedMessage)
        {
            if (CurrentUserContext.Instance.IsFullAccess)
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
            var dialog = new ManagementSettingsWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenNewProject_Click(object sender, RoutedEventArgs e)
            => NavigateToView(new CreateProjectUserControl());

        private void OpenTemplate_Click(object sender, RoutedEventArgs e)
            => NavigateToView(new WindowEditProject());

        private void Approve_Click(object sender, RoutedEventArgs e)
            => NavigateToView(new ProjectFolderTreeView());

        private void OpenProjectWork2_Click(object sender, RoutedEventArgs e)
            => NavigateToView(new ProjectWorkView());

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
            ActiveProjectContext.Instance.Clear();
            var emailView = new EmailManagementView();
            var googleService = App.ServiceProvider.GetRequiredService<GoogleService>();
            emailView.DataContext = new EmailManagementViewModel(googleService);
            NavigateToView(emailView);
        }

        /// <summary>
        /// Navigates to EmailManagement view from a pending-email link in the task panel.
        /// Looks up the email subject from DB so the user knows which email to find.
        /// The view is shown immediately; project pre-selection happens after async data loads.
        /// </summary>
        public async void NavigateToEmail(int emailId)
        {
            try
            {
                // Look up email subject and MessageUniqueId for auto-selection
                string? emailSubject = null;
                string? messageUniqueId = null;
                var dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
                using var ctx = dbFactory.CreateDbContext();
                var email = ctx.EmailInboxMessages.FirstOrDefault(e => e.Id == emailId);
                emailSubject = email?.Subject;
                messageUniqueId = email?.MessageUniqueId;

                // Create ViewModel (instant — constructor no longer blocks on DB queries)
                var googleService = App.ServiceProvider.GetRequiredService<GoogleService>();
                var emailVm = new EmailManagementViewModel(googleService);

                if (!string.IsNullOrEmpty(messageUniqueId))
                {
                    emailVm.RequestEmailSelection(messageUniqueId);
                }

                // Navigate immediately so the user sees the view while data loads
                var emailView = new EmailManagementView();
                emailView.DataContext = emailVm;
                NavigateToView(emailView);

                // Title hint as fallback if email is not on current page
                var hint = !string.IsNullOrWhiteSpace(emailSubject)
                    ? $"{_defaultTitle} — 📧 {emailSubject}"
                    : $"{_defaultTitle} — 📧 מייל #{emailId}";
                Title = hint;

                // Wait for reference data to load, then pre-select the active project
                await emailVm.DataLoadedTask;

                var activeProjectId = ActiveProjectContext.Instance.ActiveProjectId;
                if (activeProjectId.HasValue)
                {
                    var matchingProject = emailVm.Projects.FirstOrDefault(p => p.Id == activeProjectId.Value);
                    if (matchingProject != null)
                    {
                        emailVm.SelectedProject = matchingProject;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.ErrorWithContext(ex, "NavigateToEmail failed", new { EmailId = emailId });
                // Fallback: just open EmailManagement without guidance
                OpenEmailManagement_Click(this, new RoutedEventArgs());
            }
        }

        private void OpenTaskPanel_Click(object sender, RoutedEventArgs e)
        {
            ActiveProjectContext.Instance.Clear();
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

        // ─────────────────────────────────────────────────────────────
        //  Dialog Handlers
        // ─────────────────────────────────────────────────────────────

        private void OpenProjectTypeRules_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProjectTypeRulesWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenStatusMapping_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new StatusMappingWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenActionPermissions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ActionPermissionWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenMigrationPoc_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MigrationPocWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenProjectDecisions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProjectDecisionsWindow();
            dialog.Owner = this;
            dialog.Show();
        }

        private void OpenR01Report_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new R01ReportDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenR02Report_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new R02ReportDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenR03Report_Click(object sender, RoutedEventArgs e)
        {
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
            var dialog = new SecretSetupWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenWorkflowDashboard_Click(object sender, RoutedEventArgs e)
        {
            ActiveProjectContext.Instance.Clear();
            NavigateToView(new WorkflowDashboardView());
        }

        private void OpenWorkflowStatusDashboard_Click(object sender, RoutedEventArgs e)
        {
            var window = new WPF_Window.WorkflowStatusMonitorWindow { Owner = this };
            window.Show();
        }

        private void OpenWorkflowManagement_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WorkflowManagementWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
