using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

public sealed class AccReadOnlyDocumentBrowserViewModel : ObservableObject
{
    private const string RootBrowseLabel = "Project Files";
    private const string LiveDiscoveryInitialSummary = "טרם בוצעה טעינת hubs/projects חיה מ-ACC.";

    private readonly IAccDocumentService _accDocumentService;
    private readonly IAccFolderBrowserService _accFolderBrowserService;
    private readonly IAccLiveProjectDiscoveryService _accLiveProjectDiscoveryService;
    private readonly IAccResolvedDocsUrlLauncher _resolvedDocsUrlLauncher;
    private readonly IClipboardTextWriter _clipboardTextWriter;
    private readonly Func<bool>? _canInteract;
    private readonly Func<bool>? _isHostBusy;
    private readonly Action<string>? _summaryMessageSink;
    private readonly List<AccBrowseLocation> _browseTrail = [];

    private AccHubCatalogEntry? _selectedLiveHub;
    private AccProjectCatalogEntry? _selectedLiveProject;
    private AccProjectCatalogEntry? _selectedKnownProject;
    private string? _selectedKnownProjectId;
    private string _lookupProjectId = string.Empty;
    private string _lookupFolderId = string.Empty;
    private string _lookupFileName = string.Empty;
    private string _lookupResultSummary = "טרם בוצע חיפוש פריט ACC.";
    private string _lookupResolvedDocsUrl = string.Empty;
    private string _browseSummary = "טרם נטען תוכן ACC.";
    private string _browseTrailText = RootBrowseLabel;
    private string _liveDiscoverySummary = LiveDiscoveryInitialSummary;
    private bool _isBusy;
    private bool _isSynchronizingKnownProjectSelection;
    private AccFolderBrowseEntry? _selectedBrowseFolder;
    private AccFolderBrowseEntry? _selectedBrowseFile;

    public AccReadOnlyDocumentBrowserViewModel(
        IAccDocumentService accDocumentService,
        IAccFolderBrowserService accFolderBrowserService,
        IAccLiveProjectDiscoveryService accLiveProjectDiscoveryService,
        IAccResolvedDocsUrlLauncher resolvedDocsUrlLauncher,
        IClipboardTextWriter clipboardTextWriter,
        Func<bool>? canInteract = null,
        Func<bool>? isHostBusy = null,
        Action<string>? summaryMessageSink = null)
    {
        _accDocumentService = accDocumentService ?? throw new ArgumentNullException(nameof(accDocumentService));
        _accFolderBrowserService = accFolderBrowserService ?? throw new ArgumentNullException(nameof(accFolderBrowserService));
        _accLiveProjectDiscoveryService = accLiveProjectDiscoveryService ?? throw new ArgumentNullException(nameof(accLiveProjectDiscoveryService));
        _resolvedDocsUrlLauncher = resolvedDocsUrlLauncher ?? throw new ArgumentNullException(nameof(resolvedDocsUrlLauncher));
        _clipboardTextWriter = clipboardTextWriter ?? throw new ArgumentNullException(nameof(clipboardTextWriter));
        _canInteract = canInteract;
        _isHostBusy = isHostBusy;
        _summaryMessageSink = summaryMessageSink;

        LiveHubs = [];
        LiveProjects = [];
        KnownProjects = [];
        KnownProjectIds = [];
        BrowseFolders = [];
        BrowseFiles = [];

        LoadLiveHubsCommand = new AsyncRelayCommand(LoadLiveHubsAsync, CanLoadLiveHubs);
        LoadLiveProjectsCommand = new AsyncRelayCommand(LoadLiveProjectsAsync, CanLoadLiveProjects);
        UseSelectedLiveProjectCommand = new AsyncRelayCommand(UseSelectedLiveProjectAsync, CanUseSelectedLiveProject);
        BrowseFolderCommand = new AsyncRelayCommand(BrowseFolderAsync, CanBrowseFolder);
        BrowseParentFolderCommand = new AsyncRelayCommand(BrowseParentFolderAsync, CanBrowseParentFolder);
        OpenSelectedFolderCommand = new AsyncRelayCommand(OpenSelectedFolderAsync, CanOpenSelectedFolder);
        UseSelectedFileCommand = new RelayCommand(_ => UseSelectedFile(), _ => CanUseSelectedFile());
        ResolveDocumentCommand = new AsyncRelayCommand(ResolveDocumentAsync, CanResolveDocument);
        CopyResolvedDocsUrlCommand = new RelayCommand(_ => CopyResolvedDocsUrl(), _ => CanUseResolvedDocsUrl());
        OpenResolvedDocsUrlCommand = new RelayCommand(_ => OpenResolvedDocsUrl(), _ => CanUseResolvedDocsUrl());
    }

