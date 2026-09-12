namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Observes an asynchronous operation so a fault cannot later surface as
/// <see cref="TaskScheduler.UnobservedTaskException"/> after GC.
/// Reports through <see cref="AppErrorReporter"/> (existing diagnostic path).
/// </summary>
internal static class ObservedTask
{
    public static Task Run(Task task, string context, Action<Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return ObserveAsync(task, context, onFault);
    }

    public static Task Run(Func<Task> work, string context, Action<Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return ObserveAsync(work(), context, onFault);
    }

    private static async Task ObserveAsync(Task task, string context, Action<Exception>? onFault)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppErrorReporter.Report(ex, context);
            if (onFault is null)
            {
                return;
            }

            try
            {
                onFault(ex);
            }
            catch (Exception faultEx)
            {
                AppErrorReporter.Report(faultEx, context + ".OnFault");
            }
        }
    }
}
