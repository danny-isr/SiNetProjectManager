using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Domain.Actions;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Phase 5B follow-up dialog for <see cref="SuggestedActionType.AddMaterialToProject"/>
/// (and other <c>ActionFollowUp.FileImportDialog</c> flows).
/// <para>
/// Collects the inputs required by the canonical filing path
/// (<see cref="SiNetSQL.Services.Files.IProjectFileFilingService.FileAsync"/>):
/// a target <c>ProjectFile</c> (the user picks from the existing
/// <c>OutSidData == true</c> set for the project's types) and a source local file
/// per selected attachment. The dialog never writes to the DB directly; it
/// re-dispatches <see cref="ActionCodes.AddMaterialToProject"/> through
/// <see cref="IProcessActionDispatcher"/> with full <c>PrefilledData</c>, which
/// routes through <c>AddMaterialToProjectProcessActionHandler</c> ->
/// <c>IProjectFileFilingService.FileAsync</c>.
/// </para>
/// <para>
/// No automatic <c>ProjectAlternative</c> selection is performed; if the project
/// needs an alternative it must already be modeled in the filing service.
/// </para>
/// </summary>
public partial class FileImportDialog : Window
{
    private readonly int _emailMessageId;
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IProcessActionDispatcher _dispatcher;
    private int _projectId;

    public ObservableCollection<ImportAttachmentItem> Attachments { get; } = [];
    public ObservableCollection<FolderItem> Folders { get; } = [];

    public FileImportDialog(int emailMessageId)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;
        _dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        _dispatcher = App.ServiceProvider.GetRequiredService<IProcessActionDispatcher>();

