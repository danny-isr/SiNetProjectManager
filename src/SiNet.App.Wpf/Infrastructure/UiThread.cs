using System.Threading;
using System.Windows.Threading;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Marshals work to the WPF UI (STA) dispatcher. Async continuations after
/// <c>ConfigureAwait(false)</c> often resume on a thread-pool thread; window creation,
/// <c>Show</c>/<c>ShowDialog</c>, and WPF-bound collection mutations must run on the UI thread.
/// </summary>
/// <remarks>
/// Ownership rule: WPF-bound collections and properties are UI-thread owned. Background work may
/// call Gmail, inspect History, wait on timers, and calculate, but it must not mutate
/// ObservableCollection-backed UI state directly. When already on the dispatcher, work runs
/// inline (no nested wait). Do not use <c>BindingOperations.EnableCollectionSynchronization</c>.
/// </remarks>
internal static class UiThread
{
    /// <summary>
    /// Test-only dispatcher override. Flows through <see cref="ExecutionContext"/> so
    /// <c>Task.Run</c> from an STA test still marshals to the test dispatcher. Production leaves
    /// this unset and uses <see cref="System.Windows.Application.Current"/>.
    /// </summary>
    internal static readonly AsyncLocal<Dispatcher?> TestDispatcher = new();

    internal static Dispatcher? Dispatcher =>
        TestDispatcher.Value ?? System.Windows.Application.Current?.Dispatcher;

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    public static Task RunAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    public static Task RunAsync(Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return func();
        }

        return dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task.Unwrap();
    }

    public static async Task<T> RunAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return func();
        }

        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task.ConfigureAwait(true);
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return await func().ConfigureAwait(true);
        }

        // Hop to the UI thread, then run the async work there so subsequent awaits
        // that use ConfigureAwait(true) stay on STA.
        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task.Unwrap().ConfigureAwait(true);
    }
}
