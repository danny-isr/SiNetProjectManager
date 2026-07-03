using System.Windows;
using System.Windows.Threading;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Wires global exception safety nets for the New System WPF host (see audit priority 1 / docs/LOGGING.md).
/// </summary>
public static class AppGlobalExceptionHandling
{
    private static bool _configured;

    public static void Configure(System.Windows.Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (_configured)
        {
            return;
        }

        _configured = true;

        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppErrorReporter.Report(e.Exception, "DispatcherUnhandledException");
        MessageBox.Show(
            AppErrorReporter.FormatUserMessage(e.Exception, "UI"),
            "שגיאה לא צפויה",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception
            ?? new Exception($"Unknown AppDomain exception: {e.ExceptionObject}");
        AppErrorReporter.Report(ex, "AppDomain.UnhandledException");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppErrorReporter.Report(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }
}
