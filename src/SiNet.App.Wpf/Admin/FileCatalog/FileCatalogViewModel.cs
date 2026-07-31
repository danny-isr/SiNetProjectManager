using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.FileCatalog;

namespace SiNet.App.Wpf.Admin.FileCatalog;

public sealed class FileCatalogViewModel : ObservableObject
{
    private enum DialogMode
    {
        JobTypeAdd,
        JobTypeEdit,
        FolderAdd,
    }

    private readonly IFileCatalogQueryService _query;
    private readonly IFileCatalogWriteService _write;

    private FileCatalogJobTypeDto? _selectedJobType;
    private FileCatalogFileRowVm? _selectedFile;
    private FileCatalogFolderNodeVm? _selectedFolder;
    private FileCatalogFolderNodeVm? _folderFilter;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _isDialogOpen;
    private string _dialogHeader = string.Empty;
    private string _dialogInput = string.Empty;
    private DialogMode _dialogMode;
    private FileCatalogFolderNodeVm? _folderAddParent;
    private ICollectionView? _filesView;

    public FileCatalogViewModel(IFileCatalogQueryService query, IFileCatalogWriteService write)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _write = write ?? throw new ArgumentNullException(nameof(write));

        JobTypes = [];
        FolderTree = [];
        AllFiles = [];
        FileExtensions = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => !IsBusy);
        AddFileCommand = new AsyncRelayCommand(AddFileAsync, () => !IsBusy);
        DeleteFileCommand = new AsyncRelayCommand(DeleteFileAsync, () => !IsBusy && SelectedFile is not null);
        AddJobTypeCommand = new RelayCommand(_ => OpenJobTypeAddDialog(), _ => !IsBusy);
        RenameJobTypeCommand = new RelayCommand(
            _ => OpenJobTypeEditDialog(),
            _ => !IsBusy && SelectedJobType is { Id: > 0 });
        AddFolderCommand = new RelayCommand(p => OpenFolderAddDialog(p), _ => !IsBusy);
        AssignFileToFolderCommand = new AsyncRelayCommand<object>(AssignFileAsync, _ => !IsBusy);
        DeleteFolderCommand = new AsyncRelayCommand<object>(DeleteFolderAsync, _ => !IsBusy);
        ConfirmDialogCommand = new AsyncRelayCommand(ConfirmDialogAsync, () => !IsBusy);
        CancelDialogCommand = new RelayCommand(_ => IsDialogOpen = false);
        BrowseTemplateCommand = new RelayCommand(p => BrowseTemplate(p as FileCatalogFileRowVm));
    }

    public ObservableCollection<FileCatalogJobTypeDto> JobTypes { get; }
    public ObservableCollection<FileCatalogFolderNodeVm> FolderTree { get; }
    public ObservableCollection<FileCatalogFileRowVm> AllFiles { get; }
    public ObservableCollection<string> FileExtensions { get; }

    public ICollectionView? FilesView
    {
        get => _filesView;
        private set => SetField(ref _filesView, value);
    }

    public FileCatalogJobTypeDto? SelectedJobType
    {
        get => _selectedJobType;
        set
        {
            if (!SetField(ref _selectedJobType, value))
                return;
            FilesView?.Refresh();
            RaiseCanExecutes();
        }
    }

    public FileCatalogFileRowVm? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetField(ref _selectedFile, value))
                return;
            RaiseCanExecutes();
        }
    }

    /// <summary>
    /// Target folder for «הוסף קובץ» / create-subfolder fallback. Not changed by right-click assign.
    /// </summary>
    public FileCatalogFolderNodeVm? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (_selectedFolder is not null)
                _selectedFolder.IsSelected = false;
            if (!SetField(ref _selectedFolder, value))
                return;
            if (_selectedFolder is not null)
                _selectedFolder.IsSelected = true;
            RaiseCanExecutes();
        }
    }

    /// <summary>
    /// Left-click folder filter for the files grid. Project root / null = all files (JobType still applies).
    /// </summary>
    public FileCatalogFolderNodeVm? FolderFilter
    {
        get => _folderFilter;
        private set
        {
            if (SetField(ref _folderFilter, value))
                OnPropertyChanged(nameof(FolderFilterCaption));
        }
    }

    public string FolderFilterCaption =>
        FolderFilter is null || FolderFilter.IsProjectRoot
            ? "כל התיקיות"
            : FolderFilter.Title;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseCanExecutes();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsDialogOpen
    {
        get => _isDialogOpen;
        set => SetField(ref _isDialogOpen, value);
    }

    public string DialogHeader
    {
        get => _dialogHeader;
        set => SetField(ref _dialogHeader, value);
    }

    public string DialogInput
    {
        get => _dialogInput;
        set => SetField(ref _dialogInput, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveChangesCommand { get; }
    public AsyncRelayCommand AddFileCommand { get; }
    public AsyncRelayCommand DeleteFileCommand { get; }
    public RelayCommand AddJobTypeCommand { get; }
    public RelayCommand RenameJobTypeCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand<object> AssignFileToFolderCommand { get; }
    public AsyncRelayCommand<object> DeleteFolderCommand { get; }
    public AsyncRelayCommand ConfirmDialogCommand { get; }
    public RelayCommand CancelDialogCommand { get; }
    public RelayCommand BrowseTemplateCommand { get; }

    /// <summary>Left-click on a folder: set grid filter + add-file target. Do not call from right-click.</summary>
    public void ApplyFolderFilter(FileCatalogFolderNodeVm folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        FolderFilter = folder;
        SelectedFolder = folder;
        FilesView?.Refresh();
        RaiseCanExecutes();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = "טוען…";
        try
        {
            var selectedJobId = SelectedJobType?.Id;
            var selectedFileId = SelectedFile?.FileId;
            var filterFolderId = FolderFilter?.FolderId;

            var snap = await _query.GetSnapshotAsync().ConfigureAwait(true);

            JobTypes.Clear();
            JobTypes.Add(new FileCatalogJobTypeDto(0, "הכל"));
            foreach (var jt in snap.JobTypes)
                JobTypes.Add(jt);

            FileExtensions.Clear();
            foreach (var ext in snap.FileExtensions)
                FileExtensions.Add(ext);

            FolderTree.Clear();
            foreach (var root in snap.FolderRoots)
                FolderTree.Add(new FileCatalogFolderNodeVm(root));

            AllFiles.Clear();
            foreach (var f in snap.Files)
                AllFiles.Add(new FileCatalogFileRowVm(f));

            FilesView = CollectionViewSource.GetDefaultView(AllFiles);
            FilesView.Filter = FilterFile;

            SelectedJobType = JobTypes.FirstOrDefault(j => j.Id == selectedJobId) ?? JobTypes[0];

            if (selectedFileId is int fid)
                SelectedFile = AllFiles.FirstOrDefault(f => f.FileId == fid);
            else
                SelectedFile = null;

            var restore = filterFolderId is int id ? FindFolder(id) : null;
            restore ??= FolderTree.FirstOrDefault(f => f.IsProjectRoot) ?? FolderTree.FirstOrDefault();
            if (restore is not null)
                ApplyFolderFilter(restore);
            else
            {
                FolderFilter = null;
                SelectedFolder = null;
                FilesView?.Refresh();
            }

            StatusMessage = $"נטענו {AllFiles.Count} קבצים, {CountFolders()} תיקיות. תצוגה: {FolderFilterCaption}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "שגיאה בטעינה: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool FilterFile(object obj)
    {
        if (obj is not FileCatalogFileRowVm file)
            return false;

        if (SelectedJobType is { Id: > 0 } && file.JobTypeId != SelectedJobType.Id)
            return false;

        // Root / no filter → all folders; specific folder → only that folder's files.
        if (FolderFilter is null || FolderFilter.IsProjectRoot)
            return true;

        return file.FolderId == FolderFilter.FolderId;
    }

    private async Task SaveChangesAsync()
    {
        var dirty = AllFiles.Where(f => f.IsDirty).Select(f => f.ToEditDto()).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = "אין שינויים לשמירה.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _write.SaveFileEditsAsync(dirty).ConfigureAwait(true);
            if (!result.Success)
            {
                StatusMessage = result.ErrorMessage ?? "שמירה נכשלה.";
                MessageBox.Show(StatusMessage, "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var row in AllFiles.Where(f => f.IsDirty))
                row.ClearDirty();
            StatusMessage = $"נשמרו {dirty.Count} שינויים.";
        }
        catch (Exception ex)
        {
            StatusMessage = "שגיאה בשמירה: " + ex.Message;
            MessageBox.Show(StatusMessage, "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddFileAsync()
    {
        if (SelectedFolder is null || SelectedFolder.IsProjectRoot)
        {
            MessageBox.Show(
                "יש לבחור תיקייה ספציפית (לא «תיקיית הפרויקט») ליצירת קובץ.",
                "ניהול קבצים",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (SelectedJobType is null || SelectedJobType.Id <= 0)
        {
            MessageBox.Show("יש לבחור סוג עבודה (לא «הכל»).", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _write
                .CreateFileAsync(SelectedFolder.FolderId, SelectedJobType.Id)
                .ConfigureAwait(true);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "יצירה נכשלה.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
            if (result.NewId is int id)
                SelectedFile = AllFiles.FirstOrDefault(f => f.FileId == id);
            StatusMessage = "נוסף קובץ חדש.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteFileAsync()
    {
        if (SelectedFile is null)
            return;

        var hadCatalogCode = SelectedFile.HasCatalogCode;
        var codeNote = hadCatalogCode
            ? $"{Environment.NewLine}{Environment.NewLine}זהו קובץ קטלוג (Code: {SelectedFile.Code}).{Environment.NewLine}"
              + "כדי לשחזר אותו אחרי המחיקה יש להריץ שוב «טעינת Seed בסיסי»."
            : string.Empty;

        if (MessageBox.Show(
                $"למחוק את '{SelectedFile.Title}'?{codeNote}",
                "אישור מחיקה",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = await _write.DeleteFileAsync(SelectedFile.FileId).ConfigureAwait(true);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "מחיקה נכשלה.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
            StatusMessage = hadCatalogCode
                ? "הקובץ נמחק. לשחזור: טעינת Seed בסיסי."
                : "הקובץ נמחק.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenJobTypeAddDialog()
    {
        _dialogMode = DialogMode.JobTypeAdd;
        DialogHeader = "הוספת סוג עבודה";
        DialogInput = string.Empty;
        IsDialogOpen = true;
    }

    private void OpenJobTypeEditDialog()
    {
        if (SelectedJobType is null || SelectedJobType.Id <= 0)
            return;
        _dialogMode = DialogMode.JobTypeEdit;
        DialogHeader = "עריכת סוג עבודה";
        DialogInput = SelectedJobType.Title;
        IsDialogOpen = true;
    }

    private void OpenFolderAddDialog(object? param)
    {
        var parent = param as FileCatalogFolderNodeVm ?? SelectedFolder;
        if (parent is null)
        {
            MessageBox.Show("יש לבחור תיקיית אב.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _folderAddParent = parent;
        _dialogMode = DialogMode.FolderAdd;
        DialogHeader = $"תיקייה חדשה תחת '{parent.Title}'";
        DialogInput = string.Empty;
        IsDialogOpen = true;
    }

    private async Task ConfirmDialogAsync()
    {
        if (string.IsNullOrWhiteSpace(DialogInput))
        {
            MessageBox.Show("יש להזין שם.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            FileCatalogWriteResult result;
            switch (_dialogMode)
            {
                case DialogMode.JobTypeAdd:
                    result = await _write.CreateJobTypeAsync(DialogInput).ConfigureAwait(true);
                    break;
                case DialogMode.JobTypeEdit when SelectedJobType is { Id: > 0 } jt:
                    result = await _write.RenameJobTypeAsync(jt.Id, DialogInput).ConfigureAwait(true);
                    break;
                case DialogMode.FolderAdd when _folderAddParent is not null:
                    result = await _write
                        .CreateFolderAsync(_folderAddParent.FolderId, DialogInput)
                        .ConfigureAwait(true);
                    break;
                default:
                    result = FileCatalogWriteResult.Fail("מצב דיאלוג לא תקף.");
                    break;
            }

            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "הפעולה נכשלה.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsDialogOpen = false;
            var keepJobId = _dialogMode == DialogMode.JobTypeAdd ? result.NewId : SelectedJobType?.Id;
            await LoadAsync().ConfigureAwait(true);
            if (keepJobId is int id)
                SelectedJobType = JobTypes.FirstOrDefault(j => j.Id == id) ?? SelectedJobType;
            StatusMessage = "הפעולה הושלמה.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteFolderAsync(object? param)
    {
        if (param is not FileCatalogFolderNodeVm folder)
            return;

        if (folder.IsProjectRoot)
        {
            MessageBox.Show(
                "לא ניתן למחוק את תיקיית השורש של הפרויקט.",
                "ניהול קבצים",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (folder.Children.Count > 0)
        {
            MessageBox.Show(
                "לא ניתן למחוק תיקייה שמכילה תיקיות משנה. רוקן אותה קודם.",
                "ניהול קבצים",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (AllFiles.Any(f => f.FolderId == folder.FolderId))
        {
            MessageBox.Show(
                "לא ניתן למחוק תיקייה שמכילה הגדרות קבצים. העבר או מחק אותן קודם.",
                "ניהול קבצים",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(
                $"למחוק את התיקייה הריקה '{folder.Title}'?",
                "מחיקת תיקייה",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = await _write.DeleteFolderAsync(folder.FolderId).ConfigureAwait(true);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "מחיקה נכשלה.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var keepFilterId = FolderFilter?.FolderId == folder.FolderId
                ? FolderFilter.Parent?.FolderId
                : FolderFilter?.FolderId;
            await LoadAsync().ConfigureAwait(true);
            if (keepFilterId is int id)
            {
                var restore = FindFolder(id);
                if (restore is not null)
                    ApplyFolderFilter(restore);
            }

            StatusMessage = $"נמחקה תיקייה: {folder.Title}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AssignFileAsync(object? param)
    {
        // Prefer CommandParameter (context-menu folder). Do not change FolderFilter — right-click is not a filter click.
        var folder = param as FileCatalogFolderNodeVm;
        if (folder is null)
        {
            MessageBox.Show(
                "יש ללחוץ ימני על תיקיית היעד ולבחור «שייך את הקובץ הנבחר לכאן».",
                "ניהול קבצים",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (SelectedFile is null)
        {
            MessageBox.Show("יש לבחור קובץ בטבלה לפני השיוך.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _write
                .AssignFileToFolderAsync(SelectedFile.FileId, folder.FolderId)
                .ConfigureAwait(true);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "שיוך נכשל.", "ניהול קבצים", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedFile.ApplyFolder(folder.FolderId, folder.Title);
            FilesView?.Refresh();
            StatusMessage = $"הקובץ שויך לתיקייה '{folder.Title}'. תצוגה נשארה: {FolderFilterCaption}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseTemplate(FileCatalogFileRowVm? file)
    {
        if (file is null)
            return;

        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() == true)
        {
            file.TemplateLocation = dialog.FileName;
            return;
        }

        if (string.IsNullOrEmpty(file.TemplateLocation))
            return;

        if (MessageBox.Show(
                "לנקות את נתיב התבנית הקיים?",
                "ניהול קבצים",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            file.TemplateLocation = null;
        }
    }

    private FileCatalogFolderNodeVm? FindFolder(int folderId)
    {
        foreach (var root in FolderTree)
        {
            var found = root.Find(folderId);
            if (found is not null)
                return found;
        }

        return null;
    }

    private int CountFolders()
    {
        static int Count(FileCatalogFolderNodeVm n)
            => 1 + n.Children.Sum(Count);

        return FolderTree.Sum(Count);
    }

    private void RaiseCanExecutes()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        SaveChangesCommand.RaiseCanExecuteChanged();
        AddFileCommand.RaiseCanExecuteChanged();
        DeleteFileCommand.RaiseCanExecuteChanged();
        AddJobTypeCommand.RaiseCanExecuteChanged();
        RenameJobTypeCommand.RaiseCanExecuteChanged();
        ConfirmDialogCommand.RaiseCanExecuteChanged();
    }
}
