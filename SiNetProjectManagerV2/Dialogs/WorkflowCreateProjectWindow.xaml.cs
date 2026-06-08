using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiNetSQL.Services.Tasks;
using SiNetSQL.Services.Workflow;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Project-creation dialog: email preview (top) + project creation form (bottom).
/// <para>
/// This window is project-creation only. Filing of the originating email's
/// attachments belongs to the dedicated <c>FileQuoteMaterial</c> task in the
/// <c>PRP.FileMaterial</c> stage (or the equivalent filing tasks in other
/// workflows) and is handled by the canonical email-filing host through the
/// existing MoveToProject pipeline. The dialog never drives MoveToProject.
/// </para>
/// <para>
/// When opened from a task (e.g. <c>OpenQuoteProject</c>) via
/// <see cref="EmailFilingTaskContext"/>, the originating task is closed by the
/// existing project-creation completion path (TaskCompletionPolicy.ProjectCreated)
/// triggered by <see cref="CreateProjectViewModel"/> when it persists the project.
/// </para>
/// </summary>
public partial class WorkflowCreateProjectWindow : Window
{
    private readonly int _emailMessageId;
    private readonly EmailFilingTaskContext? _taskContext;

    private bool _projectCreated;
    private CreateProjectViewModel? _createProjectVm;

    /// <summary>Standalone constructor (no task context).</summary>
    public WorkflowCreateProjectWindow(int emailMessageId, Window? owner = null)
        : this(emailMessageId, taskContext: null, owner)
    {
    }

    /// <summary>
    /// Task-mode constructor. The window behaves identically to standalone mode
    /// (project creation + Gmail label + continuation workflow). The originating
    /// task is closed via the existing ProjectCreated completion policy in
    /// <see cref="CreateProjectViewModel"/>. The <paramref name="taskContext"/>
    /// is kept so the task host can refresh after creation.
    /// </summary>
    public WorkflowCreateProjectWindow(
        int emailMessageId,
        EmailFilingTaskContext? taskContext,
        Window? owner = null)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;
        _taskContext = taskContext;

        if (owner != null) Owner = owner;

        LoadEmailData();
        LoadProjectForm();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Email Preview (top panel)
    // ════════════════════════════════════════════════════════════════════════

    private void LoadEmailData()
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var db = dbFactory.CreateDbContext();

