using SiNet.Application.WorkSurfaces;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Optional host seam that embeds the ProjectWork surface in the production shell content area
/// (cached across navigation, like Email). When registered, menu browse and task opens prefer this
/// over a floating window so file-query / file-open providers stay process-wide.
/// </summary>
public interface IProjectWorkSurfaceHost
{
    /// <summary>Shows browse mode in the shell. Returns <see langword="false"/> when unsupported.</summary>
    Task<bool> TryOpenBrowseAsync(CancellationToken cancellationToken = default);

    /// <summary>Shows task mode in the shell. Returns <see langword="false"/> to fall back to a floating window.</summary>
    Task<bool> TryOpenFromTaskAsync(WorkSurfaceContext context, CancellationToken cancellationToken = default);
}
