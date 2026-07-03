namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Returns a read-only catalog of known ACC projects for operator selection.
/// This catalog is intentionally safe: it may surface cached SQL names and IDs,
/// but it does not provision or mutate ACC state.
/// </summary>
public interface IAccProjectCatalogService
{
    Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(CancellationToken cancellationToken = default);
}
