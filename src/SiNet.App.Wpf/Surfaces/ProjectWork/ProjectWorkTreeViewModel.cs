using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
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
/// skeleton via <see cref="IProjectFileQueryService"/>, overlays disk-only user folders (DEV-012),
/// and lazily scans files on expand with unload on collapse (DEV-013). Also serves as the process-wide
/// <see cref="IActiveFileQueryService"/> / <see cref="IFileOpenService"/> provider while a tree is loaded.
/// </summary>
public sealed class ProjectWorkTreeViewModel : ObservableObject, IActiveFileQueryService, IFileOpenService, IDisposable
{
    private const string UnfiledBucketTitle = "\u05E7\u05D5\u05D1\u05E5 \u05E9\u05D0\u05D9\u05E0\u05D5 \u05E9\u05D9\u05D9\u05DA \u05DC\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8";
    private const int IoDegreeOfParallelism = 4;

    private readonly IProjectFileQueryService _query;
    private readonly IFileIndexService _index;
    private readonly IActiveFileQueryHub _activeHub;
    private readonly IFileOpenHub _openHub;
    private readonly IAccViewerHost? _accViewerHost;
    private readonly IFileServerWatcher? _watcher;
    private readonly IProjectFolderPathResolver? _folderPathResolver;
    private readonly IAccWritePolicy? _writePolicy;
    private readonly IProjectWorkScanExclusionPolicy? _scanExclusions;

    private readonly Dictionary<int, ProjectFolderNodeVm> _foldersById = new();
    private readonly Dictionary<int, ProjectFolderDto> _folderDtos = new();
    private readonly List<(ProjectFolderNodeVm Node, ProjectFolderDto Dto)> _scanTargets = new();
    private CancellationTokenSource? _loadCts;
    private Timer? _reconcilePollTimer;
    private int _reconcileBusy;
    private bool _reconcilePending;
    private string? _reconcilePendingPath;
    private bool _disposed;
    private bool _suppressExpandHandlers;
    private int _nextUserFolderId = -1;
    private static readonly TimeSpan ReconcilePollInterval = TimeSpan.FromSeconds(20);

    private bool _isScanning;
    private string _scanStatus = string.Empty;
    private int _currentProjectId;
    private int? _currentProjectNumber;
    private string? _currentProjectNameAndNumber;
    private IReadOnlySet<string> _activeRequiredCatalogCodes =
        new HashSet<string>(StringComparer.Ordinal);

    public ProjectWorkTreeViewModel(
        IProjectFileQueryService query,
        IFileIndexService index,
        IActiveFileQueryHub activeHub,
        IFileOpenHub openHub,
        IAccViewerHost? accViewerHost = null,
        IFileServerWatcher? watcher = null,
        IProjectFolderPathResolver? folderPathResolver = null,
        IAccWritePolicy? writePolicy = null,
        IProjectFolderWriteService? folderWrite = null,
        IProjectWorkScanExclusionPolicy? scanExclusions = null)
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
        _ = folderWrite; // DEV-012: tree create is disk-only; catalog write service unused here.
        _scanExclusions = scanExclusions;
        _index.InFlightChanged += OnInFlightChanged;
        if (_accViewerHost is not null)
            _accViewerHost.TabClosed += OnAccTabClosed;

