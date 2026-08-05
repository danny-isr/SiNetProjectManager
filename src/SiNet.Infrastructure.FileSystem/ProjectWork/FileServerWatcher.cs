using System.IO;
using SiNet.Application.ProjectWork;

namespace SiNet.Infrastructure.FileSystem.ProjectWork;

/// <summary>
/// <see cref="IFileServerWatcher"/> over <see cref="FileSystemWatcher"/>. Watches each folder path
/// (non-recursive) and coalesces bursts of file events into a single debounced callback (default 800ms)
/// carrying the last affected path. Not thread-affine — the callback fires on a timer thread; callers
/// marshal to the UI thread as needed.
/// </summary>
public sealed class FileServerWatcher : IFileServerWatcher
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(800);

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _debounceTimer;
    private Action<string?>? _onChanged;
    private string? _lastAffectedPath;
    private bool _disposed;

    /// <inheritdoc />
    public void Watch(IEnumerable<string> folderPaths, Action<string?> onChangedDebounced)
    {
        ArgumentNullException.ThrowIfNull(folderPaths);
        ArgumentNullException.ThrowIfNull(onChangedDebounced);

        lock (_gate)
        {
            if (_disposed)
                return;

            StopAllCore();
            _onChanged = onChangedDebounced;
            _lastAffectedPath = null;

            foreach (var path in folderPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;

                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                       | NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Changed += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemEvent;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch
                {
                    // A path that can't be watched (permissions, transient share issue) is skipped.
                }
            }
        }
    }

    /// <inheritdoc />
    public void StopAll()
    {
        lock (_gate)
            StopAllCore();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        => ScheduleDebounced(e.FullPath);

    private void ScheduleDebounced(string? affectedPath)
    {
        lock (_gate)
        {
            if (_disposed || _onChanged is null)
                return;
            if (!string.IsNullOrWhiteSpace(affectedPath))
                _lastAffectedPath = affectedPath;
            _debounceTimer ??= new Timer(_ => FireDebounced());
            _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void FireDebounced()
    {
        Action<string?>? callback;
        string? path;
        lock (_gate)
        {
            if (_disposed)
                return;
            callback = _onChanged;
            path = _lastAffectedPath;
            _lastAffectedPath = null;
        }
        callback?.Invoke(path);
    }

    private void StopAllCore()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnFileSystemEvent;
                watcher.Deleted -= OnFileSystemEvent;
                watcher.Changed -= OnFileSystemEvent;
                watcher.Renamed -= OnFileSystemEvent;
                watcher.Dispose();
            }
            catch
            {
                // best-effort teardown
            }
        }
        _watchers.Clear();
        _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _lastAffectedPath = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopAllCore();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _onChanged = null;
        }
    }
}
