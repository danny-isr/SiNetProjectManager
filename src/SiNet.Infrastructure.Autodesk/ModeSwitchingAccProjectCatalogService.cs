using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccProjectCatalogService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccProjectCatalogService localProjectCatalogService,
    RemoteAccProjectCatalogService remoteProjectCatalogService) : IAccProjectCatalogService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccProjectCatalogService _localProjectCatalogService = localProjectCatalogService;
    private readonly RemoteAccProjectCatalogService _remoteProjectCatalogService = remoteProjectCatalogService;

    public Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteProjectCatalogService.GetProjectsAsync(cancellationToken)
            : _localProjectCatalogService.GetProjectsAsync(cancellationToken);
}