            var email = db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.Id == _emailMessageId)
                .Select(m => new { m.Id, m.Subject, m.FromAddress, m.ReceivedUtc, m.MessageUniqueId })
                .FirstOrDefault();

            if (email != null)
            {
                Title = $"🆕 יצירת פרויקט — {email.Subject}";
                SubjectText.Text = $"📧 {email.Subject ?? "(ללא נושא)"}";
                FromText.Text = $"מאת: {email.FromAddress}";
                DateText.Text = $"תאריך: {email.ReceivedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";

                if (!string.IsNullOrEmpty(email.MessageUniqueId))
                {
                    LoadEmailBodyAsync(email.MessageUniqueId);
                }
            }

            var message = db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.Id == _emailMessageId)
                .Select(m => new { m.InboxAccProjectId, m.InboxAccFolderId })
                .FirstOrDefault();

            var attachments = db.EmailInboxAttachments
                .AsNoTracking()
                .Where(a => a.MessageId == _emailMessageId)
                .OrderBy(a => a.AttachmentIndex)
                .Select(a => new AttachmentDisplayItem
                {
                    Id = a.Id,
                    FileName = a.OriginalFileName ?? a.SavedFileName ?? $"קובץ #{a.AttachmentIndex}",
                    AccItemId = a.AccItemId,
                    AccVersionId = a.AccVersionId,
                    InboxAccProjectId = message!.InboxAccProjectId,
                    InboxAccFolderId = message.InboxAccFolderId,
                })
                .ToList();

            if (attachments.Count > 0)
            {
                AttachmentsHeader.Text = $"📎 קבצים מצורפים ({attachments.Count}):";
                AttachmentsList.ItemsSource = attachments;
            }
            else
            {
                AttachmentsHeader.Text = "📎 אין קבצים מצורפים";
            }
        }
        catch (Exception ex)
        {
            SubjectText.Text = $"שגיאה: {ex.Message}";
        }
    }

    private void OpenAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AttachmentDisplayItem att }) return;

        if (string.IsNullOrEmpty(att.AccItemId))
        {
            MessageBox.Show("הקובץ עדיין לא הועלה ל-ACC.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(att.InboxAccProjectId))
        {
            MessageBox.Show("מזהה פרויקט ACC לא נמצא.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var projectGuid = att.InboxAccProjectId.StartsWith("b.", StringComparison.Ordinal)
            ? att.InboxAccProjectId[2..] : att.InboxAccProjectId;

        var url = $"https://acc.autodesk.com/docs/files/projects/{projectGuid}";
        if (!string.IsNullOrEmpty(att.InboxAccFolderId))
            url += $"?folderUrn={Uri.EscapeDataString(att.InboxAccFolderId)}&entityId={Uri.EscapeDataString(att.AccItemId)}";

        try
        {
            var viewer = new ExternalBrowserWindow(url, null)
            {
                Title = $"📄 {att.FileName}",
                Width = 1200,
                Height = 800
            };
            viewer.Show();
        }
        catch
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Project Creation Form
    // ════════════════════════════════════════════════════════════════════════

    private void LoadProjectForm()
    {
        var control = new WpfSiData.WPFUserControl.CreateProjectUserControl(_emailMessageId);
        ProjectFormHost.Content = control;

        if (control.DataContext is CreateProjectViewModel vm)
        {
            _createProjectVm = vm;
            vm.ProjectCreated += OnProjectCreated;
        }
    }

    private void OnProjectCreated(ProjectCreatedEventArgs args)
    {
        // Defense in depth: ignore any second ProjectCreated event (the VM
        // could re-raise on resubmit). The window closes on the first one.
        if (_projectCreated)
        {
            AppLogger.Warn(
                $"[WorkflowCreateProject] Ignoring duplicate ProjectCreated event " +
                $"(incoming projectId={args.ProjectId}).");
            return;
        }
        _projectCreated = true;

        if (_createProjectVm is not null)
        {
            _createProjectVm.ProjectCreated -= OnProjectCreated;
        }

        if (!string.IsNullOrEmpty(args.GmailMessageId))
        {
            ApplyGmailLabelAsync(args);
        }

        // LEGACY DISABLED 2026-05-22: Continuation workflow must not start
        // immediately after project creation in Proposal. New rule: continue
        // Proposal to FileMaterial; start project-type workflows only after
        // quote approval. Candidate for deletion after validation.
        //
        // Standalone callers (no _taskContext) still auto-start the
        // continuation workflow — that path predates Proposal and is not in
        // scope for this fix.
        var isProposalProjectCreation = _taskContext is { ComponentKey: TaskComponentKeys.ProjectCreationFromEmail };
        if (!isProposalProjectCreation)
        {
            StartContinuationWorkflowAsync(args, _emailMessageId);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[WorkflowCreateProject] Skipping continuation workflow for project {args.ProjectId}: " +
                $"opened from Proposal task ({_taskContext!.ComponentKey}). " +
                $"Proposal advances to PRP.FileMaterial via the existing workflow engine.");
        }

        // Ask the task host to refresh its list so the closed task and the
        // newly seeded follow-up tasks (e.g. FileQuoteMaterial) appear.
        if (_taskContext?.OnTaskRefreshRequested is { } refresh)
        {
            try { Application.Current?.Dispatcher.BeginInvoke(refresh); }
            catch { /* best-effort */ }
        }

        // Reuse the existing "שייך לפרויקט" local refresh path so the
        // EmailManagementView (if already opened) updates the in-memory
        // EmailInfo item from Unassigned → Assigned/Filed without any
        // Gmail full refresh or full-mailbox reload. No parallel refresh
        // mechanism is created. When the view has not been opened yet,
        // there is nothing local to update — the standard load path will
        // pick up the assignment from the DB / Gmail label on first open.
        ApplyAssignedToProjectLocalRefresh(args, _emailMessageId);

        DialogResult = true;
    }

    private static void ApplyAssignedToProjectLocalRefresh(ProjectCreatedEventArgs args, int emailMessageId)
    {
        try
        {
            var emailVm = (Application.Current?.MainWindow as MainWindow)
                ?.TryGetCachedEmailManagementViewModel();
            if (emailVm == null)
            {
                AppLogger.LogDebug(
                    "[WorkflowCreateProject] EmailManagementView not yet open — " +
                    "skipping local refresh (no UI state to update).");
                return;
            }

            var displayName = !string.IsNullOrWhiteSpace(args.ProjectName)
                ? args.ProjectName
                : $"#{args.ProjectId}";

            // Fire and forget on the UI thread; the helper itself dispatches.
            _ = emailVm.ApplyEmailAssignedToProjectLocalAsync(
                args.ProjectId, emailMessageId, displayName);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex,
                "[WorkflowCreateProject] Failed to apply local 'assign to project' refresh");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Continuation workflow + Gmail label
    // ════════════════════════════════════════════════════════════════════════

    private static async void StartContinuationWorkflowAsync(ProjectCreatedEventArgs args, int emailMessageId)
    {
        try
        {
            var sp = App.ServiceProvider;
            if (sp == null) return;

            var orchestrator = sp.GetService<WorkflowTaskOrchestrator>();
            var policyService = sp.GetService<ProjectWorkflowPolicyService>();
            if (orchestrator == null || policyService == null)
            {
                AppLogger.Warn(
                    "[WorkflowCreateProject] Workflow services unavailable — skipping auto-start");
                return;
            }

            var allowed = await policyService.GetAllowedWorkflowsAsync(args.ProjectId, CancellationToken.None);
            var definition = allowed.FirstOrDefault(d => d.IsActive);
            if (definition is null)
            {
                AppLogger.Warn(
                    $"[WorkflowCreateProject] No default workflow mapping found for project {args.ProjectId} " +
                    "(ProjectType has no ProjectTypeWorkflowDefinition). Skipping continuation auto-start — " +
                    "no silent fallback.");
                return;
            }

            var userId = CurrentUserContext.Instance.CurrentUserId ?? 0;

            AppLogger.Info(
                $"[WorkflowCreateProject] Starting continuation workflow '{definition.Code}' for project {args.ProjectId}");

            await orchestrator.StartWorkflowAsync(
                definition.Id,
                args.ProjectId,
                WorkflowTriggerType.Email,
                triggerEntityId: emailMessageId == 0 ? null : emailMessageId,
                userId: userId,
                notes: $"Auto-started on project creation from email (continuation: {definition.Code})",
                ct: CancellationToken.None);

            AppLogger.Info(
                $"[WorkflowCreateProject] ✅ Continuation workflow '{definition.Code}' started for project {args.ProjectId}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex,
                "[WorkflowCreateProject] Failed to auto-start continuation workflow");
        }
    }

    private static async void ApplyGmailLabelAsync(ProjectCreatedEventArgs args)
    {
        try
        {
            var google = App.ServiceProvider?.GetService<SiOffice.GoogleConnector.GoogleService>();
            if (google == null)
            {
                AppLogger.Warn("[WorkflowCreateProject] GoogleService not available — skipping label");
                return;
            }

            AppLogger.Info(
                $"[WorkflowCreateProject] Applying label: {args.LocationName}/{args.ProjectName} → gmail msg {args.GmailMessageId}");

            var labelId = await google.GetOrCreateProjectLabelAsync(
                args.LocationName, args.ProjectName);

            await google.AttachProjectLabelAsync(args.GmailMessageId!, labelId);

            AppLogger.Info(
                "[WorkflowCreateProject] ✅ Gmail label applied successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex,
                "[WorkflowCreateProject] Failed to apply Gmail label");
        }
    }

    private async void LoadEmailBodyAsync(string messageUniqueId)
    {
        try
        {
            var google = App.ServiceProvider?.GetService<SiOffice.GoogleConnector.GoogleService>();
            if (google == null) return;

            string? gmailMessageId = messageUniqueId;
            if (!messageUniqueId.StartsWith("gmail:", StringComparison.Ordinal))
            {
                gmailMessageId = await google.ResolveLocalMessageIdByRfc822Async(messageUniqueId);
            }
            else
            {
                gmailMessageId = messageUniqueId["gmail:".Length..];
            }

            if (string.IsNullOrEmpty(gmailMessageId))
            {
                AppLogger.Warn($"Could not resolve Gmail message ID for unique ID: {messageUniqueId}");
                return;
            }

            var fullEmail = await google.LoadFullEmailBodyAsync(gmailMessageId);
            if (fullEmail != null && !string.IsNullOrEmpty(fullEmail.HtmlBody))
            {
                Dispatcher.Invoke(() =>
                {
                    EmailBodyBrowser.Visibility = Visibility.Visible;
                    EmailBodyBrowser.NavigateToString(fullEmail.HtmlBody);
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to load email body in CreateProjectWindow: {ex.Message}");
        }
    }

    private void EmailBodyBrowser_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        SuppressScriptErrors(EmailBodyBrowser, true);
    }

    private static void SuppressScriptErrors(WebBrowser wb, bool Hide)
    {
        var fi = typeof(WebBrowser).GetField("_axIWebBrowser2", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (fi != null)
        {
            var browser = fi.GetValue(wb);
            if (browser != null)
            {
                browser.GetType().InvokeMember("Silent", System.Reflection.BindingFlags.SetProperty, null, browser, new object[] { Hide });
            }
        }
    }
}