        AttachmentsList.ItemsSource = Attachments;
        FolderCombo.ItemsSource = Folders;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            StatusText.Text = "טוען נתונים...";

            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);

            // Load email message with attachments and project
            var message = await db.EmailInboxMessages
                .AsNoTracking()
                .Include(m => m.Attachments)
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == _emailMessageId);

            if (message is null)
            {
                StatusText.Text = "הודעת מייל לא נמצאה.";
                return;
            }

            // Populate attachments list
            foreach (var att in message.Attachments.OrderBy(a => a.AttachmentIndex))
            {
                Attachments.Add(new ImportAttachmentItem
                {
                    AttachmentId = att.Id,
                    FileName = att.OriginalFileName ?? att.SavedFileName ?? $"attachment_{att.AttachmentIndex}",
                    IsSelected = true,
                    Status = att.AccItemId != null ? "ב-ACC" : "—",
                });
            }

            // Load valid target ProjectFiles for this project — same OutSidData
            // filter as before, but we now expose the ProjectFile itself so the
            // canonical filing service receives ProjectFileId (not a folder id).
            if (message.ProjectId > 0)
            {
                _projectId = message.ProjectId;

                var projectTypeIds = await db.TypeOfProjectInProjects
                    .AsNoTracking()
                    .Where(tp => tp.ProjectId == message.ProjectId)
                    .Select(tp => tp.ProjectTypeId)
                    .ToListAsync();

                var projectFiles = await db.ProjectFiles
                    .AsNoTracking()
                    .Include(pf => pf.Folder)
                    .Where(pf => pf.OutSidData == true
                        && pf.Folderid.HasValue
                        && pf.TypeProjId.HasValue
                        && projectTypeIds.Contains(pf.TypeProjId.Value))
                    .OrderBy(pf => pf.Title)
                    .Select(pf => new FolderItem
                    {
                        Id = pf.Id,
                        Title = (pf.Title ?? "(ללא שם)") +
                                (pf.Folder != null && pf.Folder.Title != null
                                    ? " — " + pf.Folder.Title
                                    : string.Empty),
                    })
                    .ToListAsync();

                foreach (var pf in projectFiles)
                    Folders.Add(pf);

                if (Folders.Count > 0)
                    FolderCombo.SelectedIndex = 0;
            }

            StatusText.Text = Attachments.Count > 0
                ? $"{Attachments.Count} קבצים מצורפים נטענו."
                : "אין קבצים מצורפים.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"שגיאה בטעינת נתונים: {ex.Message}";
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (FolderCombo.SelectedItem is not FolderItem selectedTarget)
        {
            StatusText.Text = "נא לבחור קובץ יעד בפרויקט.";
            return;
        }

        if (_projectId <= 0)
        {
            StatusText.Text = "פרויקט לא זוהה למייל זה.";
            return;
        }

        var selectedAttachments = Attachments.Where(a => a.IsSelected).ToList();
        if (selectedAttachments.Count == 0)
        {
            StatusText.Text = "נא לסמן קבצים לייבוא.";
            return;
        }

        // Ask user to select local source files via OpenFileDialog
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "בחר קבצים מקור לייבוא",
            Multiselect = true,
            Filter = "כל הקבצים (*.*)|*.*",
        };

        if (openDialog.ShowDialog(this) != true || openDialog.FileNames.Length == 0)
        {
            StatusText.Text = "הייבוא בוטל — לא נבחרו קבצי מקור.";
            return;
        }

        var sourceFiles = openDialog.FileNames;
        int imported = 0;
        int failed = 0;

        // Shared duplicate-target validation: in this dialog every selected
        // attachment is being filed to the same ProjectFileId (no per-row picker),
        // so if more than one attachment is selected they would all collide on
        // (ProjectFileId, ProjectAlternativeId=default). Apply the same rule the
        // email MoveToProject flow uses so behavior is identical across batches.
        var dupItems = selectedAttachments.Select(a => (
            Target: new SiNetSQL.Services.Files.FilingTargetDuplicateValidator.TargetKey(
                selectedTarget.Id, ProjectAlternativeId: null),
            SourceLabel: a.FileName));
        var duplicateGroups = SiNetSQL.Services.Files.FilingTargetDuplicateValidator
            .FindDuplicates(dupItems);
        if (duplicateGroups.Count > 0)
        {
            StatusText.Text = SiNetSQL.Services.Files.FilingTargetDuplicateValidator.UserMessageHebrew;
            MessageBox.Show(
                SiNetSQL.Services.Files.FilingTargetDuplicateValidator.UserMessageHebrew
                    + "\n\n" + SiNetSQL.Services.Files.FilingTargetDuplicateValidator.FormatDetails(duplicateGroups),
                "תיוג כפול", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText.Text = "מייבא קבצים...";

        // While filing is running and once an attachment is placed, prevent the
        // user from changing the target. The selection ComboBox + selection
        // checkboxes are disabled during/after a successful import; failed items
        // remain editable so the user can retry.
        FolderCombo.IsEnabled = false;
        var importButton = sender as System.Windows.Controls.Button;
        if (importButton != null) importButton.IsEnabled = false;

        foreach (var attachment in selectedAttachments)
        {
            // Match by file name; if exactly one source + one attachment,
            // accept the single source regardless of name.
            var matchingSource = sourceFiles.FirstOrDefault(sf =>
                string.Equals(Path.GetFileName(sf), attachment.FileName, StringComparison.OrdinalIgnoreCase));

            if (matchingSource is null && sourceFiles.Length == 1 && selectedAttachments.Count == 1)
                matchingSource = sourceFiles[0];

            if (matchingSource is null)
            {
                attachment.Status = "⚠ לא נמצא קובץ מקור";
                failed++;
                continue;
            }

            attachment.Status = "מייבא...";

            var context = new ActionExecutionContext
            {
                ActionCode = ActionCodes.AddMaterialToProject,
                ProjectId = _projectId,
                EmailMessageId = _emailMessageId,
                ProjectFileId = selectedTarget.Id,
                Source = "FileImportDialog",
                Data = new Dictionary<string, object?>
                {
                    ["ProjectId"] = _projectId,
                    ["ProjectFileId"] = selectedTarget.Id,
                    ["SourceLocalPath"] = matchingSource,
                    ["OriginalFileName"] = attachment.FileName,
                    ["SourceType"] = FileInstanceSourceType.EmailAttachment,
                    ["SourceEmailAttachmentId"] = attachment.AttachmentId,
                },
            };

            ProcessActionResult result;
            try
            {
                result = await _dispatcher.DispatchAsync(context, CancellationToken.None);
            }
            catch (Exception ex)
            {
                attachment.Status = $"✗ {ex.Message}";
                failed++;
                continue;
            }

            if (result.Status == ActionExecutionStatus.Completed)
            {
                attachment.Status = "✓ נקלט";
                attachment.IsPlaced = true;
                imported++;
            }
            else
            {
                attachment.Status = $"✗ {result.Message}";
                failed++;
            }
        }

        StatusText.Text = failed == 0
            ? $"✓ הייבוא הושלם — {imported} קבצים נקלטו לפרויקט."
            : $"הייבוא הסתיים — {imported} הצליחו, {failed} נכשלו.";

        // Re-open the controls only if there is something the user can still
        // act on (any non-placed row). Placed rows stay locked; if everything
        // was placed, the dialog stays read-only until closed.
        var anyEditable = Attachments.Any(a => !a.IsPlaced);
        FolderCombo.IsEnabled = anyEditable;
        if (importButton != null) importButton.IsEnabled = anyEditable;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Represents an attachment in the import list.
    /// </summary>
    public class ImportAttachmentItem : INotifyPropertyChanged
    {
        public int AttachmentId { get; init; }
        public string FileName { get; init; } = string.Empty;

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        private string _status = "—";
        public string Status
        {
            get => _status;
            set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
        }

        // Lock model parallel to EmailAttachment: once placed, the row is locked
        // and cannot be selected/re-imported (no "Refile" UI exists yet for this
        // dialog; user must use the Email flow to correct a placement).
        private bool _isPlaced;
        public bool IsPlaced
        {
            get => _isPlaced;
            set
            {
                if (_isPlaced != value)
                {
                    _isPlaced = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaced)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocked)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditTarget)));
                }
            }
        }

        public bool IsLocked => IsPlaced;
        public bool CanEditTarget => !IsLocked;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Represents a project folder in the target folder dropdown.
    /// </summary>
    public class FolderItem
    {
        public int Id { get; init; }
        public string Title { get; init; } = string.Empty;
    }
}
