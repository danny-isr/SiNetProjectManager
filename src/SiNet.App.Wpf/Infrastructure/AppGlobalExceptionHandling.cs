using System.Windows;
using System.Windows.Threading;
using SiNet.Application.Workflow;

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

        AdoptionDebugLog.Reset();
        AdoptionDebugLog.Write("[Unhandled]", "handlers attached; product exception policy unchanged");

        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AdoptionDebugLog.Error("[Unhandled] DispatcherUnhandledException", e.Exception);
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
        AdoptionDebugLog.Error("[Unhandled] AppDomain.UnhandledException", ex);
        AppErrorReporter.Report(ex, "AppDomain.UnhandledException");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AdoptionDebugLog.Error("[Unhandled] TaskScheduler.UnobservedTaskException", e.Exception);
        AppErrorReporter.Report(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }
}
