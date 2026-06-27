using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;

namespace SiNet.App.Wpf;

/// <summary>
/// WPF host scaffold. Demonstrates the intended startup shape for the App-startup migration
/// round: build the service graph with the single <c>AddSiNet()</c> composition call, then
/// resolve the shell window from DI. This scaffold is NOT yet the production entry point and
/// does not replace the existing application.
/// </summary>
// Base type is fully qualified: this project references the SiNet.Application namespace, so an
// unqualified "Application" would bind to that namespace instead of System.Windows.Application.
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSiNet();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
