using System.IO;
using SiNet.Application.ProjectWork;

namespace SiNet.Infrastructure.FileSystem.ProjectWork;

/// <summary>
/// <see cref="IFileServerWatcher"/> over <see cref="FileSystemWatcher"/>. Watches each root path
/// recursively and coalesces bursts of file events into a single debounced callback (default 800ms) so
/// a rescan runs once after copying/saving settles. Not thread-affine — the callback fires on a timer
/// thread; callers marshal to the UI thread as needed.
/// </summary>
public sealed class FileServerWatcher : IFileServerWatcher
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(800);

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _debounceTimer;
    private Action? _onChanged;
    private bool _disposed;

    /// <inheritdoc />
    public void Watch(IEnumerable<string> rootPaths, Action onChangedDebounced)
    {
        ArgumentNullException.ThrowIfNull(rootPaths);
        ArgumentNullException.ThrowIfNull(onChangedDebounced);

        lock (_gate)
        {
            if (_disposed)
                return;

            StopAllCore();
            _onChanged = onChangedDebounced;

            foreach (var path in rootPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;

                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
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

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => ScheduleDebounced();

    private void ScheduleDebounced()
    {
        lock (_gate)
        {
            if (_disposed || _onChanged is null)
                return;
            _debounceTimer ??= new Timer(_ => FireDebounced());
            _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void FireDebounced()
    {
        Action? callback;
        lock (_gate)
        {
            if (_disposed)
                return;
            callback = _onChanged;
        }
        callback?.Invoke();
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
