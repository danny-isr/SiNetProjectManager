using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccLiveProjectDiscoveryService(
    IAccHubReader hubReader,
    IAccLiveProjectReader liveProjectReader) : IAccLiveProjectDiscoveryService
{
    private readonly IAccHubReader _hubReader = hubReader;
    private readonly IAccLiveProjectReader _liveProjectReader = liveProjectReader;

    public Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default) =>
        _hubReader.GetHubsAsync(cancellationToken);

    public Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
        string hubId,
        CancellationToken cancellationToken = default) =>
        _liveProjectReader.GetProjectsAsync(hubId, cancellationToken);
}
