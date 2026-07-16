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
    /// Starts watching <paramref name="rootPaths"/> (recursively). Any create/change/delete/rename under
    /// a watched root schedules a debounced call to <paramref name="onChangedDebounced"/>. Replaces any
    /// previously watched set. Paths that do not exist are skipped.
    /// </summary>
    void Watch(IEnumerable<string> rootPaths, Action onChangedDebounced);

    /// <summary>Stops watching all folders and cancels any pending debounced callback.</summary>
    void StopAll();
}
