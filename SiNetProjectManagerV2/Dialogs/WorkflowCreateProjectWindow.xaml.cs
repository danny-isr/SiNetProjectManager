using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Constants;
using SiNetSQL.Data;
using SiNetSQL.MVVM;
using SiNetSQL.Models;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;
using SiNetSQL.Services.MoveToProject;
using SiNetSQL.Services.Tasks;
using SiNetSQL.Services.Workflow;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Combined window: email preview (top) + project creation form (middle) +
/// optional per-attachment routing footer (bottom, task-mode only).
/// <para>
/// In standalone mode (no <see cref="EmailFilingTaskContext"/>), the window
/// behaves as before: project is created, Gmail label applied, continuation
/// workflow auto-started, dialog closes.
/// </para>
/// <para>
/// In task mode (opened from <c>IdentifyQuoteRequest</c> via the floating task
/// list), the dialog stays open after project creation. The user picks a
/// destination <see cref="ProjectFile"/> and <see cref="ProjectAlternative"/>
/// for each tagged-eligible attachment, then clicks "העבר לפרויקט וסיים משימה".
/// That calls the existing <see cref="IEmailMoveToProjectApplicationService"/>
/// which (via the MoveToProjectProcessActionHandler) drives the centralized
/// <see cref="ITaskCompletionCoordinator"/>. The dialog closes only when the
/// service reports <c>TaskClosed</c>.
/// </para>
/// </summary>
public partial class WorkflowCreateProjectWindow : Window
{
    private readonly int _emailMessageId;
    private readonly EmailFilingTaskContext? _taskContext;
    private readonly bool _isTaskMode;

    private int? _createdProjectId;
    private string? _createdProjectName;
    private string? _createdLocationName;
    private string? _createdGmailMessageId;
    private bool _moveAndFinishCompleted;

    private readonly ObservableCollection<AttachmentRoutingItem> _routingItems = new();

    /// <summary>Standalone constructor (no task context). Behaves as before.</summary>
    public WorkflowCreateProjectWindow(int emailMessageId, Window? owner = null)
        : this(emailMessageId, taskContext: null, owner)
    {
    }

    /// <summary>
    /// Task-mode constructor. When <paramref name="taskContext"/> is non-null,
    /// the dialog stays open after project creation and exposes the
    /// per-attachment routing footer; the originating task closes only after
    /// a successful MoveToProject run.
    /// </summary>
    public WorkflowCreateProjectWindow(
        int emailMessageId,
        EmailFilingTaskContext? taskContext,
        Window? owner = null)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;
        _taskContext = taskContext;
        _isTaskMode = taskContext is not null;

        if (owner != null) Owner = owner;

        LoadEmailData();
        LoadProjectForm();

        RoutingList.ItemsSource = _routingItems;
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
    //  Project Creation Form (middle panel)
    // ════════════════════════════════════════════════════════════════════════

    private void LoadProjectForm()
    {
        var control = new WpfSiData.WPFUserControl.CreateProjectUserControl(_emailMessageId);
        ProjectFormHost.Content = control;

        if (control.DataContext is CreateProjectViewModel vm)
        {
            vm.ProjectCreated += OnProjectCreated;
        }
    }

    private void OnProjectCreated(ProjectCreatedEventArgs args)
    {
        _createdProjectId = args.ProjectId;
        _createdProjectName = args.ProjectName;
        _createdLocationName = args.LocationName;
        _createdGmailMessageId = args.GmailMessageId;

        if (!string.IsNullOrEmpty(args.GmailMessageId))
        {
            ApplyGmailLabelAsync(args);
        }

        if (!_isTaskMode)
        {
            // Standalone mode: original behavior — auto-start the continuation
            // workflow resolved by ProjectType and close the dialog.
            StartContinuationWorkflowAsync(args, _emailMessageId);
            DialogResult = true;
            return;
        }

        // Task mode: keep the window open. The IdentifyQuoteRequest task is
        // gated by a successful MoveToProject run — not by project creation
        // alone (see ReviewCompletionEventBehavior.ReviewMaterialFiled +
        // ReviewTaskInteractionRegistry IdentifyQuoteRequest entry).
        _ = ShowRoutingFooterAsync(args.ProjectId);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Task-mode: per-attachment routing footer + Move & finish
    // ════════════════════════════════════════════════════════════════════════

    private async Task ShowRoutingFooterAsync(int projectId)
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null)
            {
                TaskFooterStatus.Text = "DB factory לא זמין — לא ניתן לטעון אלטרנטיבות.";
                TaskFooterPanel.Visibility = Visibility.Visible;
                return;
            }

