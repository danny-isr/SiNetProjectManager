namespace SiNet.Application.ProjectWork;

/// <summary>
/// Watches file-server folders for changes and raises a single debounced callback after activity
/// settles, so the ProjectWork surface can rescan without hammering the disk on every event. Clean-layer
/// port of the legacy <c>FileSystemWatcher</c> usage in <c>ProjectWorkViewModel</c>; the OS-specific
/// implementation lives in <c>SiNet.Infrastructure.FileSystem</c>.
/// </summary>
public interface IFileServerWatcher : IDisposable
{
    /// <summary>
    /// Starts watching <paramref name="folderPaths"/> (direct children only — not recursive).
    /// Any create/change/delete/rename under a watched folder schedules a debounced call to
    /// <paramref name="onChangedDebounced"/> with the last affected path (or null if unknown).
    /// Replaces any previously watched set. Paths that do not exist are skipped.
    /// </summary>
    void Watch(IEnumerable<string> folderPaths, Action<string?> onChangedDebounced);

    /// <summary>Stops watching all folders and cancels any pending debounced callback.</summary>
    void StopAll();
}
