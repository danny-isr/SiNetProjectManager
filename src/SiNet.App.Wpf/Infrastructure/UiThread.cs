using System.Windows.Threading;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Marshals work to the WPF UI (STA) dispatcher. Async continuations after
/// <c>ConfigureAwait(false)</c> often resume on a thread-pool thread; window creation
/// and <c>Show</c>/<c>ShowDialog</c> must run on the UI thread.
/// </summary>
internal static class UiThread
{
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
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

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    public static async Task<T> RunAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return func();
        }

        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task.ConfigureAwait(true);
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return await func().ConfigureAwait(true);
        }

        // Hop to the UI thread, then run the async work there so subsequent awaits
        // that use ConfigureAwait(true) stay on STA.
        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task.Unwrap().ConfigureAwait(true);
    }
}
