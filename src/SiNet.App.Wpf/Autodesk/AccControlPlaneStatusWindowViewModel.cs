using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

public sealed class AccControlPlaneStatusWindowViewModel : ObservableObject
{
    private const string RootBrowseLabel = "Project Files";
    private readonly AccControlPlaneStatusPresenter _presenter;
    private readonly IAccDocumentService _accDocumentService;
    private readonly IAccFolderBrowserService _accFolderBrowserService;
    private readonly IAccLookupSeedService _accLookupSeedService;
    private readonly IAccResolvedDocsUrlLauncher _resolvedDocsUrlLauncher;
    private readonly IClipboardTextWriter _clipboardTextWriter;
    private readonly List<AccBrowseLocation> _browseTrail = [];
    private string? _hintText;
    private string _modeSummary = "טוען...";
    private string _keySummary = "טוען...";
    private string _projectsSummary = "טוען...";
    private string _healthSummary = "טוען...";
    private string _diagnosticsSummary = "טוען...";
    private string? _selectedKnownProjectId;
    private string _lookupProjectId = string.Empty;
    private string _lookupFolderId = string.Empty;
    private string _lookupFileName = string.Empty;
    private string _lookupResultSummary = "טרם בוצע חיפוש פריט ACC.";
    private string _lookupResolvedDocsUrl = string.Empty;
    private string _browseSummary = "טרם נטען תוכן ACC.";
    private string _browseTrailText = RootBrowseLabel;
    private string _summaryMessage = string.Empty;
    private bool _isBusy;
    private bool _isSynchronizingKnownProjectSelection;
    private AccFolderBrowseEntry? _selectedBrowseFolder;
    private AccFolderBrowseEntry? _selectedBrowseFile;

    public AccControlPlaneStatusWindowViewModel(
        AccControlPlaneStatusPresenter presenter,
        IAccDocumentService accDocumentService,
        IAccFolderBrowserService accFolderBrowserService,
        IAccLookupSeedService accLookupSeedService,
        IAccResolvedDocsUrlLauncher resolvedDocsUrlLauncher,
        IClipboardTextWriter clipboardTextWriter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _accDocumentService = accDocumentService ?? throw new ArgumentNullException(nameof(accDocumentService));
        _accFolderBrowserService = accFolderBrowserService ?? throw new ArgumentNullException(nameof(accFolderBrowserService));
        _accLookupSeedService = accLookupSeedService ?? throw new ArgumentNullException(nameof(accLookupSeedService));
        _resolvedDocsUrlLauncher = resolvedDocsUrlLauncher ?? throw new ArgumentNullException(nameof(resolvedDocsUrlLauncher));
        _clipboardTextWriter = clipboardTextWriter ?? throw new ArgumentNullException(nameof(clipboardTextWriter));
        KnownProjectIds = [];
        BrowseFolders = [];
        BrowseFiles = [];
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        LoadLookupSeedCommand = new AsyncRelayCommand(LoadLookupSeedAsync, () => !IsBusy);
        BrowseFolderCommand = new AsyncRelayCommand(BrowseFolderAsync, CanBrowseFolder);
        BrowseParentFolderCommand = new AsyncRelayCommand(BrowseParentFolderAsync, CanBrowseParentFolder);
        OpenSelectedFolderCommand = new AsyncRelayCommand(OpenSelectedFolderAsync, CanOpenSelectedFolder);
        UseSelectedFileCommand = new RelayCommand(_ => UseSelectedFile(), _ => CanUseSelectedFile());
        ResolveDocumentCommand = new AsyncRelayCommand(ResolveDocumentAsync, CanResolveDocument);
        CopyResolvedDocsUrlCommand = new RelayCommand(_ => CopyResolvedDocsUrl(), _ => CanUseResolvedDocsUrl());
        OpenResolvedDocsUrlCommand = new RelayCommand(_ => OpenResolvedDocsUrl(), _ => CanUseResolvedDocsUrl());
    }

    public string? HintText
    {
        get => _hintText;
        private set => SetField(ref _hintText, value);
    }

