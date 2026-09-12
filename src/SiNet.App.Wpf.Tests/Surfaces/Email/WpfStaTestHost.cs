using System.Windows.Threading;
using SiNet.App.Wpf.Infrastructure;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

/// <summary>
/// Dedicated STA dispatcher for CollectionView tests. Does not create
/// <see cref="System.Windows.Application"/> so existing tests keep executing
/// <see cref="UiThread"/> inline when they have no dispatcher override.
/// </summary>
internal static class WpfStaTestHost
{
    private static readonly object Gate = new();
    private static Dispatcher? _dispatcher;

    public static Task RunAsync(Func<Task> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        EnsureDispatcher();
        var dispatcher = _dispatcher!;
        return dispatcher.InvokeAsync(async () =>
        {
            UiThread.TestDispatcher.Value = dispatcher;
            try
            {
                await test().ConfigureAwait(true);
            }
            finally
            {
                UiThread.TestDispatcher.Value = null;
            }
        }).Task.Unwrap();
    }

    private static void EnsureDispatcher()
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
            {
                return;
            }

            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                ready.TrySetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WpfStaTestDispatcher",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            _dispatcher = ready.Task.GetAwaiter().GetResult();
        }
    }
}
