namespace SiNet.Application.Projects;

/// <summary>
/// Read port that loads the full filter option lists for the shared Project Selector. Separate from
/// <see cref="IProjectQueryService"/> so filter dropdowns are never derived from capped search results.
/// </summary>
public interface IProjectFilterOptionsService
{
    /// <summary>
    /// Returns all selectable Status / Job Type (and optionally User) filter values. Never returns
    /// <see langword="null"/>.
    /// </summary>
    Task<ProjectFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default);
}
