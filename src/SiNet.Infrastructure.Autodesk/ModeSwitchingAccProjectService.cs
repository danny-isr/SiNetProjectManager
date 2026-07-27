using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccProjectService(
    IAccServiceModeProvider serviceModeProvider,
    ILocalAccProjectService localProjectService,
    RemoteAccProjectService remoteProjectService) : IAccProjectService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly ILocalAccProjectService _localProjectService = localProjectService;
    private readonly RemoteAccProjectService _remoteProjectService = remoteProjectService;

    public Task<IReadOnlyList<string>> GetProjectIdsAsync(CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteProjectService.GetProjectIdsAsync(cancellationToken)
            : _localProjectService.GetProjectIdsAsync(cancellationToken);
}
