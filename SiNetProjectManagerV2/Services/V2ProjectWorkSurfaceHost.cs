using System.Windows;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Prefers legacy <see cref="MainWindow"/> content hosting when that shell is open;
/// otherwise delegates to NewShell <see cref="ProjectWorkSurfaceHost"/> (cached UserControl).
/// </summary>
internal sealed class V2ProjectWorkSurfaceHost(ProjectWorkSurfaceHost newShellHost) : IProjectWorkSurfaceHost
{
    private readonly ProjectWorkSurfaceHost _newShellHost =
        newShellHost ?? throw new ArgumentNullException(nameof(newShellHost));

    public async Task<bool> TryOpenBrowseAsync(CancellationToken cancellationToken = default)
    {
        if (Application.Current?.MainWindow is MainWindow main)
            return await main.TryOpenProjectWorkBrowseAsync(cancellationToken).ConfigureAwait(true);

        return await _newShellHost.TryOpenBrowseAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<bool> TryOpenFromTaskAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.MainWindow is MainWindow main)
            return await main.TryOpenProjectWorkFromTaskAsync(context, cancellationToken).ConfigureAwait(true);

        return await _newShellHost.TryOpenFromTaskAsync(context, cancellationToken).ConfigureAwait(true);
    }
}
