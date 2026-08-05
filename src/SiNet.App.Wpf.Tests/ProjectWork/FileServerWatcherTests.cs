using System.IO;
using SiNet.Infrastructure.FileSystem.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class FileServerWatcherTests : IDisposable
{
    private readonly string _dir;

    public FileServerWatcherTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sinet_watch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task Watch_fires_debounced_callback_once_after_burst_of_changes()
    {
        using var watcher = new FileServerWatcher();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        string? lastPath = null;

        watcher.Watch(new[] { _dir }, path =>
        {
            Interlocked.Increment(ref callbackCount);
            lastPath = path;
            tcs.TrySetResult();
        });

        // Burst of changes — should coalesce into a single debounced callback.
        for (var i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(_dir, $"f{i}.txt"), "x");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);
        Assert.False(string.IsNullOrWhiteSpace(lastPath));

        // Give the debounce window time to prove it doesn't fire repeatedly for the same burst.
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void Watch_ignores_missing_paths_without_throwing()
    {
        using var watcher = new FileServerWatcher();
        watcher.Watch(new[] { Path.Combine(_dir, "missing") }, _ => { });
        watcher.StopAll();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