        CollapseAllCommand = new RelayCommand(_ => CollapseAllFolders());
        DeleteStaleRecoversCommand = new AsyncRelayCommand(DeleteStaleRecoversAsync, () => _currentProjectId > 0);
    }

    /// <summary>True when ACC write operations are enabled by the ACC-write gate.</summary>
    public bool IsAccWriteEnabled => _writePolicy?.IsWriteEnabled == true;

    /// <summary>
    /// Catalog codes that paint orange when missing physical files (current-task completion gates).
    /// </summary>
    public void SetActiveRequiredCatalogCodes(IReadOnlySet<string>? codes)
    {
        _activeRequiredCatalogCodes = codes is { Count: > 0 }
            ? new HashSet<string>(codes, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
            RefreshHasFiles(root);
    }

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

    /// <summary>Collapses every folder in the tree (DEV-003 H).</summary>
    public ICommand CollapseAllCommand { get; }

    /// <summary>Deletes paired stale recover files across the loaded project (DEV-003 E).</summary>
    public ICommand DeleteStaleRecoversCommand { get; }

    // ── IActiveFileQueryService ──────────────────────────────────────────────
    /// <inheritdoc />
    public bool IsAvailable => RootFolders.Count > 0;

    /// <inheritdoc />
    public int? CurrentProjectNumber => _currentProjectNumber;

    /// <summary>
    /// Loads the tree for a project: DB skeleton + one-level disk children + probe; scans only expanded folders (DEV-013).
    /// </summary>
    public async Task LoadProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCts.Token;

        var expandedFolderIds = CaptureExpandedFolderIds();
        var expandedPaths = CaptureExpandedFolderPaths();

        _currentProjectId = projectId;
        StopReconcilePoll();
        _watcher?.StopAll();
        RootFolders.Clear();
        _foldersById.Clear();
        _folderDtos.Clear();
        _scanTargets.Clear();
        _nextUserFolderId = -1;

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

            _suppressExpandHandlers = true;
            try
            {
                foreach (var rootDto in tree.RootFolders)
                {
                    var node = BuildFolder(rootDto);
                    RootFolders.Add(node);
                }

                await ResolveFolderPathsAsync(projectId, token).ConfigureAwait(true);

                foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
                    SyncDiskChildrenOneLevel(root);

                await ProbeFoldersParallelAsync(EnumerateFolders(RootFolders).ToList(), token).ConfigureAwait(true);

                foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
                    root.IsExpanded = true;

                RestoreExpandedFolderIds(expandedFolderIds);
                RestoreExpandedFolderPaths(expandedPaths);
            }
            finally
            {
                _suppressExpandHandlers = false;
            }

            RegisterProviders();

            IsScanning = true;
            ScanStatus = "טוען תיקיות פתוחות…";
            var scannedCount = 0;
            try
            {
                foreach (var folder in EnumerateFolders(RootFolders).Where(static f => f.IsExpanded).ToList())
                {
                    if (token.IsCancellationRequested)
                        break;
                    scannedCount += await ExpandFolderAsync(folder, token).ConfigureAwait(true);
                }

                foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
                {
                    RefreshHasFiles(root);
                    RefreshExtensionConflicts(root);
                }

                ScanStatus = token.IsCancellationRequested
                    ? "הסריקה בוטלה"
                    : $"מוכן — נסרקו {scannedCount} קבצים בתיקיות הפתוחות";
            }
            finally
            {
                IsScanning = false;
                _activeHub.NotifyAvailabilityChanged();
            }

            (DeleteStaleRecoversCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

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
    /// Rescans: rediscovers one level under expanded folders, reloads file nodes only for expanded folders.
    /// </summary>
    public async Task RescanAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProjectId <= 0 || _scanTargets.Count == 0)
            return;

        var token = cancellationToken;
        var expandedPaths = CaptureExpandedFolderPaths();

        _suppressExpandHandlers = true;
        try
        {
            foreach (var folder in EnumerateFolders(RootFolders).ToList())
            {
                if (folder.LoadState == ProjectFolderLoadState.Expanded || folder.IsExpanded)
                    UnloadFolderContentsCore(folder, collapseChildren: false);
            }

            foreach (var folder in EnumerateFolders(RootFolders).Where(static f => f.IsExpanded).ToList())
                SyncDiskChildrenOneLevel(folder);

            foreach (var folder in EnumerateFolders(RootFolders))
            {
                if (!string.IsNullOrWhiteSpace(folder.FullPath) && expandedPaths.Contains(folder.FullPath!))
                    folder.IsExpanded = true;
            }

            // After restore, sync any newly expanded paths that were not synced above.
            foreach (var folder in EnumerateFolders(RootFolders).Where(static f => f.IsExpanded).ToList())
                SyncDiskChildrenOneLevel(folder);
        }
        finally
        {
            _suppressExpandHandlers = false;
        }

        await ProbeFoldersParallelAsync(EnumerateFolders(RootFolders).ToList(), token).ConfigureAwait(true);

        IsScanning = true;
        ScanStatus = "מרענן תיקיות פתוחות…";
        try
        {
            var scannedCount = 0;
            foreach (var folder in EnumerateFolders(RootFolders).Where(static f => f.IsExpanded).ToList())
            {
                if (token.IsCancellationRequested)
                    break;
                scannedCount += await ExpandFolderAsync(folder, token).ConfigureAwait(true);
            }

            foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
            {
                RefreshHasFiles(root);
                RefreshExtensionConflicts(root);
            }

            ScanStatus = $"רוענן — {scannedCount} קבצים בתיקיות הפתוחות";
        }
        finally
        {
            IsScanning = false;
            _activeHub.NotifyAvailabilityChanged();
            RefreshWatchSet();
        }
    }

    /// <summary>Expands a folder and waits until its file scan completes (tests / callers).</summary>
    public async Task ExpandAndWaitAsync(ProjectFolderNodeVm folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.LoadState == ProjectFolderLoadState.Expanded)
            return;

        _suppressExpandHandlers = true;
        try
        {
            folder.IsExpanded = true;
        }
        finally
        {
            _suppressExpandHandlers = false;
        }

        await ExpandFolderAsync(folder, cancellationToken).ConfigureAwait(true);
    }

    private Task StartWatchingAsync(int projectId, CancellationToken token)
    {
        _ = projectId;
        _ = token;
        RefreshWatchSet();
        StartReconcilePoll();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Watches FullPath of each Expanded folder only (non-recursive). Collapse stops listening.
    /// </summary>
    private void RefreshWatchSet()
    {
        if (_watcher is null || _disposed)
            return;

        var paths = CaptureExpandedFolderPaths().ToList();
        if (paths.Count == 0)
        {
            _watcher.StopAll();
            return;
        }

        _watcher.Watch(paths, OnWatchedPathChanged);
    }

    private void OnWatchedPathChanged(string? affectedPath)
    {
        if (_disposed || _currentProjectId <= 0)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Run() => _ = ReconcileExpandedAsync(affectedPath);
        if (dispatcher is null || dispatcher.CheckAccess())
            Run();
        else
            dispatcher.BeginInvoke(Run);
    }

    private void StartReconcilePoll()
    {
        StopReconcilePoll();
        if (_disposed || _currentProjectId <= 0)
            return;

        _reconcilePollTimer = new Timer(
            _ => OnWatchedPathChanged(null),
            null,
            ReconcilePollInterval,
            ReconcilePollInterval);
    }

    private void StopReconcilePoll()
    {
        _reconcilePollTimer?.Dispose();
        _reconcilePollTimer = null;
    }

    /// <summary>
    /// Background reconcile for open folders: sync disk subfolders (add/remove user) + refresh files.
    /// </summary>
    private async Task ReconcileExpandedAsync(string? affectedPath)
    {
        if (_disposed || _currentProjectId <= 0)
            return;

        if (Interlocked.CompareExchange(ref _reconcileBusy, 1, 0) != 0)
        {
            _reconcilePendingPath = affectedPath;
            _reconcilePending = true;
            return;
        }

        try
        {
            while (!_disposed)
            {
                _reconcilePending = false;
                var path = affectedPath;

                var token = _loadCts?.Token ?? CancellationToken.None;
                // Poll (null path): folders + probe only. Watcher path: also merge files in-place.
                var mergeFiles = path is not null;
                var targets = ResolveReconcileTargets(path);
                foreach (var folder in targets)
                {
                    if (token.IsCancellationRequested || _disposed)
                        break;

                    var diskDirs = await EnumerateDiskDirectoriesAsync(folder.FullPath, token).ConfigureAwait(true);
                    SyncDiskChildrenOneLevel(folder, diskDirs);

                    var childFolders = folder.Children.OfType<ProjectFolderNodeVm>().ToList();
                    if (childFolders.Count > 0)
                        await ProbeFoldersParallelAsync(childFolders, token).ConfigureAwait(true);

                    if (mergeFiles
                        && folder.IsExpanded
                        && folder.LoadState == ProjectFolderLoadState.Expanded)
                    {
                        await MergeExpandedFolderFilesAsync(folder, token).ConfigureAwait(true);
                    }

                    RefreshHasFiles(folder);
                    var root = FindRoot(folder) ?? folder;
                    RefreshHasFiles(root);
                    RefreshExtensionConflicts(root);
                }

                RefreshWatchSet();

                if (!_reconcilePending)
                    break;

                affectedPath = _reconcilePendingPath;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconcileBusy, 0);
            if (_reconcilePending && !_disposed)
            {
                _reconcilePending = false;
                var pending = _reconcilePendingPath;
                _ = ReconcileExpandedAsync(pending);
            }
        }
    }

    private List<ProjectFolderNodeVm> ResolveReconcileTargets(string? affectedPath)
    {
        var expanded = EnumerateFolders(RootFolders).Where(static f => f.IsExpanded).ToList();
        if (expanded.Count == 0)
            return [];

        var matched = FindExpandedFolderForPath(affectedPath);
        if (matched is not null)
            return [matched];

        return expanded;
    }

    private ProjectFolderNodeVm? FindExpandedFolderForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        ProjectFolderNodeVm? best = null;
        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (!folder.IsExpanded || string.IsNullOrWhiteSpace(folder.FullPath))
                continue;

            if (!IsSameOrUnderPath(folder.FullPath!, path))
                continue;

            if (best is null || folder.FullPath!.Length > best.FullPath!.Length)
                best = folder;
        }

        return best;
    }

    private static bool IsSameOrUnderPath(string folderPath, string candidatePath)
    {
        try
        {
            var folderFull = Path.GetFullPath(folderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidateFull = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(folderFull, candidateFull, StringComparison.OrdinalIgnoreCase))
                return true;

            var prefix = folderFull + Path.DirectorySeparatorChar;
            return candidateFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static Task<string[]> EnumerateDiskDirectoriesAsync(string? folderPath, CancellationToken token)
    {
        return Task.Run(
            () =>
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                    return Array.Empty<string>();

                try
                {
                    return Directory.EnumerateDirectories(folderPath).ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Array.Empty<string>();
                }
            },
            token);
    }

    private async Task ResolveFolderPathsAsync(int projectId, CancellationToken token)
    {
        if (_folderPathResolver is null)
            return;

        var folders = EnumerateFolders(RootFolders)
            .Where(f => !f.IsUserCreated && f.FolderId > 0)
            .ToList();

        await Parallel.ForEachAsync(
            folders,
            new ParallelOptions { MaxDegreeOfParallelism = IoDegreeOfParallelism, CancellationToken = token },
            async (folder, ct) =>
            {
                try
                {
                    var path = await _folderPathResolver
                        .ResolveFileServerFolderPathAsync(projectId, folder.FolderId, ct)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(path))
                        folder.FullPath = path;
                }
                catch
                {
                    // leave FullPath null
                }
            }).ConfigureAwait(true);
    }

    /// <summary>Syncs immediate disk subfolders: add missing user folders, remove deleted user folders.</summary>
    private void SyncDiskChildrenOneLevel(ProjectFolderNodeVm parent)
    {
        string[] dirs;
        if (string.IsNullOrWhiteSpace(parent.FullPath) || !Directory.Exists(parent.FullPath))
        {
            dirs = [];
        }
        else
        {
            try
            {
                dirs = Directory.EnumerateDirectories(parent.FullPath).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                dirs = [];
            }
        }

        SyncDiskChildrenOneLevel(parent, dirs);
    }

    private void SyncDiskChildrenOneLevel(ProjectFolderNodeVm parent, IReadOnlyList<string> diskDirectories)
    {
        var diskByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in diskDirectories)
        {
            string name;
            try
            {
                name = Path.GetFileName(dir);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
                continue;

            diskByName[name] = dir;
        }

        foreach (var (name, dir) in diskByName)
        {
            var existing = parent.Children
                .OfType<ProjectFolderNodeVm>()
                .FirstOrDefault(c => string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new ProjectFolderNodeVm
                {
                    FolderId = _nextUserFolderId--,
                    Title = name,
                    FullPath = dir,
                    IsUserCreated = true,
                    LoadState = ProjectFolderLoadState.Skeleton,
                };
                WireFolderCommands(existing);
                InsertFolderChild(parent, existing);
            }
            else if (existing.IsUserCreated || string.IsNullOrWhiteSpace(existing.FullPath))
            {
                existing.FullPath = dir;
            }
        }

        var staleUserFolders = parent.Children
            .OfType<ProjectFolderNodeVm>()
            .Where(c => c.IsUserCreated && !diskByName.ContainsKey(c.Title))
            .ToList();

        foreach (var stale in staleUserFolders)
        {
            UnloadFolderContentsCore(stale, collapseChildren: true);
            parent.Children.Remove(stale);
            _foldersById.Remove(stale.FolderId);
        }
    }

    private static void InsertFolderChild(ProjectFolderNodeVm parent, ProjectFolderNodeVm child)
    {
        var insertAt = parent.Children.Count;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] is ProjectFileNodeVm)
            {
                insertAt = i;
                break;
            }
        }

        parent.Children.Insert(insertAt, child);
    }

    private async Task ProbeFoldersParallelAsync(IReadOnlyList<ProjectFolderNodeVm> folders, CancellationToken token)
    {
        if (folders.Count == 0)
            return;

        await Parallel.ForEachAsync(
            folders,
            new ParallelOptions { MaxDegreeOfParallelism = IoDegreeOfParallelism, CancellationToken = token },
            (folder, _) =>
            {
                var hasPhysical = ProbeHasPhysicalFiles(folder.FullPath);
                folder.HasPhysicalFiles = hasPhysical;
                if (folder.LoadState == ProjectFolderLoadState.Skeleton)
                    folder.LoadState = ProjectFolderLoadState.Probed;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(true);
    }

    private bool ProbeHasPhysicalFiles(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        try
        {
            foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(path);
                if (ShouldSkipPathFromStaleRecoverSweep(name))
                    continue;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private async Task<int> ExpandFolderAsync(ProjectFolderNodeVm folder, CancellationToken token)
    {
        if (folder.LoadState == ProjectFolderLoadState.Expanded)
            return 0;

        SyncDiskChildrenOneLevel(folder);

        ClearFileNodes(folder);
        ProjectFolderDto? dto = null;
        if (_folderDtos.TryGetValue(folder.FolderId, out var mapped))
        {
            dto = mapped;
            AddFileDefNodes(folder, dto);
        }

        var count = 0;
        if (folder.IsUserCreated || folder.FolderId <= 0 || dto is null)
            count = await ScanFolderByPathAsync(folder, token).ConfigureAwait(true);
        else
            count = await ScanFolderAsync(_currentProjectId, folder, dto, token).ConfigureAwait(true);

        folder.LoadState = ProjectFolderLoadState.Expanded;
        folder.HasPhysicalFiles = count > 0
            || folder.Children.OfType<ProjectFolderNodeVm>().Any(c => c.HasPhysicalFiles);

        var childFolders = folder.Children.OfType<ProjectFolderNodeVm>().ToList();
        if (childFolders.Count > 0)
            await ProbeFoldersParallelAsync(childFolders, token).ConfigureAwait(true);

        return count;
    }

    /// <summary>
    /// Differential file sync for an already-expanded folder: add missing versions, drop vanished
    /// FileServer versions. Reuses existing file/alternative nodes so TreeView IsExpanded is preserved.
    /// </summary>
    internal async Task MergeExpandedFolderFilesAsync(
        ProjectFolderNodeVm folder,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var scanned = await ListFolderScannedFilesAsync(folder, token).ConfigureAwait(true);
        var presentFileServerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sf in scanned)
        {
            if (sf.Source == FileStorageDestination.FileServer && !string.IsNullOrWhiteSpace(sf.NativeId))
                presentFileServerIds.Add(sf.NativeId);
        }

        PruneMissingFileServerVersions(folder, presentFileServerIds);

        var defByKey = BuildDefByKey(folder);
        var roles = RecoverScanClassifier.Classify(
            scanned.Select(sf => new RecoverScanClassifier.FileStamp(
                sf.FileName,
                sf.SizeBytes,
                sf.LastModified)));

        var count = 0;
        foreach (var sf in scanned)
        {
            if (token.IsCancellationRequested)
                break;

            if (roles.TryGetValue(sf.FileName, out var role) && role == RecoverTreeRole.Hidden)
                continue;

            IntegrateScannedFile(folder, sf, defByKey, roles.GetValueOrDefault(sf.FileName, RecoverTreeRole.NotRecover));
            count++;
        }

        folder.LoadState = ProjectFolderLoadState.Expanded;
        folder.HasPhysicalFiles = count > 0
            || folder.Children.OfType<ProjectFileNodeVm>()
                .Any(f => f.Children.OfType<AlternativeNodeVm>().Any(a => a.Children.Count > 0))
            || folder.Children.OfType<ProjectFolderNodeVm>().Any(c => c.HasPhysicalFiles);
    }

    private async Task<List<ScannedFile>> ListFolderScannedFilesAsync(
        ProjectFolderNodeVm folder,
        CancellationToken token)
    {
        _folderDtos.TryGetValue(folder.FolderId, out var dto);

        if (folder.IsUserCreated || folder.FolderId <= 0 || dto is null)
        {
            if (string.IsNullOrWhiteSpace(folder.FullPath))
                return [];

            return await ListFileServerFilesAsync(folder.FullPath!, token).ConfigureAwait(true);
        }

        var destinations = new HashSet<FileStorageDestination> { FileStorageDestination.FileServer };
        foreach (var f in dto.Files)
            destinations.Add(f.StorageDestination);

        if (!string.IsNullOrWhiteSpace(folder.FullPath)
            && destinations.Count == 1
            && destinations.Contains(FileStorageDestination.FileServer))
        {
            return await ListFileServerFilesAsync(folder.FullPath!, token).ConfigureAwait(true);
        }

        var scanned = new List<ScannedFile>();
        await foreach (var sf in _index.ScanFolderAsync(
                           _currentProjectId, folder.FolderId, destinations, token).ConfigureAwait(true))
        {
            if (token.IsCancellationRequested)
                break;
            scanned.Add(sf);
        }

        return scanned;
    }

    private async Task<List<ScannedFile>> ListFileServerFilesAsync(string folderPath, CancellationToken token)
    {
        var store = _index.GetStore(FileStorageDestination.FileServer);
        if (store is null)
            return [];

        return await Task.Run(
            async () =>
            {
                var list = new List<ScannedFile>();
                await foreach (var sf in store.ListFilesAsync(folderPath, token).ConfigureAwait(false))
                    list.Add(sf);
                return list;
            },
            token).ConfigureAwait(true);
    }

    private Dictionary<(int, int), ProjectFileDefinitionDto> BuildDefByKey(ProjectFolderNodeVm folder)
    {
        var defByKey = new Dictionary<(int, int), ProjectFileDefinitionDto>();
        if (!_folderDtos.TryGetValue(folder.FolderId, out var dto))
            return defByKey;

        foreach (var f in dto.Files)
        {
            if (f.ProjectType is { } t && f.Number is { } n)
                defByKey[(t, n)] = f;
        }

        return defByKey;
    }

    private static void PruneMissingFileServerVersions(
        ProjectFolderNodeVm folder,
        IReadOnlySet<string> presentFileServerIds)
    {
        foreach (var file in folder.Children.OfType<ProjectFileNodeVm>().ToList())
        {
            foreach (var alt in file.Children.OfType<AlternativeNodeVm>().ToList())
            {
                foreach (var version in alt.Children.OfType<VersionNodeVm>().ToList())
                {
                    if (version.StorageDestination != FileStorageDestination.FileServer)
                        continue;
                    if (string.IsNullOrWhiteSpace(version.FullPath))
                        continue;
                    if (!presentFileServerIds.Contains(version.FullPath))
                        alt.Children.Remove(version);
                }

                if (alt.Children.Count == 0)
                    file.Children.Remove(alt);
            }

            if (file.IsUnfiled && file.Children.Count == 0)
                folder.Children.Remove(file);
        }
    }

    private void UnloadFolderContents(ProjectFolderNodeVm folder)
    {
        UnloadFolderContentsCore(folder, collapseChildren: true);
        var parent = FindParentFolder(folder);
        if (parent is not null)
            RefreshHasFiles(parent);
        else
            RefreshHasFiles(folder);
    }

    private void UnloadFolderContentsCore(ProjectFolderNodeVm folder, bool collapseChildren)
    {
        foreach (var child in folder.Children.OfType<ProjectFolderNodeVm>().ToList())
        {
            UnloadFolderContentsCore(child, collapseChildren: true);
            if (collapseChildren)
            {
                var prev = _suppressExpandHandlers;
                _suppressExpandHandlers = true;
                try
                {
                    child.IsExpanded = false;
                }
                finally
                {
                    _suppressExpandHandlers = prev;
                }
            }
        }

        ClearFileNodes(folder);
        if (_folderDtos.TryGetValue(folder.FolderId, out var dto))
            AddFileDefNodes(folder, dto);

        folder.LoadState = ProjectFolderLoadState.Probed;
        // Keep HasPhysicalFiles from probe / prior knowledge; refresh cheaply.
        if (!string.IsNullOrWhiteSpace(folder.FullPath))
            folder.HasPhysicalFiles = ProbeHasPhysicalFiles(folder.FullPath);
    }

    private async Task OnFolderExpandStateChangedAsync(ProjectFolderNodeVm folder)
    {
        if (_suppressExpandHandlers || _currentProjectId <= 0)
            return;

        if (folder.IsExpanded)
        {
            IsScanning = true;
            ScanStatus = $"טוען: {folder.Title}…";
            try
            {
                var token = _loadCts?.Token ?? CancellationToken.None;
                var count = await ExpandFolderAsync(folder, token).ConfigureAwait(true);
                RefreshHasFiles(folder);
                var root = FindRoot(folder) ?? folder;
                RefreshHasFiles(root);
                RefreshExtensionConflicts(root);
                ScanStatus = $"נטענו {count} קבצים ב־{folder.Title}";
            }
            catch (OperationCanceledException)
            {
                ScanStatus = "הסריקה בוטלה";
            }
            catch (Exception ex)
            {
                ScanStatus = $"טעינת תיקייה נכשלה: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                _activeHub.NotifyAvailabilityChanged();
                RefreshWatchSet();
            }
        }
        else
        {
            UnloadFolderContents(folder);
            ScanStatus = $"שוחרר תוכן: {folder.Title}";
            RefreshWatchSet();
        }
    }

    private ProjectFolderNodeVm? FindRoot(ProjectFolderNodeVm folder)
    {
        var current = folder;
        while (true)
        {
            var parent = FindParentFolder(current);
            if (parent is null)
                return current;
            current = parent;
        }
    }

    private async Task<int> ScanFolderByPathAsync(ProjectFolderNodeVm node, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(node.FullPath))
            return 0;

        var store = _index.GetStore(FileStorageDestination.FileServer);
        if (store is null)
            return 0;

        var defByKey = new Dictionary<(int, int), ProjectFileDefinitionDto>();
        if (_folderDtos.TryGetValue(node.FolderId, out var dto))
        {
            foreach (var f in dto.Files)
            {
                if (f.ProjectType is { } t && f.Number is { } n)
                    defByKey[(t, n)] = f;
            }
        }

        var path = node.FullPath;
        var scanned = await Task.Run(async () =>
        {
            var list = new List<ScannedFile>();
            await foreach (var sf in store.ListFilesAsync(path, token).ConfigureAwait(false))
                list.Add(sf);
            return list;
        }, token).ConfigureAwait(true);

        var roles = RecoverScanClassifier.Classify(
            scanned.Select(sf => new RecoverScanClassifier.FileStamp(
                sf.FileName,
                sf.SizeBytes,
                sf.LastModified)));

        var count = 0;
        foreach (var sf in scanned)
        {
            if (token.IsCancellationRequested)
                break;

            if (roles.TryGetValue(sf.FileName, out var role) && role == RecoverTreeRole.Hidden)
                continue;

            IntegrateScannedFile(node, sf, defByKey, roles.GetValueOrDefault(sf.FileName, RecoverTreeRole.NotRecover));
            count++;
        }

        return count;
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
            LoadState = ProjectFolderLoadState.Skeleton,
        };
        WireFolderCommands(node);
        _foldersById[dto.FolderId] = node;
        _folderDtos[dto.FolderId] = dto;

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
                TemplateLocation = fileDto.TemplateLocation,
                OutSidData = fileDto.OutSidData,
            };
            fileNode.IsActiveCompletionGate = IsActiveGateCode(fileNode.Code);
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
        var scanned = new List<ScannedFile>();
        // DEV-013: prefer already-resolved FullPath for FileServer to avoid N+1 path resolve.
        if (!string.IsNullOrWhiteSpace(node.FullPath)
            && destinations.Count == 1
            && destinations.Contains(FileStorageDestination.FileServer))
        {
            scanned = await Task.Run(async () =>
            {
                var list = new List<ScannedFile>();
                var store = _index.GetStore(FileStorageDestination.FileServer);
                if (store is null)
                    return list;
                await foreach (var sf in store.ListFilesAsync(node.FullPath!, token).ConfigureAwait(false))
                    list.Add(sf);
                return list;
            }, token).ConfigureAwait(true);
        }
        else
        {
            await foreach (var sf in _index.ScanFolderAsync(projectId, node.FolderId, destinations, token).ConfigureAwait(true))
            {
                if (token.IsCancellationRequested)
                    break;
                scanned.Add(sf);
            }
        }

        var roles = RecoverScanClassifier.Classify(
            scanned.Select(sf => new RecoverScanClassifier.FileStamp(
                sf.FileName,
                sf.SizeBytes,
                sf.LastModified)));

        foreach (var sf in scanned)
        {
            if (token.IsCancellationRequested)
                break;

            if (roles.TryGetValue(sf.FileName, out var role) && role == RecoverTreeRole.Hidden)
            {
                continue;
            }

            IntegrateScannedFile(node, sf, defByKey, roles.GetValueOrDefault(sf.FileName, RecoverTreeRole.NotRecover));
            count++;
        }

        return count;
    }

    private void IntegrateScannedFile(
        ProjectFolderNodeVm folder,
        ScannedFile sf,
        IReadOnlyDictionary<(int, int), ProjectFileDefinitionDto> defByKey,
        RecoverTreeRole recoverRole = RecoverTreeRole.NotRecover)
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

            if (FindExistingVersion(alt, sf) is not null)
            {
                folder.HasFiles = true;
                return;
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
                RecoverRole = recoverRole,
                RecoverToolTip = recoverRole switch
                {
                    RecoverTreeRole.ActionableNewer =>
                        "recover חדש יותר מה-DWG השמור — לפתוח לשחזור?",
                    RecoverTreeRole.Orphan =>
                        "אין קובץ מקור מתאים באותה תיקייה",
                    _ => null,
                },
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

    private static VersionNodeVm? FindExistingVersion(AlternativeNodeVm alt, ScannedFile sf)
    {
        foreach (var version in alt.Children.OfType<VersionNodeVm>())
        {
            if (version.StorageDestination != sf.Source)
                continue;

            var match = sf.Source switch
            {
                FileStorageDestination.FileServer =>
                    !string.IsNullOrWhiteSpace(version.FullPath)
                    && string.Equals(version.FullPath, sf.NativeId, StringComparison.OrdinalIgnoreCase),
                FileStorageDestination.Acc =>
                    !string.IsNullOrWhiteSpace(version.AccItemId)
                    && string.Equals(version.AccItemId, sf.NativeId, StringComparison.Ordinal),
                FileStorageDestination.GoogleDrive =>
                    !string.IsNullOrWhiteSpace(version.DriveFileId)
                    && string.Equals(version.DriveFileId, sf.NativeId, StringComparison.Ordinal),
                _ => false,
            };
            if (match)
                return version;
        }

        return null;
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
            TemplateLocation = def.TemplateLocation,
            OutSidData = def.OutSidData,
        };
        created.IsActiveCompletionGate = IsActiveGateCode(created.Code);
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

    private bool RefreshHasFiles(ProjectFolderNodeVm folder)
    {
        var hasPhysical = false;
        var hasDefined = false;
        var hasRequiredMissing = false;

        foreach (var file in folder.Children.OfType<ProjectFileNodeVm>())
        {
            var physical = file.Children.OfType<AlternativeNodeVm>().Any(a => a.Children.Count > 0);
            file.IsActiveCompletionGate = IsActiveGateCode(file.Code);
            file.HasPhysicalVersions = physical;
            file.RefreshRequiredMissing();

            if (physical)
                hasPhysical = true;
            if (!file.IsUnfiled && file.FileId is not null)
                hasDefined = true;
            if (file.IsRequiredMissing)
                hasRequiredMissing = true;
        }

        // DEV-013: collapsed / not-yet-scanned folders keep presence from probe, not only version nodes.
        if (folder.LoadState != ProjectFolderLoadState.Expanded && !hasPhysical)
            hasPhysical = ProbeHasPhysicalFiles(folder.FullPath);

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

    private bool IsActiveGateCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _activeRequiredCatalogCodes.Contains(code);

    /// <summary>
    /// True when every catalog slot with <paramref name="catalogCode"/> has at least one physical
    /// version, or when no such slot is present on this project tree (not applicable).
    /// </summary>
    public bool HasRequiredPhysicalFile(string catalogCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogCode);
        var matches = EnumerateFileNodes(RootFolders)
            .Where(f => !f.IsUnfiled && string.Equals(f.Code, catalogCode, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 0)
            return true;
        return matches.All(f => f.HasPhysicalVersions);
    }

    /// <summary>
    /// True when at least one required catalog file exists and every required file has a physical version.
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
        file.AddVersionFromTemplateCommand = new AsyncRelayCommand(
            () => AddVersionFromTemplateAsync(file),
            () => file.CanAddFromTemplate);
    }

    private void WireFolderCommands(ProjectFolderNodeVm folder)
    {
        folder.PropertyChanged -= OnFolderPropertyChanged;
        folder.PropertyChanged += OnFolderPropertyChanged;
        folder.OpenFolderCommand = new AsyncRelayCommand(() => OpenFolderInExplorerAsync(folder));
        folder.CreateFolderCommand = new AsyncRelayCommand(
            () => CreateChildFolderAsync(folder),
            () => !string.IsNullOrWhiteSpace(folder.FullPath));
        folder.DeleteFolderCommand = new AsyncRelayCommand(
            () => DeleteUserFolderAsync(folder),
            () => folder.CanDeleteFolder);
        folder.CopyPathCommand = new RelayCommand(_ => CopyTextToClipboard(folder.FullPath, requireExistingDirectory: true));
        folder.CopyProjectNameCommand = new RelayCommand(_ => CopyProjectNameToClipboard());
        folder.CollapseAllCommand = CollapseAllCommand;
        folder.DeleteStaleRecoversCommand = DeleteStaleRecoversCommand;
    }

    private void OnFolderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ProjectFolderNodeVm folder)
            return;
        if (e.PropertyName != nameof(ProjectWorkNodeVm.IsExpanded))
            return;
        _ = OnFolderExpandStateChangedAsync(folder);
    }

    private HashSet<int> CaptureExpandedFolderIds()
    {
        var ids = new HashSet<int>();
        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (folder.IsExpanded && folder.FolderId != 0)
                ids.Add(folder.FolderId);
        }

        return ids;
    }

    private HashSet<string> CaptureExpandedFolderPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (folder.IsExpanded && !string.IsNullOrWhiteSpace(folder.FullPath))
                paths.Add(folder.FullPath!);
        }

        return paths;
    }

    private void RestoreExpandedFolderIds(HashSet<int> expandedFolderIds)
    {
        if (expandedFolderIds.Count == 0)
            return;

        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (expandedFolderIds.Contains(folder.FolderId))
                folder.IsExpanded = true;
        }
    }

    private void RestoreExpandedFolderPaths(HashSet<string> expandedPaths)
    {
        if (expandedPaths.Count == 0)
            return;

        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (!string.IsNullOrWhiteSpace(folder.FullPath) && expandedPaths.Contains(folder.FullPath!))
                folder.IsExpanded = true;
        }
    }

    private void CollapseAllFolders()
    {
        _suppressExpandHandlers = true;
        try
        {
            foreach (var folder in EnumerateFolders(RootFolders).ToList())
            {
                folder.IsExpanded = false;
                UnloadFolderContentsCore(folder, collapseChildren: false);
            }
        }
        finally
        {
            _suppressExpandHandlers = false;
        }

        foreach (var root in RootFolders.OfType<ProjectFolderNodeVm>())
            RefreshHasFiles(root);

        RefreshWatchSet();
        ScanStatus = "כל התיקיות כווצו והתוכן שוחרר מהזיכרון.";
    }

    /// <summary>
    /// DEV-003 E: re-scan each folder's FileServer listing, delete only paired stale recovers
    /// (threshold 0; orphans never). Confirm first.
    /// </summary>
    private async Task DeleteStaleRecoversAsync()
    {
        if (_currentProjectId <= 0 || _scanTargets.Count == 0)
        {
            ScanStatus = "אין פרויקט טעון למחיקת recover.";
            return;
        }

        var candidates = new List<(string Path, string FileName)>();
        foreach (var node in EnumerateFolders(RootFolders))
        {
            if (string.IsNullOrWhiteSpace(node.FullPath) || !Directory.Exists(node.FullPath))
            {
                continue;
            }

            var stamps = new List<RecoverScanClassifier.FileStamp>();
            var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(node.FullPath))
            {
                var name = Path.GetFileName(path);
                if (ShouldSkipPathFromStaleRecoverSweep(name))
                {
                    continue;
                }

                var info = new FileInfo(path);
                stamps.Add(new RecoverScanClassifier.FileStamp(name, info.Length, info.LastWriteTimeUtc));
                pathByName[name] = path;
            }

            var byName = stamps.ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
            foreach (var stamp in stamps)
            {
                if (!RecoverFileNaming.TryGetPrimaryFileName(stamp.FileName, out var primaryName))
                {
                    continue;
                }

                if (!byName.TryGetValue(primaryName, out var primary))
                {
                    continue;
                }

                if (!RecoverFileRelevance.IsEligibleForStaleDelete(
                        hasPrimary: true,
                        recoverLength: stamp.SizeBytes,
                        recoverLastWrite: stamp.LastModified ?? DateTime.MinValue,
                        primaryLastWrite: primary.LastModified ?? DateTime.MinValue))
                {
                    continue;
                }

                if (pathByName.TryGetValue(stamp.FileName, out var fullPath))
                {
                    candidates.Add((fullPath, stamp.FileName));
                }
            }
        }

        if (candidates.Count == 0)
        {
            ScanStatus = "לא נמצאו קבצי recover ישנים למחיקה.";
            return;
        }

        var sample = string.Join("\n", candidates.Take(8).Select(c => c.FileName));
        var more = candidates.Count > 8 ? $"\n… ועוד {candidates.Count - 8}" : string.Empty;
        var confirm = MessageBox.Show(
            $"למחוק {candidates.Count} קבצי recover ישנים (עם DWG מקור באותה תיקייה)?\n\n{sample}{more}",
            "מחק recover ישנים",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            ScanStatus = "מחיקת recover בוטלה.";
            return;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var (path, _) in candidates)
        {
            try
            {
                File.Delete(path);
                deleted++;
                try
                {
                    var sidecar = path + ".si.json";
                    if (File.Exists(sidecar))
                    {
                        File.Delete(sidecar);
                    }
                }
                catch
                {
                    // Sidecar cleanup is best-effort.
                }
            }
            catch
            {
                failed++;
            }
        }

        ScanStatus = failed == 0
            ? $"נמחקו {deleted} קבצי recover ישנים."
            : $"נמחקו {deleted} recover; {failed} נכשלו (קובץ פתוח?).";
        await RescanAsync().ConfigureAwait(true);
    }

    private bool ShouldSkipPathFromStaleRecoverSweep(string fileName) =>
        fileName.EndsWith(".si.json", StringComparison.OrdinalIgnoreCase)
        || (_scanExclusions?.ShouldExclude(fileName)
            ?? ProjectWorkScanExclusions.IsExcludedExtension(fileName));

    private async Task CreateChildFolderAsync(ProjectFolderNodeVm parent)
    {
        if (_currentProjectId <= 0 || string.IsNullOrWhiteSpace(parent.FullPath))
        {
            ScanStatus = "לא ניתן ליצור תיקייה — נתיב האב לא זוהה.";
            return;
        }

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

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ScanStatus = "שם התיקייה מכיל תווים לא חוקיים.";
            return;
        }

        var dest = Path.Combine(parent.FullPath, name);
        try
        {
            if (Directory.Exists(dest)
                || parent.Children.OfType<ProjectFolderNodeVm>()
                    .Any(c => string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase)))
            {
                ScanStatus = $"כבר קיימת תיקייה בשם '{name}'.";
                return;
            }

            Directory.CreateDirectory(dest);
            var child = new ProjectFolderNodeVm
            {
                FolderId = _nextUserFolderId--,
                Title = name,
                FullPath = dest,
                IsUserCreated = true,
            };
            WireFolderCommands(child);
            InsertFolderChild(parent, child);
            parent.IsExpanded = true;
            RefreshHasFiles(parent);
            (child.DeleteFolderCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            ScanStatus = $"נוצרה תיקייה: {name}";
            await Task.CompletedTask.ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ScanStatus = $"יצירת תיקייה נכשלה: {ex.Message}";
        }
    }

    private async Task DeleteUserFolderAsync(ProjectFolderNodeVm folder)
    {
        if (!folder.CanDeleteFolder || string.IsNullOrWhiteSpace(folder.FullPath))
            return;

        var confirm = MessageBox.Show(
            $"למחוק את התיקייה הידנית '{folder.Title}'?\nהתיקייה ריקה ואין בה קבצים.",
            "מחיקת תיקייה",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            if (Directory.Exists(folder.FullPath))
                Directory.Delete(folder.FullPath, recursive: true);

            var parent = FindParentFolder(folder);
            if (parent is not null)
            {
                parent.Children.Remove(folder);
                RefreshHasFiles(parent);
            }
            else
            {
                RootFolders.Remove(folder);
            }

            ScanStatus = $"נמחקה תיקייה: {folder.Title}";
            await Task.CompletedTask.ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScanStatus = $"מחיקת תיקייה נכשלה: {ex.Message}";
        }
    }

    private ProjectFolderNodeVm? FindParentFolder(ProjectFolderNodeVm target)
    {
        foreach (var folder in EnumerateFolders(RootFolders))
        {
            if (folder.Children.Contains(target))
                return folder;
        }

        return null;
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
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;
        var name = StringPromptDialog.Prompt(
            owner,
            "\u05E9\u05DD \u05D0\u05DC\u05D8\u05E8\u05E0\u05D8\u05D9\u05D1\u05D4", // שם אלטרנטיבה
            "\u05D4\u05DB\u05E0\u05E1 \u05E9\u05DD \u05DC\u05D0\u05DC\u05D8\u05E8\u05E0\u05D8\u05D9\u05D1\u05D4 \u05D4\u05D7\u05D3\u05E9\u05D4:", // הכנס שם לאלטרנטיבה החדשה:
            initial: "1");
        if (name is null)
            return null;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        var used = file.Children.OfType<AlternativeNodeVm>()
            .Select(a => a.AlternativeName)
            .ToHashSet(StringComparer.Ordinal);
        if (used.Contains(trimmed))
        {
            System.Windows.MessageBox.Show(
                $"\u05D4\u05E9\u05DD '{trimmed}' \u05DB\u05D1\u05E8 \u05E7\u05D9\u05D9\u05DD.", // השם '{trimmed}' כבר קיים.
                "\u05E1\u05D1\u05D9\u05D1\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return null;
        }

        return trimmed;
    }

    private async Task AddVersionFromTemplateAsync(ProjectFileNodeVm file)
    {
        if (!file.CanAddFromTemplate || string.IsNullOrWhiteSpace(file.TemplateLocation))
            return;

        var altName = PromptAlternativeName(file);
        if (string.IsNullOrWhiteSpace(altName))
            return;

        var outcome = await AddVersionAsync(file, altName, file.TemplateLocation).ConfigureAwait(true);
        ReportOutcome(outcome, file.Title);
        if (outcome.Status != FileWriteStatus.Success)
            return;

        var alt = file.Children.OfType<AlternativeNodeVm>()
            .FirstOrDefault(a => string.Equals(a.AlternativeName, altName, StringComparison.Ordinal));
        var version = alt?.Children.OfType<VersionNodeVm>()
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
        if (version is null)
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(version.FullPath) && File.Exists(version.FullPath))
                Process.Start(new ProcessStartInfo(version.FullPath) { UseShellExecute = true });
            else if (version.OpenCommand?.CanExecute(null) == true)
                version.OpenCommand.Execute(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[ProjectWork] Open after template place failed: {ex.Message}");
        }
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
        StopReconcilePoll();
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