    public string ModeSummary
    {
        get => _modeSummary;
        private set => SetField(ref _modeSummary, value);
    }

    public string KeySummary
    {
        get => _keySummary;
        private set => SetField(ref _keySummary, value);
    }

    public string ProjectsSummary
    {
        get => _projectsSummary;
        private set => SetField(ref _projectsSummary, value);
    }

    public string HealthSummary
    {
        get => _healthSummary;
        private set => SetField(ref _healthSummary, value);
    }

    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetField(ref _diagnosticsSummary, value);
    }

    public ObservableCollection<string> KnownProjectIds { get; }

    public string? SelectedKnownProjectId
    {
        get => _selectedKnownProjectId;
        set
        {
            if (SetField(ref _selectedKnownProjectId, value)
                && !_isSynchronizingKnownProjectSelection
                && !string.IsNullOrWhiteSpace(value))
            {
                LookupProjectId = value;
                ResetBrowseState(clearFolderId: true, clearFileName: true);
                SummaryMessage = "נבחר projectId מתוך הרשימה המוכרת.";
            }
        }
    }

    public string LookupProjectId
    {
        get => _lookupProjectId;
        set
        {
            if (SetField(ref _lookupProjectId, value))
            {
                BrowseFolderCommand.RaiseCanExecuteChanged();
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LookupFolderId
    {
        get => _lookupFolderId;
        set
        {
            if (SetField(ref _lookupFolderId, value))
            {
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LookupFileName
    {
        get => _lookupFileName;
        set
        {
            if (SetField(ref _lookupFileName, value))
            {
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LookupResultSummary
    {
        get => _lookupResultSummary;
        private set => SetField(ref _lookupResultSummary, value);
    }

    public string LookupResolvedDocsUrl
    {
        get => _lookupResolvedDocsUrl;
        private set
        {
            if (SetField(ref _lookupResolvedDocsUrl, value))
            {
                CopyResolvedDocsUrlCommand.RaiseCanExecuteChanged();
                OpenResolvedDocsUrlCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BrowseSummary
    {
        get => _browseSummary;
        private set => SetField(ref _browseSummary, value);
    }

    public string BrowseTrailText
    {
        get => _browseTrailText;
        private set => SetField(ref _browseTrailText, value);
    }

    public ObservableCollection<AccFolderBrowseEntry> BrowseFolders { get; }

    public ObservableCollection<AccFolderBrowseEntry> BrowseFiles { get; }

    public AccFolderBrowseEntry? SelectedBrowseFolder
    {
        get => _selectedBrowseFolder;
        set
        {
            if (SetField(ref _selectedBrowseFolder, value))
            {
                BrowseParentFolderCommand.RaiseCanExecuteChanged();
                OpenSelectedFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AccFolderBrowseEntry? SelectedBrowseFile
    {
        get => _selectedBrowseFile;
        set
        {
            if (SetField(ref _selectedBrowseFile, value))
            {
                UseSelectedFileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SummaryMessage
    {
        get => _summaryMessage;
        private set => SetField(ref _summaryMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                LoadLookupSeedCommand.RaiseCanExecuteChanged();
                BrowseFolderCommand.RaiseCanExecuteChanged();
                BrowseParentFolderCommand.RaiseCanExecuteChanged();
                OpenSelectedFolderCommand.RaiseCanExecuteChanged();
                UseSelectedFileCommand.RaiseCanExecuteChanged();
                ResolveDocumentCommand.RaiseCanExecuteChanged();
                CopyResolvedDocsUrlCommand.RaiseCanExecuteChanged();
                OpenResolvedDocsUrlCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand LoadLookupSeedCommand { get; }

    public AsyncRelayCommand BrowseFolderCommand { get; }

    public AsyncRelayCommand BrowseParentFolderCommand { get; }

    public AsyncRelayCommand OpenSelectedFolderCommand { get; }

    public RelayCommand UseSelectedFileCommand { get; }

    public AsyncRelayCommand ResolveDocumentCommand { get; }

    public RelayCommand CopyResolvedDocsUrlCommand { get; }

    public RelayCommand OpenResolvedDocsUrlCommand { get; }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var presentation = await _presenter
                .BuildAsync(AccControlPlaneStatusPresentationKind.StatusWindow)
                .ConfigureAwait(true);

            HintText = presentation.Hint;
            ModeSummary = presentation.ModeSummary;
            KeySummary = presentation.KeySummary;
            ProjectsSummary = presentation.ProjectsSummary;
            ReplaceKnownProjectIds(presentation.KnownProjectIds);
            UpdateSelectedKnownProjectId(ResolveMatchingKnownProjectId(LookupProjectId));
            HealthSummary = presentation.HealthSummary;
            DiagnosticsSummary = presentation.DiagnosticsSummary;
            SummaryMessage = "סטטוס ACC נטען.";
        }
        catch (Exception ex)
        {
            SummaryMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanResolveDocument() =>
        !string.IsNullOrWhiteSpace(LookupProjectId)
        && !string.IsNullOrWhiteSpace(LookupFolderId)
        && !string.IsNullOrWhiteSpace(LookupFileName);

    private bool CanBrowseFolder() =>
        !IsBusy && !string.IsNullOrWhiteSpace(LookupProjectId);

    private bool CanBrowseParentFolder() =>
        !IsBusy && _browseTrail.Count > 1;

    private bool CanOpenSelectedFolder() =>
        !IsBusy && SelectedBrowseFolder is not null;

    private bool CanUseSelectedFile() =>
        !IsBusy && SelectedBrowseFile is not null;

    private bool CanUseResolvedDocsUrl() =>
        !IsBusy && !string.IsNullOrWhiteSpace(LookupResolvedDocsUrl);

    public async Task ResolveDocumentAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _accDocumentService
                .FindItemAsync(
                    LookupProjectId.Trim(),
                    LookupFolderId.Trim(),
                    LookupFileName.Trim())
                .ConfigureAwait(true);

            if (result is null)
            {
                LookupResultSummary = "פריט ACC לא נמצא עבור projectId + folderId + fileName שסופקו.";
                LookupResolvedDocsUrl = string.Empty;
            }
            else
            {
                var versionText = string.IsNullOrWhiteSpace(result.VersionId) ? "(none)" : result.VersionId;
                var viewerText = string.IsNullOrWhiteSpace(result.ViewerUrl) ? "(none)" : result.ViewerUrl;
                LookupResolvedDocsUrl = AccResolvedDocsUrlBuilder.Build(result.ProjectId, LookupFolderId.Trim(), result.ItemId);
                LookupResultSummary =
                    $"נמצא פריט ACC: projectId={result.ProjectId}; itemId={result.ItemId}; versionId={versionText}; viewerUrl={viewerText}";
            }

            SummaryMessage = "בדיקת lookup של פריט ACC הושלמה.";
        }
        catch (Exception ex)
        {
            LookupResultSummary = $"שגיאה ב-lookup של פריט ACC: {ex.Message}";
            LookupResolvedDocsUrl = string.Empty;
            SummaryMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadLookupSeedAsync()
    {
        IsBusy = true;
        try
        {
            var seeds = await _accLookupSeedService.GetRecentSeedsAsync().ConfigureAwait(true);
            var seed = seeds.FirstOrDefault();
            if (seed is null)
            {
                SummaryMessage = "לא נמצאה דוגמת lookup מתאימה ב-DB.";
                return;
            }

            LookupProjectId = seed.ProjectId;
            LookupFolderId = seed.FolderId;
            LookupFileName = seed.FileName;
            UpdateSelectedKnownProjectId(ResolveMatchingKnownProjectId(seed.ProjectId));
            ResetBrowseState(clearFolderId: false, clearFileName: false);
            LookupResultSummary =
                $"נטענה דוגמה מה-DB: projectId={seed.ProjectId}; folderId={seed.FolderId}; fileName={seed.FileName}; source={seed.SourceLabel}";
            SummaryMessage = $"נטענה דוגמת lookup מה-DB ({seeds.Count} מועמדים זמינים).";
        }
        catch (Exception ex)
        {
            SummaryMessage = $"שגיאה בטעינת דוגמת lookup מה-DB: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BrowseFolderAsync()
    {
        var requestedFolderId = string.IsNullOrWhiteSpace(LookupFolderId) ? null : LookupFolderId.Trim();
        var requestedLabel = string.IsNullOrWhiteSpace(requestedFolderId) ? RootBrowseLabel : requestedFolderId;
        await BrowseFolderCoreAsync(LookupProjectId.Trim(), requestedFolderId, requestedLabel, AccBrowseNavigationMode.Reset).ConfigureAwait(true);
    }

    public async Task OpenSelectedFolderAsync()
    {
        if (!CanOpenSelectedFolder())
        {
            return;
        }

        await BrowseFolderCoreAsync(
                LookupProjectId.Trim(),
                SelectedBrowseFolder!.Id,
                SelectedBrowseFolder.DisplayName,
                AccBrowseNavigationMode.Child)
            .ConfigureAwait(true);
    }

    public async Task BrowseParentFolderAsync()
    {
        if (!CanBrowseParentFolder())
        {
            return;
        }

        var parent = _browseTrail[^2];
        await BrowseFolderCoreAsync(
                LookupProjectId.Trim(),
                parent.FolderId,
                parent.DisplayName,
                AccBrowseNavigationMode.Back)
            .ConfigureAwait(true);
    }

    private void UseSelectedFile()
    {
        if (!CanUseSelectedFile())
        {
            return;
        }

        LookupFileName = SelectedBrowseFile!.DisplayName;
        LookupResolvedDocsUrl = string.Empty;
        LookupResultSummary = $"נבחר קובץ ל-resolve: {SelectedBrowseFile.DisplayName}";
        SummaryMessage = "נבחר קובץ מתיקיית ACC.";
    }

    private void CopyResolvedDocsUrl()
    {
        if (!CanUseResolvedDocsUrl())
        {
            return;
        }

        try
        {
            _clipboardTextWriter.SetText(LookupResolvedDocsUrl);
            SummaryMessage = "קישור ACC Docs הועתק ללוח.";
        }
        catch (Exception ex)
        {
            SummaryMessage = $"שגיאה בהעתקת קישור ACC Docs: {ex.Message}";
        }
    }

    private void OpenResolvedDocsUrl()
    {
        if (!CanUseResolvedDocsUrl())
        {
            return;
        }

        try
        {
            _resolvedDocsUrlLauncher.Open(LookupResolvedDocsUrl);
            SummaryMessage = "ACC Docs נפתח בדפדפן ברירת המחדל.";
        }
        catch (Exception ex)
        {
            SummaryMessage = $"שגיאה בפתיחת ACC Docs בדפדפן: {ex.Message}";
        }
    }

    private async Task BrowseFolderCoreAsync(
        string projectId,
        string? requestedFolderId,
        string requestedLabel,
        AccBrowseNavigationMode navigationMode)
    {
        IsBusy = true;
        try
        {
            var result = await _accFolderBrowserService
                .BrowseAsync(projectId, requestedFolderId)
                .ConfigureAwait(true);
            if (result is null)
            {
                BrowseFolders.Clear();
                BrowseFiles.Clear();
                SelectedBrowseFolder = null;
                SelectedBrowseFile = null;
                if (navigationMode == AccBrowseNavigationMode.Reset)
                {
                    ResetBrowseTrail();
                }

                BrowseSummary = "לא נמצא תוכן ACC עבור הפרויקט/תיקייה שסופקו.";
                SummaryMessage = BrowseSummary;
                return;
            }

            LookupProjectId = result.ProjectId;
            UpdateSelectedKnownProjectId(ResolveMatchingKnownProjectId(result.ProjectId));
            LookupFolderId = result.FolderId;
            LookupResolvedDocsUrl = string.Empty;
            LookupResultSummary = "טרם בוצע resolve עבור קובץ נבחר.";
            SummaryMessage = "נטען תוכן תיקיית ACC.";

            BrowseFolders.Clear();
            BrowseFiles.Clear();
            foreach (var entry in result.Entries.Where(static entry => entry.Kind == AccFolderEntryKind.Folder))
            {
                BrowseFolders.Add(entry);
            }

            foreach (var entry in result.Entries.Where(static entry => entry.Kind == AccFolderEntryKind.Item))
            {
                BrowseFiles.Add(entry);
            }

            SelectedBrowseFolder = BrowseFolders.FirstOrDefault();
            SelectedBrowseFile = BrowseFiles.FirstOrDefault();
            UpdateBrowseTrail(navigationMode, result.FolderId, requestedLabel);
            BrowseSummary = $"נטענו {BrowseFolders.Count} תיקיות ו-{BrowseFiles.Count} קבצים מתוך folderId={result.FolderId}.";
        }
        catch (Exception ex)
        {
            BrowseSummary = $"שגיאה בטעינת תוכן ACC: {ex.Message}";
            SummaryMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceKnownProjectIds(IReadOnlyList<string> projectIds)
    {
        KnownProjectIds.Clear();
        foreach (var projectId in projectIds)
        {
            KnownProjectIds.Add(projectId);
        }
    }

    private string? ResolveMatchingKnownProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId)
            ? null
            : KnownProjectIds.FirstOrDefault(knownProjectId =>
                string.Equals(knownProjectId, projectId.Trim(), StringComparison.OrdinalIgnoreCase));

    private void UpdateSelectedKnownProjectId(string? projectId)
    {
        _isSynchronizingKnownProjectSelection = true;
        try
        {
            SetField(ref _selectedKnownProjectId, projectId);
        }
        finally
        {
            _isSynchronizingKnownProjectSelection = false;
        }
    }

    private void ResetBrowseState(bool clearFolderId, bool clearFileName)
    {
        if (clearFolderId)
        {
            LookupFolderId = string.Empty;
        }

        if (clearFileName)
        {
            LookupFileName = string.Empty;
        }

        LookupResolvedDocsUrl = string.Empty;
        LookupResultSummary = "טרם בוצע חיפוש פריט ACC.";
        BrowseFolders.Clear();
        BrowseFiles.Clear();
        SelectedBrowseFolder = null;
        SelectedBrowseFile = null;
        BrowseSummary = "טרם נטען תוכן ACC.";
        ResetBrowseTrail();
    }

    private void ResetBrowseTrail()
    {
        _browseTrail.Clear();
        BrowseTrailText = RootBrowseLabel;
        BrowseParentFolderCommand.RaiseCanExecuteChanged();
    }

    private void UpdateBrowseTrail(
        AccBrowseNavigationMode navigationMode,
        string folderId,
        string folderLabel)
    {
        switch (navigationMode)
        {
            case AccBrowseNavigationMode.Reset:
                _browseTrail.Clear();
                _browseTrail.Add(new AccBrowseLocation(folderId, folderLabel));
                break;

            case AccBrowseNavigationMode.Child:
                if (_browseTrail.Count == 0
                    || !string.Equals(_browseTrail[^1].FolderId, folderId, StringComparison.OrdinalIgnoreCase))
                {
                    _browseTrail.Add(new AccBrowseLocation(folderId, folderLabel));
                }
                break;

            case AccBrowseNavigationMode.Back:
                if (_browseTrail.Count > 1)
                {
                    _browseTrail.RemoveAt(_browseTrail.Count - 1);
                }
                else
                {
                    _browseTrail.Clear();
                    _browseTrail.Add(new AccBrowseLocation(folderId, folderLabel));
                }
                break;
        }

        BrowseTrailText = string.Join(" / ", _browseTrail.Select(static location => location.DisplayName));
        BrowseParentFolderCommand.RaiseCanExecuteChanged();
    }
    private sealed record AccBrowseLocation(string FolderId, string DisplayName);

    private enum AccBrowseNavigationMode
    {
        Reset = 0,
        Child = 1,
        Back = 2,
    }
}
