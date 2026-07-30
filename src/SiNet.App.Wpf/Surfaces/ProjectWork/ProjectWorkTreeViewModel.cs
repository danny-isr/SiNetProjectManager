using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Owns the unified project file/folder tree for the ProjectWork surface: loads the DB-defined folder
/// skeleton via <see cref="IProjectFileQueryService"/>, scans every relevant storage backend through
/// <see cref="IFileIndexService"/>, and integrates scanned files into folder/file/alternative/version
/// nodes. Also serves as the process-wide <see cref="IActiveFileQueryService"/> / <see cref="IFileOpenService"/>
/// provider while a tree is loaded. Phase 1 is read-only (no drag/drop, replace or ACC write).
/// </summary>
public sealed class ProjectWorkTreeViewModel : ObservableObject, IActiveFileQueryService, IFileOpenService, IDisposable
{
    private const string UnfiledBucketTitle = "\u05DC\u05D0 \u05DE\u05E9\u05D5\u05D9\u05DA \u05DC\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8";

    private readonly IProjectFileQueryService _query;
    private readonly IFileIndexService _index;
    private readonly IActiveFileQueryHub _activeHub;
    private readonly IFileOpenHub _openHub;
    private readonly IAccViewerHost? _accViewerHost;
    private readonly IFileServerWatcher? _watcher;
    private readonly IProjectFolderPathResolver? _folderPathResolver;
    private readonly IAccWritePolicy? _writePolicy;
    private readonly IProjectFolderWriteService? _folderWrite;

    private readonly Dictionary<int, ProjectFolderNodeVm> _foldersById = new();
    private readonly List<(ProjectFolderNodeVm Node, ProjectFolderDto Dto)> _scanTargets = new();
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    private bool _isScanning;
    private string _scanStatus = string.Empty;
    private int _currentProjectId;
    private int? _currentProjectNumber;
    private string? _currentProjectNameAndNumber;

    public ProjectWorkTreeViewModel(
        IProjectFileQueryService query,
        IFileIndexService index,
        IActiveFileQueryHub activeHub,
        IFileOpenHub openHub,
        IAccViewerHost? accViewerHost = null,
        IFileServerWatcher? watcher = null,
        IProjectFolderPathResolver? folderPathResolver = null,
        IAccWritePolicy? writePolicy = null,
        IProjectFolderWriteService? folderWrite = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(activeHub);
        ArgumentNullException.ThrowIfNull(openHub);
        _query = query;
        _index = index;
        _activeHub = activeHub;
        _openHub = openHub;
        _accViewerHost = accViewerHost;
        _watcher = watcher;
        _folderPathResolver = folderPathResolver;
        _writePolicy = writePolicy;
        _folderWrite = folderWrite;
        _index.InFlightChanged += OnInFlightChanged;
        if (_accViewerHost is not null)
            _accViewerHost.TabClosed += OnAccTabClosed;
    }

    /// <summary>True when ACC write operations are enabled by the ACC-write gate.</summary>
    public bool IsAccWriteEnabled => _writePolicy?.IsWriteEnabled == true;

    /// <summary>Top-level project folders shown in the tree.</summary>
    public ObservableCollection<ProjectWorkNodeVm> RootFolders { get; } = new();

