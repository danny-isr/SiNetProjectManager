using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.MVVM.Components;
using SiNetSQL.Services;
using SiNetSQL.Services.Tasks;
using SiNetSQL.Services.Workflow;
using SiOffice.GoogleConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

using SiNetProjectManagerV2.WPFUserControl;

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
    private readonly EmailViewerViewModel _emailViewerVm;

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
        _emailViewerVm = new EmailViewerViewModel();
        InitializeComponent();
        EmailViewer.ViewModel = _emailViewerVm;

        _emailMessageId = emailMessageId;
        _taskContext = taskContext;

        if (owner != null) Owner = owner;

        _emailViewerVm.AttachmentClicked += OnViewerAttachmentClicked;

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
                .FirstOrDefault();

            if (email != null)
            {
                Title = $"🆕 יצירת פרויקט — {email.Subject}";

                var emailInfo = new EmailInfo
                {
                    Subject = email.Subject ?? "",
                    From = email.FromAddress ?? "",
                    Date = email.ReceivedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    MessageId = email.MessageUniqueId ?? ""
                };

                var attachments = db.EmailInboxAttachments
                    .AsNoTracking()
                    .Where(a => a.MessageId == _emailMessageId)
                    .OrderBy(a => a.AttachmentIndex)
                    .ToList();

                emailInfo.Attachments = attachments.Select(a => new EmailAttachment
                {
                    InboxAttachmentId = a.Id,
                    FileName = a.OriginalFileName ?? a.SavedFileName ?? $"קובץ #{a.AttachmentIndex}",
                    AccItemId = a.AccItemId,
                    IsInline = false,
                    Size = 0,
                    MimeType = ""
                }).ToList();

                _emailViewerVm.Email = emailInfo;

                if (!string.IsNullOrEmpty(email.MessageUniqueId))
                {
                    LoadEmailBodyAsync(email.MessageUniqueId);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to load email data: {ex.Message}");
        }
    }

    private void OnViewerAttachmentClicked(EmailAttachment att)
    {
        if (string.IsNullOrEmpty(att.AccItemId))
        {
            MessageBox.Show("הקובץ עדיין לא הועלה ל-ACC.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var db = dbFactory.CreateDbContext();
            var message = db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.Id == _emailMessageId)
                .Select(m => new { m.InboxAccProjectId, m.InboxAccFolderId })
                .FirstOrDefault();

            if (message == null || string.IsNullOrEmpty(message.InboxAccProjectId))
            {
                MessageBox.Show("מזהה פרויקט ACC לא נמצא.", "לא זמין",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var projectGuid = message.InboxAccProjectId.StartsWith("b.", StringComparison.Ordinal)
                ? message.InboxAccProjectId[2..] : message.InboxAccProjectId;

            var url = $"https://acc.autodesk.com/docs/files/projects/{projectGuid}";
            if (!string.IsNullOrEmpty(message.InboxAccFolderId))
                url += $"?folderUrn={Uri.EscapeDataString(message.InboxAccFolderId)}&entityId={Uri.EscapeDataString(att.AccItemId)}";

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
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בפתיחת הקובץ: {ex.Message}", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
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

            if (_taskContext != null && _taskContext.ActiveTaskProjectId.HasValue)
            {
                vm.PreselectParentProject(_taskContext.ActiveTaskProjectId.Value);
            }
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
        var isParentDrivenProjectCreation = _taskContext is { ComponentKey: TaskComponentKeys.ProjectCreationFromEmail };
        if (!isParentDrivenProjectCreation)
        {
            StartContinuationWorkflowAsync(args, _emailMessageId);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[WorkflowCreateProject] Skipping continuation workflow for project {args.ProjectId}: " +
                $"opened from parent-driven task ({_taskContext!.ComponentKey}). " +
                $"The parent workflow advances the stages via the existing workflow engine.");
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
            var policyService = sp.GetService<IProjectWorkflowPolicyService>();
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

            string? initialStageCode = null;
            if (string.Equals(definition.Code, "Review", StringComparison.OrdinalIgnoreCase))
            {
                initialStageCode = "REV.MaterialIntake";
            }

            await orchestrator.StartWorkflowAsync(
                definition.Id,
                args.ProjectId,
                WorkflowTriggerType.Email,
                triggerEntityId: emailMessageId == 0 ? null : emailMessageId,
                userId: userId,
                notes: $"Auto-started on project creation from email (continuation: {definition.Code})",
                ct: CancellationToken.None,
                initialStageCode: initialStageCode);

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
            if (fullEmail != null && _emailViewerVm.Email != null)
            {
                string cleanHtml = fullEmail.HtmlBody ?? "";
                if (!string.IsNullOrEmpty(cleanHtml))
                {
                    string cleanCss = @"<style>
body { font-family: 'Segoe UI', Tahoma, sans-serif !important; padding: 15px !important; margin: 0 !important; max-width: 100% !important; word-wrap: break-word !important; }
.gmail_signature, .gmail_quote { display: none !important; }
</style>";
                    if (cleanHtml.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanHtml = cleanHtml.Replace("<head>", "<head>" + cleanCss, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        cleanHtml = cleanCss + cleanHtml;
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    _emailViewerVm.Email.HtmlBody = cleanHtml;
                    _emailViewerVm.Email.Body = fullEmail.Body ?? "";

                    _emailViewerVm.RefreshDisplay();
                    _emailViewerVm.Email.RefreshAttachmentDisplay();
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to load email body in CreateProjectWindow: {ex.Message}");
        }
    }
}