            // Reuse the existing alternative-loading path (auto-creates default '1').
            var tagging = new AttachmentTaggingService();
            var alternatives = await tagging.EnsureAndLoadAlternativesAsync(projectId);
            var primary = alternatives.FirstOrDefault(a => a.IsPrimary) ?? alternatives.FirstOrDefault();

            // Load the email's attachments fresh from DB (no caching of preview list).
            List<AttachmentSnapshot> snaps;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                snaps = await db.EmailInboxAttachments
                    .AsNoTracking()
                    .Where(a => a.MessageId == _emailMessageId)
                    .OrderBy(a => a.AttachmentIndex)
                    .Select(a => new AttachmentSnapshot(
                        a.Id,
                        a.OriginalFileName ?? a.SavedFileName ?? $"קובץ #{a.AttachmentIndex}",
                        a.AccItemId,
                        a.ProjectFileId,
                        a.ProjectAlternativeId))
                    .ToListAsync();
            }

            _routingItems.Clear();
            foreach (var s in snaps)
            {
                var item = new AttachmentRoutingItem(s.Id, s.FileName, s.AccItemId)
                {
                    Alternatives = alternatives,
                    TaggedProjectFileId = s.ProjectFileId,
                    SelectedAlternativeId = s.ProjectAlternativeId ?? primary?.Id,
                };
                item.PropertyChanged += (_, __) => UpdateMoveButtonState();
                _routingItems.Add(item);

                if (item.TaggedProjectFileId.HasValue)
                {
                    _ = RefreshProjectFileLabelAsync(item);
                }
            }

            UpdateMoveButtonState();
            TaskFooterStatus.Text = _routingItems.Count == 0
                ? "אין קבצים מצורפים — ניתן ללחוץ \"העבר לפרויקט וסיים משימה\" כדי לסגור את המייל."
                : "בחר קובץ פרויקט ואלטרנטיבה לכל קובץ מצורף, ולאחר מכן לחץ על הכפתור הירוק.";
            TaskFooterPanel.Visibility = Visibility.Visible;
            MoveAndFinishButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[WorkflowCreateProject] ShowRoutingFooterAsync failed");
            TaskFooterStatus.Text = $"שגיאה בטעינת אלטרנטיבות: {ex.Message}";
            TaskFooterPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateMoveButtonState()
    {
        // Allow clicking even with un-tagged attachments — the service skips
        // un-tagged items. Only disable while a run is in progress.
        if (!_moveAndFinishCompleted)
            MoveAndFinishButton.IsEnabled = TaskFooterPanel.Visibility == Visibility.Visible;
    }

    private async void PickProjectFile_Click(object sender, RoutedEventArgs e)
    {
        if (_createdProjectId is not int projectId) return;
        if (sender is not Button { Tag: AttachmentRoutingItem item }) return;

        try
        {
            var picker = App.ServiceProvider?.GetService<IAttachmentProjectFilePicker>();
            if (picker is null)
            {
                MessageBox.Show("בורר קובצי פרויקט אינו רשום ב-DI.", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var picked = await picker.PickAsync(projectId, item.TaggedProjectFileId);
            if (picked.HasValue)
            {
                item.TaggedProjectFileId = picked.Value;
                await RefreshProjectFileLabelAsync(item);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[WorkflowCreateProject] PickProjectFile_Click failed");
            MessageBox.Show($"שגיאה בבחירת קובץ פרויקט: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task RefreshProjectFileLabelAsync(AttachmentRoutingItem item)
    {
        if (item.TaggedProjectFileId is not int pfId)
        {
            item.ProjectFileLabel = "📁 בחר קובץ פרויקט…";
            return;
        }

        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;
            await using var db = await dbFactory.CreateDbContextAsync();
            var pf = await db.ProjectFiles
                .AsNoTracking()
                .Where(p => p.Id == pfId)
                .Select(p => new { p.Number, p.Title })
                .FirstOrDefaultAsync();
            item.ProjectFileLabel = pf is null
                ? $"📁 ProjectFile #{pfId}"
                : $"📁 {pf.Number} — {pf.Title}";
        }
        catch
        {
            item.ProjectFileLabel = $"📁 ProjectFile #{pfId}";
        }
    }

    private async void MoveAndFinish_Click(object sender, RoutedEventArgs e)
    {
        if (_taskContext is null || _createdProjectId is not int projectId)
        {
            DialogResult = true;
            return;
        }

        MoveAndFinishButton.IsEnabled = false;
        CancelTaskButton.IsEnabled = false;
        TaskFooterStatus.Text = "מעביר לפרויקט…";

        try
        {
            var sp = App.ServiceProvider;
            var service = sp?.GetService<IEmailMoveToProjectApplicationService>();
            if (service is null)
            {
                AppLogger.Error("[WorkflowCreateProject] IEmailMoveToProjectApplicationService not registered.");
                TaskFooterStatus.Text = "✗ שירות MoveToProject אינו זמין.";
                MoveAndFinishButton.IsEnabled = true;
                CancelTaskButton.IsEnabled = true;
                return;
            }

            // Build attachment inputs: only attachments with both AccItemId and
            // a tagged ProjectFile are eligible for filing. Un-tagged items are
            // skipped server-side; we omit them here for clarity.
            var attachments = _routingItems
                .Where(r => !string.IsNullOrEmpty(r.AccItemId) && r.TaggedProjectFileId.HasValue)
                .Select(r => new MoveToProjectAttachmentInput(
                    InboxAttachmentId: r.InboxAttachmentId,
                    AccItemId: r.AccItemId,
                    FileName: r.FileName,
                    TaggedProjectFileId: r.TaggedProjectFileId,
                    SelectedAlternativeId: r.SelectedAlternativeId,
                    IsPlacedHint: false))
                .ToList();

            // Look up email metadata for the request.
            string? gmailMsgId = _createdGmailMessageId;
            string? subject = null, from = null, dateStr = null;
            var dbFactory = sp?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory != null)
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var info = await db.EmailInboxMessages
                    .AsNoTracking()
                    .Where(m => m.Id == _emailMessageId)
                    .Select(m => new { m.MessageUniqueId, m.Subject, m.FromAddress, m.ReceivedUtc })
                    .FirstOrDefaultAsync();
                if (info != null)
                {
                    gmailMsgId ??= info.MessageUniqueId;
                    subject = info.Subject;
                    from = info.FromAddress;
                    dateStr = info.ReceivedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                }
            }

            var request = new MoveToProjectRequest(
                ProjectId: projectId,
                GmailMessageId: gmailMsgId,
                EmailSubject: subject,
                EmailFrom: from,
                EmailDate: dateStr,
                Attachments: attachments,
                ActiveTaskContext: _taskContext,
                CurrentUserId: CurrentUserContext.Instance.CurrentUserId);

            var progress = new Progress<string>(s => TaskFooterStatus.Text = s);
            var result = await service.MoveAsync(request, progress, CancellationToken.None);

            // Reflect per-attachment outcomes in the routing list.
            var byId = _routingItems.ToDictionary(r => r.InboxAttachmentId);
            foreach (var outcome in result.AttachmentOutcomes)
            {
                if (!byId.TryGetValue(outcome.InboxAttachmentId, out var item)) continue;
                item.StatusText = outcome.Kind switch
                {
                    MoveToProjectAttachmentOutcomeKind.Filed => "✓ הועבר",
                    MoveToProjectAttachmentOutcomeKind.SkippedAlreadyFiled => "✓ כבר קיים",
                    MoveToProjectAttachmentOutcomeKind.SkippedNoTag => "— ללא תיוג",
                    MoveToProjectAttachmentOutcomeKind.SkippedAlreadyPlacedHint => "— מסומן כקיים",
                    MoveToProjectAttachmentOutcomeKind.MissingInAcc => "✗ חסר ב-ACC",
                    MoveToProjectAttachmentOutcomeKind.AlreadyMovedToProject => "✓ הועבר קודם",
                    MoveToProjectAttachmentOutcomeKind.Locked => "✗ נעול",
                    MoveToProjectAttachmentOutcomeKind.FilingFailed => "✗ נכשל",
                    MoveToProjectAttachmentOutcomeKind.DownloadFailed => "✗ הורדה נכשלה",
                    MoveToProjectAttachmentOutcomeKind.FiledButMoveMetadataFailed => "⚠ הועבר/מטא נכשל",
                    _ => string.Empty,
                };
            }

            switch (result.Kind)
            {
                case MoveToProjectResultKind.EmptyEmailMoved:
                    TaskFooterStatus.Text = "✓ המייל סווג (ללא קבצים להעברה).";
                    break;
                case MoveToProjectResultKind.Completed:
                    TaskFooterStatus.Text = result.FailedCount == 0
                        ? $"✓ הועברו {result.MovedCount}/{result.TotalCount}"
                          + (result.SkippedCount > 0 ? $" ({result.SkippedCount} דולגו)" : "")
                        : $"⚠ הועברו {result.MovedCount}/{result.TotalCount} ({result.FailedCount} נכשלו)";
                    break;
                case MoveToProjectResultKind.DuplicateTargetsBlocked:
                    TaskFooterStatus.Text = "✗ " + SiNetSQL.Services.Files.FilingTargetDuplicateValidator.UserMessageHebrew;
                    MessageBox.Show(
                        SiNetSQL.Services.Files.FilingTargetDuplicateValidator.UserMessageHebrew
                            + "\n\n" + (result.DuplicateDetailsText ?? string.Empty),
                        "תיוג כפול", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                default:
                    TaskFooterStatus.Text = $"✗ {result.Kind}: {result.ExceptionMessage}";
                    break;
            }

            // Trigger task list refresh as soon as the service signals it.
            if (result.TaskRefreshRequested
                && _taskContext.OnTaskRefreshRequested is { } refresh)
            {
                try { Application.Current?.Dispatcher.BeginInvoke(refresh); }
                catch { /* best-effort */ }
            }

            if (result.TaskClosed)
            {
                _moveAndFinishCompleted = true;
                // Now (and only now) start the continuation workflow — same
                // policy resolution as the standalone mode, but deferred until
                // task closure to preserve the IdentifyQuoteRequest gating.
                StartContinuationWorkflowAsync(
                    new ProjectCreatedEventArgs
                    {
                        ProjectId = projectId,
                        ProjectName = _createdProjectName ?? string.Empty,
                        LocationName = _createdLocationName ?? string.Empty,
                        GmailMessageId = _createdGmailMessageId,
                    },
                    _emailMessageId);

                DialogResult = true;
                return;
            }

            // Task did not close — re-enable buttons so the user can retry.
            MoveAndFinishButton.IsEnabled = true;
            CancelTaskButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[WorkflowCreateProject] MoveAndFinish_Click failed");
            TaskFooterStatus.Text = $"✗ שגיאה: {ex.Message}";
            MoveAndFinishButton.IsEnabled = true;
            CancelTaskButton.IsEnabled = true;
        }
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        // Cancel = close the dialog WITHOUT closing the task. The task remains
        // open in the floating list and can be reopened later.
        DialogResult = false;
        Close();
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
                    "no silent fallback to PlanningWorkflow.");
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
                $"[WorkflowCreateProject] ✅ Gmail label applied successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex,
                $"[WorkflowCreateProject] Failed to apply Gmail label");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Routing item view-models (task-mode only)
    // ════════════════════════════════════════════════════════════════════════

    private sealed record AttachmentSnapshot(
        int Id, string FileName, string? AccItemId, int? ProjectFileId, int? ProjectAlternativeId);

    /// <summary>
    /// Per-attachment row binding for the task-mode routing footer.
    /// </summary>
    public sealed class AttachmentRoutingItem : INotifyPropertyChanged
    {
        public AttachmentRoutingItem(int inboxAttachmentId, string fileName, string? accItemId)
        {
            InboxAttachmentId = inboxAttachmentId;
            FileName = fileName;
            AccItemId = accItemId;
            _projectFileLabel = "📁 בחר קובץ פרויקט…";
        }

        public int InboxAttachmentId { get; }
        public string FileName { get; }
        public string? AccItemId { get; }
        public IReadOnlyList<ProjectAlternative> Alternatives { get; set; } = Array.Empty<ProjectAlternative>();

        private int? _taggedProjectFileId;
        public int? TaggedProjectFileId
        {
            get => _taggedProjectFileId;
            set
            {
                if (_taggedProjectFileId == value) return;
                _taggedProjectFileId = value;
                OnPropertyChanged();
            }
        }

        private int? _selectedAlternativeId;
        public int? SelectedAlternativeId
        {
            get => _selectedAlternativeId;
            set
            {
                if (_selectedAlternativeId == value) return;
                _selectedAlternativeId = value;
                OnPropertyChanged();
            }
        }

        private string _projectFileLabel;
        public string ProjectFileLabel
        {
            get => _projectFileLabel;
            set
            {
                if (_projectFileLabel == value) return;
                _projectFileLabel = value;
                OnPropertyChanged();
            }
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
