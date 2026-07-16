using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// NewShell host: caches <see cref="ProjectWorkWindowView"/> and navigates via
/// <see cref="IShellContentHost"/> (same pattern as email — not disposed on switch).
/// </summary>
public sealed class ProjectWorkSurfaceHost(
    IServiceProvider services,
    IShellContentHost contentHost) : IProjectWorkSurfaceHost
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly IShellContentHost _contentHost = contentHost ?? throw new ArgumentNullException(nameof(contentHost));

    private ProjectWorkWindowView? _view;

    /// <inheritdoc />
    public async Task<bool> TryOpenBrowseAsync(CancellationToken cancellationToken = default)
    {
        if (!_contentHost.IsAttached)
            return false;

        var surface = EnsureCreated();
        await surface.OpenBrowseModeAsync(cancellationToken).ConfigureAwait(true);
        _contentHost.NavigateTo(surface);
        ActivateMainWindow();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryOpenFromTaskAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_contentHost.IsAttached)
            return false;

        var surface = EnsureCreated();
        var opened = await surface.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
        if (!opened)
            return false;

        _contentHost.NavigateTo(surface);
        ActivateMainWindow();
        return true;
    }

    private ProjectWorkWindowView EnsureCreated()
    {
        if (_view is not null)
            return _view;

        var factory = _services.GetRequiredService<IProjectWorkWindowFactory>();
        _view = factory.Create();
        return _view;
    }

    private static void ActivateMainWindow()
    {
        if (System.Windows.Application.Current?.MainWindow is not { } main)
            return;

        if (main.WindowState == WindowState.Minimized)
            main.WindowState = WindowState.Normal;

        main.Activate();
    }
}
