using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Services.Coordinators;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Dialog for importing email attachments into a project folder.
/// Loads attachment metadata and project folders from DB, then
/// delegates the actual copy + tagging to <see cref="FileImportCoordinator"/>.
/// </summary>
public partial class FileImportDialog : Window
{
    private readonly int _emailMessageId;
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly FileImportCoordinator _coordinator;

    public ObservableCollection<ImportAttachmentItem> Attachments { get; } = [];
    public ObservableCollection<FolderItem> Folders { get; } = [];

    public FileImportDialog(int emailMessageId)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;
        _dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        _coordinator = App.ServiceProvider.GetRequiredService<FileImportCoordinator>();

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

            // Load project folders — get folders linked to ProjectFiles
            // that are valid tag targets (OutSidData == true)
            if (message.ProjectId > 0)
            {
                var projectTypeIds = await db.TypeOfProjectInProjects
                    .AsNoTracking()
                    .Where(tp => tp.ProjectId == message.ProjectId)
                    .Select(tp => tp.ProjectTypeId)
                    .ToListAsync();

                var folders = await db.ProjectFiles
                    .AsNoTracking()
                    .Where(pf => pf.OutSidData == true
                        && pf.Folderid.HasValue
                        && pf.TypeProjId.HasValue
                        && projectTypeIds.Contains(pf.TypeProjId.Value))
                    .Select(pf => pf.Folder!)
                    .Where(f => f != null)
                    .Distinct()
                    .OrderBy(f => f.Title)
                    .Select(f => new FolderItem { Id = f.Id, Title = f.Title ?? "(ללא שם)" })
                    .ToListAsync();

                foreach (var folder in folders)
                    Folders.Add(folder);

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
        if (FolderCombo.SelectedItem is not FolderItem selectedFolder)
        {
            StatusText.Text = "נא לבחור תיקיית יעד.";
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

        // Match selected source files to attachments by file name
        var sourceFiles = openDialog.FileNames;
        int imported = 0;
        int failed = 0;

        StatusText.Text = "מייבא קבצים...";

        foreach (var attachment in selectedAttachments)
        {
            // Find a matching source file by name
            var matchingSource = sourceFiles.FirstOrDefault(sf =>
                string.Equals(Path.GetFileName(sf), attachment.FileName, StringComparison.OrdinalIgnoreCase));

            if (matchingSource is null && sourceFiles.Length == 1 && selectedAttachments.Count == 1)
            {
                // Single file selected for single attachment — use it regardless of name
                matchingSource = sourceFiles[0];
            }

            if (matchingSource is null)
            {
                attachment.Status = "⚠ לא נמצא קובץ מקור";
                failed++;
                continue;
            }

            attachment.Status = "מייבא...";
            var result = await _coordinator.ImportAsync(
                attachment.AttachmentId, selectedFolder.Id, matchingSource, CancellationToken.None);

            if (result.IsSuccess)
            {
                attachment.Status = "✓ יובא";
                imported++;
            }
            else
            {
                attachment.Status = $"✗ {result.ErrorMessage}";
                failed++;
            }
        }

        StatusText.Text = failed == 0
            ? $"✓ הייבוא הושלם — {imported} קבצים יובאו בהצלחה."
            : $"הייבוא הסתיים — {imported} הצליחו, {failed} נכשלו.";
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
