using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccItemService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccItemService localAccItemService,
    RemoteAccItemService remoteAccItemService) : IAccItemService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccItemService _localAccItemService = localAccItemService;
    private readonly RemoteAccItemService _remoteAccItemService = remoteAccItemService;

    public Task<string?> GetDisplayNameAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteAccItemService.GetDisplayNameAsync(projectId, itemId, cancellationToken)
            : _localAccItemService.GetDisplayNameAsync(projectId, itemId, cancellationToken);

    public Task<int?> GetVersionCountAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteAccItemService.GetVersionCountAsync(projectId, itemId, cancellationToken)
            : _localAccItemService.GetVersionCountAsync(projectId, itemId, cancellationToken);

    public Task<bool> HideAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteAccItemService.HideAsync(projectId, itemId, cancellationToken)
            : _localAccItemService.HideAsync(projectId, itemId, cancellationToken);
}
