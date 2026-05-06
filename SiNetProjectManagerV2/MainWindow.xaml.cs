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
namespace SiNetProjectManagerV2
{
    public partial class MainWindow : BaseWindow
    {
        private static readonly string _appVersion = GetAppVersion();
        private static readonly string _defaultTitle = $"תוכנת ניהול   v{_appVersion}";
        private FloatingProjectTasksView? _floatingTasksWindow;
        private FloatingInspectionView? _floatingInspectionWindow;

        public MainWindow()
        {
            InitializeComponent(); // חובה כדי לטעון את ה־XAML
            Title = _defaultTitle;

            // Dynamic title: reflect the currently active project
            ActiveProjectContext.Instance.ActiveProjectChanged += OnActiveProjectChanged;

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
                Title = project != null
                    ? $"{_defaultTitle} - {project.Title}"
                    : _defaultTitle;
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

            // First confirmation — explicit warning with target DB name.
            var firstConfirm = MessageBox.Show(
                $"⚠️ אזהרה ⚠️\n\n" +
                $"פעולה זו תמחק את כל המידע מכל הטבלאות החדשות שנוצרו במיגריישנים\n" +
                $"(מיילים, ACC, סטטוסים, החלטות, ביקורות, תהליכי עבודה, משימות, קבוצות וכו').\n\n" +
                $"מסד הנתונים: {dbName}\n" +
                $"משתמש: {SiNetSQL.Services.DevDataResetService.CurrentWindowsUser}\n\n" +
                $"האם להמשיך?",
                "איפוס נתוני פיתוח",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (firstConfirm != MessageBoxResult.Yes)
                return;

            // Second confirmation — final.
            var secondConfirm = MessageBox.Show(
                $"זו פעולה בלתי הפיכה.\n\nלאשר מחיקה סופית של כל הנתונים מהטבלאות החדשות במסד '{dbName}'?",
                "אישור סופי",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop,
                MessageBoxResult.No);

            if (secondConfirm != MessageBoxResult.Yes)
                return;

            // Third question — should the Bootstrap / SystemSettings table also be wiped?
            // Default = No: keep configuration (ACC admin email, stamp paths, default project IDs,
            // logging, etc.) so the app stays usable after the reset.
            var wipeBootstrap = MessageBox.Show(
                "האם למחוק גם את טבלת ה-Bootstrap / SystemSettings (הגדרות מערכת)?\n\n" +
                "• ברירת מחדל: לא — ההגדרות יישמרו.\n" +
                "• בחירה ב-'כן' תמחק גם הגדרות מערכת.",
                "שמירת Bootstrap",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            bool preserveSystemSettings = wipeBootstrap != MessageBoxResult.Yes;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var factory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
                var report = await SiNetSQL.Services.DevDataResetService.ResetAsync(factory, preserveSystemSettings);

                var summary =
                    $"איפוס הסתיים.\n\n" +
                    $"מסד נתונים: {report.DatabaseName}\n" +
                    $"טבלאות שעובדו: {report.TableResults.Count}\n" +
                    $"שורות שנמחקו: {report.TotalRowsDeleted}\n" +
                    $"כשלים: {report.FailedTableCount}\n" +
                    $"משך: {report.Duration.TotalSeconds:F1} שניות\n" +
                    $"Bootstrap/SystemSettings: {(report.SystemSettingsPreserved ? "נשמר ✅" : "נמחק")}";

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
#endif
    }
}