    /// <summary>True while a background scan is running.</summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set => SetField(ref _isScanning, value);
    }

    /// <summary>Human-readable scan/status text for the bottom bar.</summary>
    public string ScanStatus
    {
        get => _scanStatus;
        private set => SetField(ref _scanStatus, value);
    }

    // ── IActiveFileQueryService ──────────────────────────────────────────────
    /// <inheritdoc />
    public bool IsAvailable => RootFolders.Count > 0;

    /// <inheritdoc />
    public int? CurrentProjectNumber => _currentProjectNumber;

    /// <summary>
    /// Loads the tree for a project: builds the DB folder skeleton, then scans and integrates files.
    /// Cancels any in-flight load for a previous project.
    /// </summary>
    public async Task LoadProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCts.Token;

        _currentProjectId = projectId;
        _watcher?.StopAll();
        RootFolders.Clear();
        _foldersById.Clear();
        _scanTargets.Clear();

        try
        {
            var tree = await _query.GetProjectFileTreeAsync(projectId, token).ConfigureAwait(true);
            if (tree is null)
            {
                _currentProjectNumber = null;
                _currentProjectNameAndNumber = null;
                ScanStatus = "הפרויקט לא נמצא או שאין לו עץ קבצים.";
                RegisterProviders();
                return;
            }

            _currentProjectNumber = tree.ProjectNumber;
            _currentProjectNameAndNumber = tree.ProjectNameAndNumber;

            foreach (var rootDto in tree.RootFolders)
            {
                var node = BuildFolder(rootDto);
                node.IsExpanded = true;
                RootFolders.Add(node);
            }

            await ResolveFolderPathsAsync(projectId, token).ConfigureAwait(true);
            RegisterProviders();

            await RunScanAsync(projectId, token).ConfigureAwait(true);

            if (!token.IsCancellationRequested)
                await StartWatchingAsync(projectId, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            ScanStatus = "הסריקה בוטלה";
            RegisterProviders();
        }
        catch (Exception ex)
        {
            ScanStatus = $"טעינת עץ הקבצים נכשלה: {ex.Message}";
            RegisterProviders();
            throw;
        }
    }

    /// <summary>
    /// Re-scans every folder's files in place (debounced file-watcher trigger), preserving the folder
    /// skeleton and its expand/selection state — only file/alternative/version nodes are rebuilt.
    /// </summary>
    public async Task RescanAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProjectId <= 0 || _scanTargets.Count == 0)
            return;

        foreach (var (node, dto) in _scanTargets)
        {
            ClearFileNodes(node);
            AddFileDefNodes(node, dto);
        }

        await RunScanAsync(_currentProjectId, cancellationToken).ConfigureAwait(true);
    }

    private async Task RunScanAsync(int projectId, CancellationToken token)
    {
        IsScanning = true;
        ScanStatus = "\u05e1\u05d5\u05e8\u05e7 \u05e7\u05d1\u05e6\u05d9\u05dd\u2026"; // "Scanning files…"

        try
        {
            var scannedCount = 0;
            foreach (var (node, dto) in _scanTargets)
            {
                if (token.IsCancellationRequested)
                    break;
                scannedCount += await ScanFolderAsync(projectId, node, dto, token).ConfigureAwait(true);
            }

            foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
            {
                RefreshHasFiles(root);
                RefreshExtensionConflicts(root);
            }

            ScanStatus = token.IsCancellationRequested
                ? "\u05d4\u05e1\u05e8\u05d9\u05e7\u05d4 \u05d1\u05d5\u05d8\u05dc\u05d4" // "Scan cancelled"
                : $"\u05d4\u05e1\u05e8\u05d9\u05e7\u05d4 \u05d4\u05e1\u05ea\u05d9\u05d9\u05de\u05d4 \u2014 {scannedCount} \u05e7\u05d1\u05e6\u05d9\u05dd"; // "Scan finished — N files"
        }
        finally
        {
            IsScanning = false;
            _activeHub.NotifyAvailabilityChanged();
        }
    }

    private async Task StartWatchingAsync(int projectId, CancellationToken token)
    {
        if (_watcher is null)
            return;

        var paths = EnumerateFolders(RootFolders)
            .Select(f => f.FullPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            return;

        _watcher.Watch(paths, () =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void Rescan() => _ = RescanAsync();
            if (dispatcher is null || dispatcher.CheckAccess())
                Rescan();
            else
                dispatcher.BeginInvoke(Rescan);
        });
    }

    private async Task ResolveFolderPathsAsync(int projectId, CancellationToken token)
    {
        if (_folderPathResolver is null)
            return;

        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (token.IsCancellationRequested)
                break;
            try
            {
                var path = await _folderPathResolver
                    .ResolveFileServerFolderPathAsync(projectId, folder.FolderId, token)
                    .ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(path))
                    folder.FullPath = path;
            }
            catch
            {
                // Path resolution failures leave FullPath null; open/copy commands surface that.
            }
        }
    }

    private static IEnumerable<ProjectFolderNodeVm> EnumerateFolders(IEnumerable<ProjectWorkNodeVm> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is ProjectFolderNodeVm folder)
            {
                yield return folder;
                foreach (var child in EnumerateFolders(folder.Children))
                    yield return child;
            }
        }
    }

    private void RegisterProviders()
    {
        _activeHub.RegisterProvider(this);
        _openHub.RegisterProvider(this);
        _activeHub.NotifyAvailabilityChanged();
        OnPropertyChanged(nameof(IsAvailable));
    }

    private ProjectFolderNodeVm BuildFolder(ProjectFolderDto dto)
    {
        var node = new ProjectFolderNodeVm
        {
            FolderId = dto.FolderId,
            Title = dto.Name,
        };
        WireFolderCommands(node);
        _foldersById[dto.FolderId] = node;

        foreach (var childDto in dto.Children)
            node.Children.Add(BuildFolder(childDto));

        // File nodes (DB definitions) are added under the folder after subfolders.
        AddFileDefNodes(node, dto);

        _scanTargets.Add((node, dto));
        return node;
    }

    private void AddFileDefNodes(ProjectFolderNodeVm node, ProjectFolderDto dto)
    {
        foreach (var fileDto in dto.Files)
        {
            var fileNode = new ProjectFileNodeVm
            {
                FileId = fileDto.FileId,
                Title = fileDto.BaseName,
                StorageDestination = fileDto.StorageDestination,
                ProjectType = fileDto.ProjectType,
                Number = fileDto.Number,
                ParentFolderId = node.FolderId,
                IsRequired = fileDto.IsRequired,
                Code = fileDto.Code,
            };
            fileNode.RefreshRequiredMissing();
            WireFileCommands(fileNode);
            node.Children.Add(fileNode);
        }
    }

    private static void ClearFileNodes(ProjectFolderNodeVm node)
    {
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            if (node.Children[i] is ProjectFileNodeVm)
                node.Children.RemoveAt(i);
        }
        node.HasFiles = false;
        node.HasDefinedFiles = false;
        node.HasRequiredMissing = false;
    }

    private async Task<int> ScanFolderAsync(
        int projectId,
        ProjectFolderNodeVm node,
        ProjectFolderDto dto,
        CancellationToken token)
    {
        var destinations = new HashSet<FileStorageDestination> { FileStorageDestination.FileServer };
        foreach (var f in dto.Files)
            destinations.Add(f.StorageDestination);

        // Match maps keyed by (projectType, number) for filed placement.
        var defByKey = new Dictionary<(int, int), ProjectFileDefinitionDto>();
        foreach (var f in dto.Files)
        {
            if (f.ProjectType is { } t && f.Number is { } n)
                defByKey[(t, n)] = f;
        }

        var count = 0;
        await foreach (var sf in _index.ScanFolderAsync(projectId, node.FolderId, destinations, token).ConfigureAwait(true))
        {
            if (token.IsCancellationRequested)
                break;
            IntegrateScannedFile(node, sf, defByKey);
            count++;
        }

        return count;
    }

    private void IntegrateScannedFile(
        ProjectFolderNodeVm folder,
        ScannedFile sf,
        IReadOnlyDictionary<(int, int), ProjectFileDefinitionDto> defByKey)
    {
        void Apply()
        {
            ProjectFileNodeVm fileNode;
            string alternativeName;

            if (sf.Parsed is { } parsed && defByKey.TryGetValue((parsed.ProjectType, parsed.Number), out var def))
            {
                fileNode = FindOrReuseFiledNode(folder, def);
                alternativeName = string.IsNullOrEmpty(parsed.Alternative) ? "1" : parsed.Alternative;
            }
            else
            {
                fileNode = GetOrCreateUnfiledBucket(folder);
                alternativeName = sf.FileName;
            }

            var alt = fileNode.Children.OfType<AlternativeNodeVm>()
                .FirstOrDefault(a => string.Equals(a.AlternativeName, alternativeName, StringComparison.Ordinal));
            if (alt is null)
            {
                alt = new AlternativeNodeVm { AlternativeName = alternativeName, Title = alternativeName };
                if (!fileNode.IsUnfiled && fileNode.FileId is not null)
                {
                    var altName = alternativeName;
                    alt.AddVersionCommand = new AsyncRelayCommand(() => PickAndAddVersionAsync(fileNode, altName));
                }
                fileNode.Children.Add(alt);
            }

            var versionNumber = sf.Parsed?.Version ?? (alt.Children.Count + 1);
            var version = new VersionNodeVm
            {
                VersionNumber = versionNumber,
                Title = sf.FileName,
                FullPath = sf.Source == FileStorageDestination.FileServer ? sf.NativeId : null,
                AccItemId = sf.Source == FileStorageDestination.Acc ? sf.NativeId : null,
                AccViewerUrl = sf.AccViewerUrl,
                AccProjectId = sf.AccProjectId,
                DriveFileId = sf.Source == FileStorageDestination.GoogleDrive ? sf.NativeId : null,
                StorageDestination = sf.Source,
                ParentFolderId = folder.FolderId,
                Details = FormatDetails(sf),
            };
            version.OpenCommand = new AsyncRelayCommand(() => OpenVersionAsync(version));
            if (version.IsAcc)
            {
                version.AccTabOpenChanged = OnVersionAccTabToggled;
                if (_accViewerHost is not null && !string.IsNullOrEmpty(version.AccItemId))
                    version.SetAccTabOpenSilent(_accViewerHost.IsTabOpen(version.AccItemId));
            }
            WireVersionCommands(version);
            alt.Children.Add(version);
            folder.HasFiles = true;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            dispatcher.Invoke(Apply);
    }

    private ProjectFileNodeVm FindOrReuseFiledNode(ProjectFolderNodeVm folder, ProjectFileDefinitionDto def)
    {
        var existing = folder.Children.OfType<ProjectFileNodeVm>()
            .FirstOrDefault(n => n.FileId == def.FileId);
        if (existing is not null)
            return existing;

        var created = new ProjectFileNodeVm
        {
            FileId = def.FileId,
            Title = def.BaseName,
            StorageDestination = def.StorageDestination,
            ProjectType = def.ProjectType,
            Number = def.Number,
            ParentFolderId = folder.FolderId,
            IsRequired = def.IsRequired,
            Code = def.Code,
        };
        created.RefreshRequiredMissing();
        WireFileCommands(created);
        folder.Children.Add(created);
        return created;
    }

    private static ProjectFileNodeVm GetOrCreateUnfiledBucket(ProjectFolderNodeVm folder)
    {
        var bucket = folder.Children.OfType<ProjectFileNodeVm>().FirstOrDefault(n => n.IsUnfiled);
        if (bucket is not null)
            return bucket;

        bucket = new ProjectFileNodeVm { Title = UnfiledBucketTitle, IsUnfiled = true };
        folder.Children.Add(bucket);
        return bucket;
    }

    private static bool RefreshHasFiles(ProjectFolderNodeVm folder)
    {
        var hasPhysical = false;
        var hasDefined = false;
        var hasRequiredMissing = false;

        foreach (var file in folder.Children.OfType<ProjectFileNodeVm>())
        {
            var physical = file.Children.OfType<AlternativeNodeVm>().Any(a => a.Children.Count > 0);
            file.HasPhysicalVersions = physical;
            file.RefreshRequiredMissing();

            if (physical)
                hasPhysical = true;
            if (!file.IsUnfiled && file.FileId is not null)
                hasDefined = true;
            if (file.IsRequiredMissing)
                hasRequiredMissing = true;
        }

        foreach (var child in folder.Children.OfType<ProjectFolderNodeVm>())
        {
            hasPhysical |= RefreshHasFiles(child);
            hasDefined |= child.HasDefinedFiles;
            hasRequiredMissing |= child.HasRequiredMissing;
        }

        folder.HasPhysicalFiles = hasPhysical;
        folder.HasDefinedFiles = hasDefined;
        folder.HasRequiredMissing = hasRequiredMissing;
        return hasPhysical;
    }

    /// <summary>
    /// Returns <see langword="false"/> when any required catalog file under the loaded tree
    /// is missing a physical version (or when no required slot is present at all for the gate caller).
    /// </summary>
    public bool HasAllRequiredPhysicalFiles()
    {
        var required = EnumerateFileNodes(RootFolders)
            .Where(f => f.IsRequired && !f.IsUnfiled)
            .ToList();
        return required.Count > 0 && required.All(f => f.HasPhysicalVersions);
    }

    /// <summary>True when at least one required catalog file is missing a physical version.</summary>
    public bool HasRequiredFilesMissing()
    {
        return EnumerateFileNodes(RootFolders).Any(f => f.IsRequiredMissing);
    }

    private static string FormatDetails(ScannedFile sf)
    {
        var size = sf.SizeBytes > 0 ? $"{sf.SizeBytes / 1024.0:0.#} KB" : null;
        var date = sf.LastModified?.ToString("dd/MM/yyyy HH:mm");
        return string.Join("  ·  ", new[] { size, date }.Where(s => !string.IsNullOrEmpty(s)));
    }

    private async Task OpenVersionAsync(VersionNodeVm version)
    {
        var request = version.FullPath is { } path
            ? new FileOpenRequest(FullPath: path)
            : new FileOpenRequest();
        _ = await OpenVersionCoreAsync(version, request).ConfigureAwait(true);
    }

    // ── IFileOpenService ─────────────────────────────────────────────────────
    /// <inheritdoc />
    public Task<FileOpenResult> OpenAsync(FileOpenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = ResolveVersion(request);
        if (version is null)
            return Task.FromResult(new FileOpenResult(FileOpenOutcome.NotFound));
        return OpenVersionCoreAsync(version, request);
    }

    private async Task<FileOpenResult> OpenVersionCoreAsync(VersionNodeVm version, FileOpenRequest request)
    {
        try
        {
            if (version.IsAcc)
            {
                if (string.IsNullOrEmpty(version.AccViewerUrl) && string.IsNullOrEmpty(version.AccItemId))
                    return new FileOpenResult(FileOpenOutcome.NotFound);

                var viewerUrl = ResolveAccViewerUrl(version);
                if (string.IsNullOrEmpty(viewerUrl))
                    return new FileOpenResult(FileOpenOutcome.NotFound);

                // #region agent log
                SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
                    "ProjectWork.AccOpen",
                    $"title='{version.Title}' itemIdLen={(version.AccItemId?.Length ?? 0)} hasEntityId={viewerUrl.Contains("entityId=", StringComparison.Ordinal)} hostAvailable={_accViewerHost?.IsAvailable == true}");
                SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
                    "ProjectWork.AccTabUi",
                    "open-path sets IsAccTabOpen on VersionNodeVm");
                // #endregion

                // Prefer the embedded ACC viewer (host-seam); fall back to an external browser tab.
                if (_accViewerHost is { IsAvailable: true })
                {
                    var tabKey = string.IsNullOrEmpty(version.AccItemId) ? viewerUrl : version.AccItemId!;
                    var opened = await _accViewerHost
                        .OpenOrActivateTabAsync(new AccViewerTabRequest(tabKey, version.Title, viewerUrl))
                        .ConfigureAwait(true);
                    if (opened)
                    {
                        version.SetAccTabOpenSilent(true);
                        return new FileOpenResult(FileOpenOutcome.OpenedInAcc, AccViewerUrl: viewerUrl);
                    }
                }

                Process.Start(new ProcessStartInfo(viewerUrl) { UseShellExecute = true });
                version.SetAccTabOpenSilent(true);
                return new FileOpenResult(FileOpenOutcome.OpenedInAcc, AccViewerUrl: viewerUrl);
            }

            if (version.IsDrive)
            {
                if (string.IsNullOrEmpty(version.DriveFileId))
                    return new FileOpenResult(FileOpenOutcome.NotFound);

                var store = _index.GetStore(FileStorageDestination.GoogleDrive);
                if (store is null)
                    return new FileOpenResult(FileOpenOutcome.Failed, Error: "No Google Drive store is registered.");

                var descriptor = new ScannedFile(
                    FileStorageDestination.GoogleDrive,
                    version.Title,
                    version.DriveFileId,
                    0,
                    null,
                    ProjectFileNameParser.TryParse(version.Title));
                var localPath = await store.DownloadToLocalAsync(descriptor).ConfigureAwait(true);
                Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
                return new FileOpenResult(FileOpenOutcome.OpenedLocally, FullPath: localPath);
            }

            if (string.IsNullOrEmpty(version.FullPath) || !File.Exists(version.FullPath))
                return new FileOpenResult(FileOpenOutcome.NotFound);

            Process.Start(new ProcessStartInfo(version.FullPath) { UseShellExecute = true });
            return new FileOpenResult(FileOpenOutcome.OpenedLocally, FullPath: version.FullPath);
        }
        catch (GoogleConsentRequiredException ex)
        {
            return new FileOpenResult(FileOpenOutcome.Failed, Error: ex.Message);
        }
        catch (Exception ex)
        {
            return new FileOpenResult(FileOpenOutcome.Failed, Error: ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<bool> SetOpenPreferenceAsync(int fileId, string? openWith, CancellationToken cancellationToken = default)
        => Task.FromResult(false); // Open-With preferences arrive in Phase 2.

    /// <inheritdoc />
    public Task<bool> SetOpenPreferenceForPathAsync(string fullPath, string? openWith, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    private VersionNodeVm? ResolveVersion(FileOpenRequest request)
    {
        var all = EnumerateVersions(RootFolders).ToList();
        if (!string.IsNullOrEmpty(request.FullPath))
            return all.FirstOrDefault(v => string.Equals(v.FullPath, request.FullPath, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private static IEnumerable<VersionNodeVm> EnumerateVersions(IEnumerable<ProjectWorkNodeVm> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is VersionNodeVm v)
                yield return v;
            foreach (var child in EnumerateVersions(node.Children))
                yield return child;
        }
    }

    // ── IActiveFileQueryService reads (best-effort snapshots) ────────────────
    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId)
        => _foldersById.TryGetValue(folderId, out var folder)
            ? folder.Children.OfType<ProjectFileNodeVm>().Select(ToActiveFileInfo).ToList()
            : Array.Empty<ActiveFileInfo>();

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(string folderFullPath)
        => Array.Empty<ActiveFileInfo>();

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId, bool recursive)
    {
        if (!recursive)
            return GetActiveFilesInFolder(folderId);
        if (!_foldersById.TryGetValue(folderId, out var folder))
            return Array.Empty<ActiveFileInfo>();

        var list = new List<ActiveFileInfo>();
        void Walk(ProjectFolderNodeVm f)
        {
            list.AddRange(f.Children.OfType<ProjectFileNodeVm>().Select(ToActiveFileInfo));
            foreach (var child in f.Children.OfType<ProjectFolderNodeVm>())
                Walk(child);
        }
        Walk(folder);
        return list;
    }

    /// <inheritdoc />
    public ActiveFileInfo? FindActiveFileByName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        return GetAllActiveFiles()
            .FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(f.FileName + f.Extension, fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetAllActiveFiles()
        => RootFolders.OfType<ProjectFolderNodeVm>()
            .SelectMany(f => GetActiveFilesInFolder(f.FolderId, recursive: true))
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<ActiveFolderInfo> GetActiveFolderTree()
        => RootFolders.OfType<ProjectFolderNodeVm>().Select(ToActiveFolderInfo).ToList();

    private ActiveFolderInfo ToActiveFolderInfo(ProjectFolderNodeVm folder) =>
        new(
            folder.FolderId,
            folder.Title,
            folder.FullPath,
            folder.Children.OfType<ProjectFileNodeVm>().Select(ToActiveFileInfo).ToList(),
            folder.Children.OfType<ProjectFolderNodeVm>().Select(ToActiveFolderInfo).ToList());

    private ActiveFileInfo ToActiveFileInfo(ProjectFileNodeVm file)
    {
        var alternatives = file.Children.OfType<AlternativeNodeVm>()
            .Select(a => new ActiveAlternativeInfo(
                a.AlternativeName,
                a.Children.OfType<VersionNodeVm>()
                    .Select(v => new ActiveVersionInfo(v.VersionNumber, v.Title, v.FullPath, null, null, v.AccItemId, v.AccViewerUrl, v.DriveFileId))
                    .ToList()))
            .ToList();

        // Prefer a real extension from a version title when the file definition title has none.
        var sampleName = file.Children.OfType<AlternativeNodeVm>()
            .SelectMany(a => a.Children.OfType<VersionNodeVm>())
            .Select(v => v.Title)
            .FirstOrDefault(t => !string.IsNullOrEmpty(Path.GetExtension(t)));
        var extension = Path.GetExtension(sampleName ?? file.Title);

        return new ActiveFileInfo(
            FileId: file.FileId,
            FileName: file.Title,
            Extension: extension,
            ProjectNumber: _currentProjectNumber ?? 0,
            FolderId: file.ParentFolderId,
            StorageDestination: file.StorageLabel,
            Alternatives: alternatives);
    }

    // ── Write pipeline (Phase 4, ACC gated) ──────────────────────────────────

    /// <summary>
    /// Adds a new version to <paramref name="file"/> under <paramref name="alternativeName"/> from a
    /// local source file: builds the canonical name, guards against extension conflicts, marks the
    /// upload in-flight, places it through the destination store, then rescans. ACC placements are
    /// gated by the ACC-write policy.
    /// </summary>
    public async Task<FileWriteOutcome> AddVersionAsync(
        ProjectFileNodeVm file,
        string alternativeName,
        string sourceLocalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.FileId is null || file.ProjectType is null || file.Number is null || _currentProjectNumber is null)
            return new FileWriteOutcome(FileWriteStatus.Failed, "This file has no canonical identity, so a version cannot be placed.");
        if (string.IsNullOrWhiteSpace(sourceLocalPath))
            return new FileWriteOutcome(FileWriteStatus.Failed, "No source file was provided.");

        var alt = string.IsNullOrWhiteSpace(alternativeName) ? "1" : alternativeName.Trim();
        var nextVersion = NextVersionFor(file, alt);
        var targetName = ProjectFileNameBuilder.Build(
            _currentProjectNumber.Value,
            file.ProjectType.Value,
            file.Number.Value,
            alt,
            nextVersion,
            file.Title,
            Path.GetFileName(sourceLocalPath));

        return await PlaceAsync(file.StorageDestination, file.ParentFolderId, targetName, sourceLocalPath, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Replaces a version's content from a local source file (drag-drop replace), keeping the
    /// canonical name. ACC replace is not supported in this phase. Drive replace trashes the
    /// existing item first (Drive has no in-place overwrite in this store).
    /// </summary>
    public async Task<FileWriteOutcome> ReplaceVersionAsync(
        VersionNodeVm version,
        string sourceLocalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.IsAcc)
            return new FileWriteOutcome(FileWriteStatus.NotSupported, "Replacing an ACC version from ProjectWork is not enabled in this phase.");
        if (string.IsNullOrWhiteSpace(sourceLocalPath))
            return new FileWriteOutcome(FileWriteStatus.Failed, "No source file was provided.");

        if (version.IsDrive)
        {
            var deleted = await DeleteVersionAsync(version, cancellationToken).ConfigureAwait(true);
            if (deleted.Status != FileWriteStatus.Success)
                return deleted;
        }

        // FileServer overwrites in place; Drive uploads after trash above.
        return await PlaceAsync(version.StorageDestination, version.ParentFolderId, version.Title, sourceLocalPath, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>Renames a FileServer/Drive version's canonical base name (ACC rename is unsupported).</summary>
    public async Task<FileWriteOutcome> RenameVersionAsync(
        VersionNodeVm version,
        string newBaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.IsAcc)
            return new FileWriteOutcome(FileWriteStatus.NotSupported, "Renaming an ACC item is not supported.");

        var nativeId = version.IsDrive ? version.DriveFileId : version.FullPath;
        if (string.IsNullOrWhiteSpace(nativeId))
            return new FileWriteOutcome(FileWriteStatus.Failed, "This version has no native id to rename.");

        var parsed = ProjectFileNameParser.TryParse(version.Title);
        var newFileName = parsed is not null
            ? ProjectFileNameBuilder.Build(parsed.ProjectNumber, parsed.ProjectType, parsed.Number, parsed.Alternative, parsed.Version, newBaseName, version.Title)
            : newBaseName;

        var store = _index.GetStore(version.StorageDestination);
        if (store is null)
            return new FileWriteOutcome(FileWriteStatus.NoStore, $"No store is registered for {version.StorageDestination}.");

        try
        {
            var descriptor = new ScannedFile(version.StorageDestination, version.Title, nativeId, 0, null, parsed);
            await store.RenameAsync(descriptor, newFileName, cancellationToken).ConfigureAwait(true);
            await RescanAsync(cancellationToken).ConfigureAwait(true);
            return new FileWriteOutcome(FileWriteStatus.Success);
        }
        catch (GoogleConsentRequiredException ex)
        {
            return new FileWriteOutcome(FileWriteStatus.NotSupported, ex.Message);
        }
        catch (Exception ex)
        {
            return new FileWriteOutcome(FileWriteStatus.Failed, ex.Message);
        }
    }

    /// <summary>Deletes a version from its destination (FileServer delete / ACC soft-hide, gated).</summary>
    public async Task<FileWriteOutcome> DeleteVersionAsync(
        VersionNodeVm version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        var store = _index.GetStore(version.StorageDestination);
        if (store is null)
            return new FileWriteOutcome(FileWriteStatus.NoStore, "No store is registered for this destination.");
        if (version.IsAcc && _writePolicy?.IsWriteEnabled != true)
            return new FileWriteOutcome(FileWriteStatus.Gated, GatedMessage);

        try
        {
            var nativeId = version.IsAcc
                ? version.AccItemId ?? string.Empty
                : version.IsDrive
                    ? version.DriveFileId ?? string.Empty
                    : version.FullPath ?? string.Empty;
            var descriptor = new ScannedFile(
                version.StorageDestination,
                version.Title,
                nativeId,
                0,
                null,
                ProjectFileNameParser.TryParse(version.Title),
                AccProjectId: version.AccProjectId);
            await store.DeleteAsync(descriptor, cancellationToken).ConfigureAwait(true);
            await RescanAsync(cancellationToken).ConfigureAwait(true);
            return new FileWriteOutcome(FileWriteStatus.Success);
        }
        catch (AccWriteGatedException)
        {
            return new FileWriteOutcome(FileWriteStatus.Gated, GatedMessage);
        }
        catch (GoogleConsentRequiredException ex)
        {
            return new FileWriteOutcome(FileWriteStatus.NotSupported, ex.Message);
        }
        catch (Exception ex)
        {
            return new FileWriteOutcome(FileWriteStatus.Failed, ex.Message);
        }
    }

    private async Task<FileWriteOutcome> PlaceAsync(
        FileStorageDestination destination,
        int folderId,
        string targetFileName,
        string sourceLocalPath,
        CancellationToken cancellationToken)
    {
        var store = _index.GetStore(destination);
        if (store is null)
            return new FileWriteOutcome(FileWriteStatus.NoStore, $"No store is registered for {destination}.");
        if (destination == FileStorageDestination.Acc && _writePolicy?.IsWriteEnabled != true)
            return new FileWriteOutcome(FileWriteStatus.Gated, GatedMessage);

        try
        {
            var handle = await store.ResolveFolderHandleAsync(_currentProjectId, folderId, cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrEmpty(handle))
                return new FileWriteOutcome(FileWriteStatus.Failed, "The target folder could not be resolved in the destination store.");

            // Extension-conflict guard: block a same-base-name/different-extension placement.
            var existing = new List<string>();
            await foreach (var sf in store.ListFilesAsync(handle, cancellationToken).ConfigureAwait(true))
                existing.Add(sf.FileName);

            var conflict = ProjectFileExtensionConflict.FindConflict(targetFileName, existing);
            if (conflict is not null)
                return new FileWriteOutcome(FileWriteStatus.ExtensionConflict,
                    $"A file named '{conflict}' already exists with a different extension. Align the file type before placing '{targetFileName}'.");

            _index.MarkInFlight(_currentProjectId, targetFileName, destination);
            try
            {
                await store.UploadAsync(handle, sourceLocalPath, targetFileName, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                _index.ClearInFlight(_currentProjectId, targetFileName, destination);
            }

            await RescanAsync(cancellationToken).ConfigureAwait(true);
            return new FileWriteOutcome(FileWriteStatus.Success);
        }
        catch (AccWriteGatedException)
        {
            return new FileWriteOutcome(FileWriteStatus.Gated, GatedMessage);
        }
        catch (GoogleConsentRequiredException ex)
        {
            return new FileWriteOutcome(FileWriteStatus.NotSupported, ex.Message);
        }
        catch (FileStoreConflictException ex)
        {
            return new FileWriteOutcome(FileWriteStatus.Failed, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return new FileWriteOutcome(FileWriteStatus.NotSupported, ex.Message);
        }
        catch (Exception ex)
        {
            return new FileWriteOutcome(FileWriteStatus.Failed, ex.Message);
        }
    }

    private int NextVersionFor(ProjectFileNodeVm file, string alternativeName)
    {
        var alt = file.Children.OfType<AlternativeNodeVm>()
            .FirstOrDefault(a => string.Equals(a.AlternativeName, alternativeName, StringComparison.Ordinal));
        if (alt is null)
            return 1;
        var max = alt.Children.OfType<VersionNodeVm>().Select(v => v.VersionNumber).DefaultIfEmpty(0).Max();
        return max + 1;
    }

    private const string GatedMessage =
        "\u05DB\u05EA\u05D9\u05D1\u05D4 \u05DC-ACC \u05D7\u05E1\u05D5\u05DE\u05D4 \u05DB\u05E8\u05D2\u05E2 (\u05E9\u05E2\u05E8 \u05DB\u05EA\u05D9\u05D1\u05EA ACC)."; // "Writing to ACC is currently blocked (ACC-write gate)."

    private void WireFileCommands(ProjectFileNodeVm file)
    {
        if (file.IsUnfiled || file.FileId is null)
            return;
        file.AddVersionCommand = new AsyncRelayCommand(() => PickAndAddVersionAsync(file, alternativeName: null));
    }

    private void WireFolderCommands(ProjectFolderNodeVm folder)
    {
        folder.OpenFolderCommand = new AsyncRelayCommand(() => OpenFolderInExplorerAsync(folder));
        folder.CreateFolderCommand = _folderWrite is null
            ? null
            : new AsyncRelayCommand(() => CreateChildFolderAsync(folder));
        folder.CopyPathCommand = new RelayCommand(_ => CopyTextToClipboard(folder.FullPath, requireExistingDirectory: true));
        folder.CopyProjectNameCommand = new RelayCommand(_ => CopyProjectNameToClipboard());
    }

    private async Task CreateChildFolderAsync(ProjectFolderNodeVm parent)
    {
        if (_folderWrite is null || _currentProjectId <= 0)
            return;

        System.Windows.Window? owner = null;
        if (System.Windows.Application.Current?.Windows is { Count: > 0 } windows)
        {
            foreach (System.Windows.Window w in windows)
            {
                if (w.IsActive)
                {
                    owner = w;
                    break;
                }
            }

            owner ??= System.Windows.Application.Current.MainWindow;
        }

        var name = StringPromptDialog.Prompt(owner, "יצירת תיקייה", "שם התיקייה החדשה:");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var result = await _folderWrite
                .CreateChildFolderAsync(parent.FolderId, name, _currentProjectId, CancellationToken.None)
                .ConfigureAwait(true);
            if (!result.Success)
            {
                ScanStatus = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "יצירת תיקייה נכשלה."
                    : result.ErrorMessage!;
                return;
            }

            ScanStatus = $"נוצרה תיקייה: {name}";
            await LoadProjectAsync(_currentProjectId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ScanStatus = $"יצירת תיקייה נכשלה: {ex.Message}";
        }
    }

    private void WireVersionCommands(VersionNodeVm version)
    {
        version.ReplaceCommand = new AsyncRelayCommand(() => PickAndReplaceAsync(version));
        version.DeleteCommand = new AsyncRelayCommand(() => ConfirmAndDeleteAsync(version));
        version.CopyPathCommand = new RelayCommand(_ => CopyVersionPathToClipboard(version));
    }

    private async Task OpenFolderInExplorerAsync(ProjectFolderNodeVm folder)
    {
        var path = folder.FullPath;
        if (string.IsNullOrWhiteSpace(path) && _folderPathResolver is not null && _currentProjectId > 0)
        {
            path = await _folderPathResolver
                .ResolveFileServerFolderPathAsync(_currentProjectId, folder.FolderId)
                .ConfigureAwait(true);
            folder.FullPath = path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            ScanStatus = "לא ניתן לפתוח תיקייה — הנתיב לא זוהה.";
            return;
        }

        if (!Directory.Exists(path))
        {
            ScanStatus = $"התיקייה לא קיימת בדיסק: {path}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            ScanStatus = $"פתיחת תיקייה נכשלה: {ex.Message}";
        }
    }

    private void CopyProjectNameToClipboard()
    {
        var text = !string.IsNullOrWhiteSpace(_currentProjectNameAndNumber)
            ? _currentProjectNameAndNumber.Trim()
            : _currentProjectNumber is { } n && n > 0
                ? $"({n.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
                : null;
        CopyTextToClipboard(text);
    }

    private void CopyVersionPathToClipboard(VersionNodeVm version)
    {
        if (version.IsAcc && !string.IsNullOrWhiteSpace(version.AccViewerUrl))
        {
            CopyTextToClipboard(version.AccViewerUrl);
            return;
        }

        CopyTextToClipboard(version.FullPath, requireExistingFile: true);
    }

    private void CopyTextToClipboard(
        string? text,
        bool requireExistingDirectory = false,
        bool requireExistingFile = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ScanStatus = "אין ערך להעתקה ללוח.";
            return;
        }

        if (requireExistingDirectory && !Directory.Exists(text))
        {
            ScanStatus = $"התיקייה לא קיימת בדיסק: {text}";
            return;
        }

        if (requireExistingFile && !File.Exists(text))
        {
            ScanStatus = $"הקובץ לא קיים בדיסק: {text}";
            return;
        }

        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                System.Windows.Clipboard.SetText(text);
            else
                dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
            ScanStatus = "הועתק ללוח.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"העתקה ללוח נכשלה: {ex.Message}";
        }
    }

    private async Task PickAndAddVersionAsync(ProjectFileNodeVm file, string? alternativeName)
    {
        var source = PickFile();
        if (source is null)
            return;

        var altName = alternativeName ?? PromptAlternativeName(file);
        if (string.IsNullOrWhiteSpace(altName))
            return;

        var outcome = await AddVersionAsync(file, altName!, source).ConfigureAwait(true);
        ReportOutcome(outcome, file.Title);
    }

    private async Task PickAndReplaceAsync(VersionNodeVm version)
    {
        var source = PickFile();
        if (source is null)
            return;
        var outcome = await ReplaceVersionAsync(version, source).ConfigureAwait(true);
        ReportOutcome(outcome, version.Title);
    }

    private async Task ConfirmAndDeleteAsync(VersionNodeVm version)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"\u05DC\u05DE\u05D7\u05D5\u05E7 \u05D0\u05EA '{version.Title}'?",
            "\u05DE\u05D7\u05D9\u05E7\u05EA \u05D2\u05E8\u05E1\u05D4",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        var outcome = await DeleteVersionAsync(version).ConfigureAwait(true);
        ReportOutcome(outcome, version.Title);
    }

    /// <summary>Places a dropped OS file onto a tree node (drag-drop entry point from the view).</summary>
    public async Task<FileWriteOutcome> HandleFileDropAsync(ProjectWorkNodeVm? targetNode, string sourceLocalPath, CancellationToken cancellationToken = default)
    {
        switch (targetNode)
        {
            case VersionNodeVm version:
                return await ReplaceVersionAsync(version, sourceLocalPath, cancellationToken).ConfigureAwait(true);
            case AlternativeNodeVm alt when FindOwningFile(alt) is { } owningFile:
                return await AddVersionAsync(owningFile, alt.AlternativeName, sourceLocalPath, cancellationToken).ConfigureAwait(true);
            case ProjectFileNodeVm { IsUnfiled: false, FileId: not null } fileNode:
            {
                var altName = PromptAlternativeName(fileNode);
                return string.IsNullOrWhiteSpace(altName)
                    ? new FileWriteOutcome(FileWriteStatus.Failed, "No alternative name was provided.")
                    : await AddVersionAsync(fileNode, altName!, sourceLocalPath, cancellationToken).ConfigureAwait(true);
            }
            default:
                return new FileWriteOutcome(FileWriteStatus.Failed, "The drop target is not a writable file, alternative or version.");
        }
    }

    private ProjectFileNodeVm? FindOwningFile(AlternativeNodeVm alt)
        => EnumerateFileNodes(RootFolders).FirstOrDefault(f => f.Children.Contains(alt));

    private static IEnumerable<ProjectFileNodeVm> EnumerateFileNodes(IEnumerable<ProjectWorkNodeVm> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is ProjectFileNodeVm f)
                yield return f;
            foreach (var child in EnumerateFileNodes(node.Children))
                yield return child;
        }
    }

    private static string? PickFile()
    {
        var dialog = new OpenFileDialog { Multiselect = false, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? PromptAlternativeName(ProjectFileNodeVm file)
    {
        // Default to the next free numeric alternative label; the drop/menu flow keeps it simple.
        var used = file.Children.OfType<AlternativeNodeVm>().Select(a => a.AlternativeName).ToHashSet(StringComparer.Ordinal);
        for (var i = 1; i <= 99; i++)
        {
            var candidate = i.ToString();
            if (!used.Contains(candidate))
                return candidate;
        }
        return null;
    }

    private static void ReportOutcome(FileWriteOutcome outcome, string title)
    {
        if (outcome.Status == FileWriteStatus.Success)
            return;
        System.Windows.MessageBox.Show(
            outcome.Message ?? $"Operation on '{title}' did not complete.",
            "\u05E1\u05D1\u05D9\u05D1\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private void OnInFlightChanged(InFlightChange change)
    {
        void Apply()
        {
            foreach (var version in EnumerateVersions(RootFolders))
            {
                if (version.StorageDestination == change.Destination
                    && string.Equals(version.Title, change.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    version.IsInFlight = change.IsStarting;
                }
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            dispatcher.BeginInvoke(Apply);
    }

    private static void RefreshExtensionConflicts(ProjectFolderNodeVm folder)
    {
        var versions = folder.Children
            .OfType<ProjectFileNodeVm>()
            .SelectMany(f => f.Children.OfType<AlternativeNodeVm>())
            .SelectMany(a => a.Children.OfType<VersionNodeVm>())
            .ToList();

        var names = versions.Select(v => v.Title).ToList();
        foreach (var version in versions)
        {
            // A same-extension match (including the version's own name) never counts as a conflict, so
            // there is no need to exclude self — only a different-extension peer trips the flag.
            version.HasExtensionConflict = ProjectFileExtensionConflict.FindConflict(version.Title, names) is not null;
        }

        foreach (var child in folder.Children.OfType<ProjectFolderNodeVm>())
            RefreshExtensionConflicts(child);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _index.InFlightChanged -= OnInFlightChanged;
        if (_accViewerHost is not null)
            _accViewerHost.TabClosed -= OnAccTabClosed;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _watcher?.Dispose();

        // Drop provider registrations only if we are still the live provider (cached shell surface
        // must not be cleared when a floating/task surface disposes).
        _activeHub.UnregisterProvider(this);
        _openHub.UnregisterProvider(this);
    }

    private void OnAccTabClosed(string tabKey)
    {
        foreach (var version in EnumerateVersions())
        {
            var key = string.IsNullOrEmpty(version.AccItemId) ? version.AccViewerUrl : version.AccItemId;
            if (string.Equals(key, tabKey, StringComparison.Ordinal))
                version.SetAccTabOpenSilent(false);
        }
    }

    private void OnVersionAccTabToggled(VersionNodeVm version, bool isOpen)
    {
        if (!version.IsAcc)
            return;

        if (isOpen)
        {
            _ = OpenVersionAsync(version);
            return;
        }

        var tabKey = string.IsNullOrEmpty(version.AccItemId) ? version.AccViewerUrl : version.AccItemId;
        if (!string.IsNullOrEmpty(tabKey))
            _accViewerHost?.CloseTab(tabKey!);
    }

    private IEnumerable<VersionNodeVm> EnumerateVersions()
        => RootFolders.OfType<ProjectFolderNodeVm>()
            .SelectMany(EnumerateFolderVersions);

    private static IEnumerable<VersionNodeVm> EnumerateFolderVersions(ProjectFolderNodeVm folder)
    {
        foreach (var file in folder.Children.OfType<ProjectFileNodeVm>())
        {
            foreach (var alt in file.Children.OfType<AlternativeNodeVm>())
            {
                foreach (var version in alt.Children.OfType<VersionNodeVm>())
                    yield return version;
            }
        }

        foreach (var child in folder.Children.OfType<ProjectFolderNodeVm>())
        {
            foreach (var version in EnumerateFolderVersions(child))
                yield return version;
        }
    }

    private static string? ResolveAccViewerUrl(VersionNodeVm version)
    {
        var existing = version.AccViewerUrl;
        if (!string.IsNullOrEmpty(existing)
            && existing.Contains("entityId=", StringComparison.Ordinal))
            return existing;

        if (string.IsNullOrEmpty(version.AccItemId) || string.IsNullOrEmpty(version.AccProjectId))
            return existing;

        var folderId = TryExtractFolderUrn(existing);
        return AccResolvedDocsUrlBuilder.Build(version.AccProjectId, folderId ?? string.Empty, version.AccItemId);
    }

    private static string? TryExtractFolderUrn(string? viewerUrl)
    {
        if (string.IsNullOrEmpty(viewerUrl))
            return null;
        const string marker = "folderUrn=";
        var idx = viewerUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var start = idx + marker.Length;
        var end = viewerUrl.IndexOf('&', start);
        var raw = end < 0 ? viewerUrl[start..] : viewerUrl[start..end];
        return Uri.UnescapeDataString(raw);
    }
}
