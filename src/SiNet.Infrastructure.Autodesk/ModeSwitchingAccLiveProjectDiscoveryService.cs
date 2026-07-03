using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccLiveProjectDiscoveryService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccLiveProjectDiscoveryService localLiveProjectDiscoveryService,
    RemoteAccLiveProjectDiscoveryService remoteLiveProjectDiscoveryService) : IAccLiveProjectDiscoveryService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccLiveProjectDiscoveryService _localLiveProjectDiscoveryService = localLiveProjectDiscoveryService;
    private readonly RemoteAccLiveProjectDiscoveryService _remoteLiveProjectDiscoveryService = remoteLiveProjectDiscoveryService;

    public Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteLiveProjectDiscoveryService.GetHubsAsync(cancellationToken)
            : _localLiveProjectDiscoveryService.GetHubsAsync(cancellationToken);

    public Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
        string hubId,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteLiveProjectDiscoveryService.GetProjectsAsync(hubId, cancellationToken)
            : _localLiveProjectDiscoveryService.GetProjectsAsync(hubId, cancellationToken);
}
