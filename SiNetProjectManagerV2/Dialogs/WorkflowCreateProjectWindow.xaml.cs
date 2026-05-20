using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Constants;
using SiNetSQL.Data;
using SiNetSQL.MVVM;
using SiNetSQL.Models;
using SiNetSQL.Services.Workflow;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Combined window: email preview (top) + project creation form (bottom).
/// Replaces the two-window approach (EmailPreviewWindow + separate dialog).
/// </summary>
public partial class WorkflowCreateProjectWindow : Window
{
    private readonly int _emailMessageId;

    public WorkflowCreateProjectWindow(int emailMessageId, Window? owner = null)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;

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
                .Select(m => new { m.Id, m.Subject, m.FromAddress, m.ReceivedUtc })
                .FirstOrDefault();

            if (email != null)
            {
                Title = $"🆕 יצירת פרויקט — {email.Subject}";
                SubjectText.Text = $"📧 {email.Subject ?? "(ללא נושא)"}";
                FromText.Text = $"מאת: {email.FromAddress}";
                DateText.Text = $"תאריך: {email.ReceivedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
            }

            // Load attachments
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
    //  Project Creation Form (bottom panel)
    // ════════════════════════════════════════════════════════════════════════

    private void LoadProjectForm()
    {
        var control = new WpfSiData.WPFUserControl.CreateProjectUserControl(_emailMessageId);
        ProjectFormHost.Content = control;

        // When project is created, apply Gmail label and close this window
        if (control.DataContext is CreateProjectViewModel vm)
        {
            vm.ProjectCreated += args =>
            {
                // Apply Gmail label in background (fire-and-forget, don't block dialog close)
                if (!string.IsNullOrEmpty(args.GmailMessageId))
                {
                    ApplyGmailLabelAsync(args);
                }

                // Auto-start the continuation workflow resolved by ProjectType
                // (via ProjectWorkflowPolicyService). No silent fallback to a
                // hardcoded PlanningWorkflow — if no mapping exists, log and skip.
                StartContinuationWorkflowAsync(args, _emailMessageId);

                DialogResult = true;
            };
        }
    }

    /// <summary>
    /// Starts the continuation workflow for a freshly-created project. The workflow
    /// is resolved through <see cref="ProjectWorkflowPolicyService"/> based on the
    /// project's ProjectType(s); the first allowed active definition (ordered by
    /// SortOrder) is used. If no mapping exists, no workflow is started and a clear
    /// warning is logged — there is intentionally NO silent fallback to PlanningWorkflow.
    /// Fire-and-forget — failures are logged but never block the dialog or the user.
    /// </summary>
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
                SiNetSQL.Services.AppLogger.Warn(
                    "[WorkflowCreateProject] Workflow services unavailable — skipping auto-start");
                return;
            }

            var allowed = await policyService.GetAllowedWorkflowsAsync(args.ProjectId, CancellationToken.None);
            var definition = allowed.FirstOrDefault(d => d.IsActive);
            if (definition is null)
            {
                SiNetSQL.Services.AppLogger.Warn(
                    $"[WorkflowCreateProject] No default workflow mapping found for project {args.ProjectId} " +
                    "(ProjectType has no ProjectTypeWorkflowDefinition). Skipping continuation auto-start — " +
                    "no silent fallback to PlanningWorkflow.");
                return;
            }

            var userId = SiNetSQL.Services.CurrentUserContext.Instance.CurrentUserId ?? 0;

            SiNetSQL.Services.AppLogger.Info(
                $"[WorkflowCreateProject] Starting continuation workflow '{definition.Code}' for project {args.ProjectId}");

            await orchestrator.StartWorkflowAsync(
                definition.Id,
                args.ProjectId,
                WorkflowTriggerType.Email,
                triggerEntityId: emailMessageId == 0 ? null : emailMessageId,
                userId: userId,
                notes: $"Auto-started on project creation from email (continuation: {definition.Code})",
                ct: CancellationToken.None);

            SiNetSQL.Services.AppLogger.Info(
                $"[WorkflowCreateProject] ✅ Continuation workflow '{definition.Code}' started for project {args.ProjectId}");
        }
        catch (Exception ex)
        {
            SiNetSQL.Services.AppLogger.Error(ex,
                "[WorkflowCreateProject] Failed to auto-start continuation workflow");
        }
    }

    /// <summary>
    /// Applies the Gmail project label to the source email. Fire-and-forget.
    /// </summary>
    private static async void ApplyGmailLabelAsync(ProjectCreatedEventArgs args)
    {
        try
        {
            var google = App.ServiceProvider?.GetService<SiOffice.GoogleConnector.GoogleService>();
            if (google == null)
            {
                SiNetSQL.Services.AppLogger.Warn("[WorkflowCreateProject] GoogleService not available — skipping label");
                return;
            }

            SiNetSQL.Services.AppLogger.Info(
                $"[WorkflowCreateProject] Applying label: {args.LocationName}/{args.ProjectName} → gmail msg {args.GmailMessageId}");

            var labelId = await google.GetOrCreateProjectLabelAsync(
                args.LocationName, args.ProjectName);

            await google.AttachProjectLabelAsync(args.GmailMessageId!, labelId);

            SiNetSQL.Services.AppLogger.Info(
                $"[WorkflowCreateProject] ✅ Gmail label applied successfully");
        }
        catch (Exception ex)
        {
            SiNetSQL.Services.AppLogger.Error(ex,
                $"[WorkflowCreateProject] Failed to apply Gmail label");
        }
    }
}
