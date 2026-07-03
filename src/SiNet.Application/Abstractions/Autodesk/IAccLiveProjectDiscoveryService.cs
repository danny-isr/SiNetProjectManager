namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Live, read-only ACC discovery for the operator flow:
/// hubs/accounts -> projects -> folders -> files.
/// </summary>
public interface IAccLiveProjectDiscoveryService
{
    Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
        string hubId,
        CancellationToken cancellationToken = default);
}