    public ObservableCollection<AccHubCatalogEntry> LiveHubs { get; }

    public ObservableCollection<AccProjectCatalogEntry> LiveProjects { get; }

    public string LiveDiscoverySummary
    {
        get => _liveDiscoverySummary;
        private set => SetField(ref _liveDiscoverySummary, value);
    }

    public AccHubCatalogEntry? SelectedLiveHub
    {
        get => _selectedLiveHub;
        set
        {
            if (SetField(ref _selectedLiveHub, value))
            {
                LiveProjects.Clear();
                SelectedLiveProject = null;
                LoadLiveProjectsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AccProjectCatalogEntry? SelectedLiveProject
    {
        get => _selectedLiveProject;
        set
        {
            if (SetField(ref _selectedLiveProject, value))
            {
                UseSelectedLiveProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<AccProjectCatalogEntry> KnownProjects { get; }

    public ObservableCollection<string> KnownProjectIds { get; }

    public AccProjectCatalogEntry? SelectedKnownProject
    {
        get => _selectedKnownProject;
        set
        {
            if (SetField(ref _selectedKnownProject, value))
            {
                _selectedKnownProjectId = value?.ProjectId;
                OnPropertyChanged(nameof(SelectedKnownProjectId));
                if (!_isSynchronizingKnownProjectSelection && value is not null)
                {
                    LookupProjectId = value.ProjectId;
                    ResetBrowseState(clearFolderId: true, clearFileName: true);
                    PublishSummary("נבחר projectId מתוך הרשימה המוכרת.");
                }
            }
        }
    }

    public string? SelectedKnownProjectId
    {
        get => _selectedKnownProjectId;
        set
        {
            if (string.Equals(_selectedKnownProjectId, value, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                UpdateSelectedKnownProject(null);
                return;
            }

            var match = ResolveMatchingKnownProject(value);
            if (match is not null)
            {
                SelectedKnownProject = match;
            }
            else if (SetField(ref _selectedKnownProjectId, value))
            {
                UpdateSelectedKnownProject(null);
                LookupProjectId = value;
                ResetBrowseState(clearFolderId: true, clearFileName: true);
                PublishSummary("הוזן projectId ידני שאינו מופיע ברשימה המוכרת.");
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
                UpdateSelectedKnownProject(ResolveMatchingKnownProject(value));
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

    public AsyncRelayCommand BrowseFolderCommand { get; }

    public AsyncRelayCommand BrowseParentFolderCommand { get; }

    public AsyncRelayCommand OpenSelectedFolderCommand { get; }

    public RelayCommand UseSelectedFileCommand { get; }

    public AsyncRelayCommand ResolveDocumentCommand { get; }

    public RelayCommand CopyResolvedDocsUrlCommand { get; }

    public RelayCommand OpenResolvedDocsUrlCommand { get; }

    public AsyncRelayCommand LoadLiveHubsCommand { get; }

    public AsyncRelayCommand LoadLiveProjectsCommand { get; }

    public AsyncRelayCommand UseSelectedLiveProjectCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyHostStateChanged();
            }
        }
    }

    public void NotifyHostStateChanged()
    {
        LoadLiveHubsCommand.RaiseCanExecuteChanged();
        LoadLiveProjectsCommand.RaiseCanExecuteChanged();
        UseSelectedLiveProjectCommand.RaiseCanExecuteChanged();
        BrowseFolderCommand.RaiseCanExecuteChanged();
        BrowseParentFolderCommand.RaiseCanExecuteChanged();
        OpenSelectedFolderCommand.RaiseCanExecuteChanged();
        UseSelectedFileCommand.RaiseCanExecuteChanged();
        ResolveDocumentCommand.RaiseCanExecuteChanged();
        CopyResolvedDocsUrlCommand.RaiseCanExecuteChanged();
        OpenResolvedDocsUrlCommand.RaiseCanExecuteChanged();
    }

    public void LoadKnownProjectIds(IReadOnlyList<string> projectIds)
    {
        KnownProjects.Clear();
        KnownProjectIds.Clear();
        foreach (var projectId in projectIds)
        {
            KnownProjects.Add(new AccProjectCatalogEntry(projectId, projectId, "ProjectIdList"));
            KnownProjectIds.Add(projectId);
        }

        UpdateSelectedKnownProject(ResolveMatchingKnownProject(LookupProjectId));
    }

    public void LoadKnownProjects(IReadOnlyList<AccProjectCatalogEntry> projects)
    {
        KnownProjects.Clear();
        KnownProjectIds.Clear();
        foreach (var project in projects)
        {
            KnownProjects.Add(project);
            KnownProjectIds.Add(project.ProjectId);
        }

        UpdateSelectedKnownProject(ResolveMatchingKnownProject(LookupProjectId));
    }

    public void ApplyLookupSeed(AccDocumentLookupSeed seed, string summaryMessage)
    {
        ArgumentNullException.ThrowIfNull(seed);

        LookupProjectId = seed.ProjectId;
        LookupFolderId = seed.FolderId;
        LookupFileName = seed.FileName;
        UpdateSelectedKnownProject(ResolveMatchingKnownProject(seed.ProjectId));
        ResetBrowseState(clearFolderId: false, clearFileName: false);
        LookupResultSummary = summaryMessage;
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

    public async Task ResolveDocumentAsync()
    {
        if (!CanResolveDocument())
        {
            return;
        }

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

            PublishSummary("בדיקת lookup של פריט ACC הושלמה.");
        }
        catch (Exception ex)
        {
            LookupResultSummary = $"שגיאה ב-lookup של פריט ACC: {ex.Message}";
            LookupResolvedDocsUrl = string.Empty;
            PublishSummary(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadLiveHubsAsync()
    {
        if (!CanLoadLiveHubs())
        {
            return;
        }

        IsBusy = true;
        try
        {
            var hubs = await _accLiveProjectDiscoveryService
                .GetHubsAsync()
                .ConfigureAwait(true);

            LiveHubs.Clear();
            foreach (var hub in hubs)
            {
                LiveHubs.Add(hub);
            }

            LiveProjects.Clear();
            SelectedLiveProject = null;
            SelectedLiveHub = LiveHubs.FirstOrDefault();
            LiveDiscoverySummary = hubs.Count == 0
                ? "לא נמצאו hubs חיים ב-ACC."
                : $"נטענו {hubs.Count} hubs חיים מ-ACC.";
            PublishSummary(LiveDiscoverySummary);
        }
        catch (Exception ex)
        {
            LiveDiscoverySummary = $"שגיאה בטעינת hubs חיים מ-ACC: {ex.Message}";
            PublishSummary(LiveDiscoverySummary);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadLiveProjectsAsync()
    {
        if (!CanLoadLiveProjects())
        {
            return;
        }

        IsBusy = true;
        try
        {
            var projects = await _accLiveProjectDiscoveryService
                .GetProjectsAsync(SelectedLiveHub!.HubId)
                .ConfigureAwait(true);

            LiveProjects.Clear();
            foreach (var project in projects)
            {
                LiveProjects.Add(project);
            }

            SelectedLiveProject = ResolveMatchingLiveProject(LookupProjectId) ?? LiveProjects.FirstOrDefault();
            LiveDiscoverySummary = projects.Count == 0
                ? $"לא נמצאו פרויקטים חיים ב-hub {SelectedLiveHub.HubId}."
                : $"נטענו {projects.Count} פרויקטים חיים מתוך hub {SelectedLiveHub.HubId}.";
            PublishSummary(LiveDiscoverySummary);
        }
        catch (Exception ex)
        {
            LiveDiscoverySummary = $"שגיאה בטעינת פרויקטים חיים מ-ACC: {ex.Message}";
            PublishSummary(LiveDiscoverySummary);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanResolveDocument() =>
        !IsInteractionBlocked()
        && !string.IsNullOrWhiteSpace(LookupProjectId)
        && !string.IsNullOrWhiteSpace(LookupFolderId)
        && !string.IsNullOrWhiteSpace(LookupFileName);

    private bool CanLoadLiveHubs() =>
        !IsInteractionBlocked();

    private bool CanLoadLiveProjects() =>
        !IsInteractionBlocked()
        && SelectedLiveHub is not null;

    private bool CanUseSelectedLiveProject() =>
        !IsInteractionBlocked()
        && SelectedLiveProject is not null;

    private bool CanBrowseFolder() =>
        !IsInteractionBlocked()
        && !string.IsNullOrWhiteSpace(LookupProjectId);

    private bool CanBrowseParentFolder() =>
        !IsInteractionBlocked()
        && _browseTrail.Count > 1;

    private bool CanOpenSelectedFolder() =>
        !IsInteractionBlocked()
        && SelectedBrowseFolder is not null;

    private bool CanUseSelectedFile() =>
        !IsInteractionBlocked()
        && SelectedBrowseFile is not null;

    private bool CanUseResolvedDocsUrl() =>
        !IsInteractionBlocked()
        && !string.IsNullOrWhiteSpace(LookupResolvedDocsUrl);

    private bool IsInteractionBlocked() =>
        IsBusy
        || (_isHostBusy?.Invoke() ?? false)
        || !(_canInteract?.Invoke() ?? true);

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
                PublishSummary(BrowseSummary);
                return;
            }

            LookupProjectId = result.ProjectId;
            UpdateSelectedKnownProject(ResolveMatchingKnownProject(result.ProjectId));
            LookupFolderId = result.FolderId;
            LookupResolvedDocsUrl = string.Empty;
            LookupResultSummary = "טרם בוצע resolve עבור קובץ נבחר.";

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
            PublishSummary("נטען תוכן תיקיית ACC.");
        }
        catch (Exception ex)
        {
            BrowseSummary = $"שגיאה בטעינת תוכן ACC: {ex.Message}";
            PublishSummary(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
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
        PublishSummary("נבחר קובץ מתיקיית ACC.");
    }

    public async Task UseSelectedLiveProjectAsync()
    {
        if (!CanUseSelectedLiveProject())
        {
            return;
        }

        var selectedProject = EnsureKnownProject(SelectedLiveProject!);
        SelectedKnownProject = selectedProject;
        LiveDiscoverySummary = $"נבחר פרויקט live ונפתחת תיקיית Project Files: {selectedProject.DisplayText}";
        PublishSummary("נבחר פרויקט חי מ-ACC.");
        await BrowseFolderCoreAsync(selectedProject.ProjectId, null, RootBrowseLabel, AccBrowseNavigationMode.Reset).ConfigureAwait(true);
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
            PublishSummary("קישור ACC Docs הועתק ללוח.");
        }
        catch (Exception ex)
        {
            PublishSummary($"שגיאה בהעתקת קישור ACC Docs: {ex.Message}");
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
            PublishSummary("ACC Docs נפתח בדפדפן ברירת המחדל.");
        }
        catch (Exception ex)
        {
            PublishSummary($"שגיאה בפתיחת ACC Docs בדפדפן: {ex.Message}");
        }
    }

    private void UpdateSelectedKnownProject(AccProjectCatalogEntry? project)
    {
        _isSynchronizingKnownProjectSelection = true;
        try
        {
            _selectedKnownProjectId = project?.ProjectId;
            SetField(ref _selectedKnownProject, project);
            OnPropertyChanged(nameof(SelectedKnownProjectId));
        }
        finally
        {
            _isSynchronizingKnownProjectSelection = false;
        }
    }

    private AccProjectCatalogEntry? ResolveMatchingKnownProject(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId)
            ? null
            : KnownProjects.FirstOrDefault(knownProject =>
                string.Equals(knownProject.ProjectId, projectId.Trim(), StringComparison.OrdinalIgnoreCase));

    private AccProjectCatalogEntry? ResolveMatchingLiveProject(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId)
            ? null
            : LiveProjects.FirstOrDefault(liveProject =>
                string.Equals(liveProject.ProjectId, projectId.Trim(), StringComparison.OrdinalIgnoreCase));

    private AccProjectCatalogEntry EnsureKnownProject(AccProjectCatalogEntry project)
    {
        var existing = ResolveMatchingKnownProject(project.ProjectId);
        if (existing is not null)
        {
            return existing;
        }

        KnownProjects.Add(project);
        KnownProjectIds.Add(project.ProjectId);
        return project;
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

    private void PublishSummary(string message) => _summaryMessageSink?.Invoke(message);

    private sealed record AccBrowseLocation(string FolderId, string DisplayName);

    private enum AccBrowseNavigationMode
    {
        Reset = 0,
        Child = 1,
        Back = 2,
    }
}
