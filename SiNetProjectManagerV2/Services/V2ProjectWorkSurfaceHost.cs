using System.Windows;
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Embeds ProjectWork in the V2 main shell content area (cached across navigation).
/// Used by <c>WorkSurfaceLauncher</c> so task opens prefer the shell over a floating window.
/// </summary>
internal sealed class V2ProjectWorkSurfaceHost : IProjectWorkSurfaceHost
{
    public async Task<bool> TryOpenBrowseAsync(CancellationToken cancellationToken = default)
    {
        if (Application.Current?.MainWindow is not MainWindow main)
            return false;
        return await main.TryOpenProjectWorkBrowseAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<bool> TryOpenFromTaskAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.MainWindow is not MainWindow main)
            return false;
        return await main.TryOpenProjectWorkFromTaskAsync(context, cancellationToken).ConfigureAwait(true);
    }
}
