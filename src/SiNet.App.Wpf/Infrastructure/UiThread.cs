namespace SiNet.App.Wpf.Infrastructure;

/// <summary>Marshals work to the WPF dispatcher when auth or gateway callbacks arrive off the UI thread.</summary>
internal static class UiThread
{
    public static void Run(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
