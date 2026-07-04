using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccProjectTreeSearchService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccProjectTreeSearchService localProjectTreeSearchService,
    RemoteAccProjectTreeSearchService remoteProjectTreeSearchService) : IAccProjectTreeSearchService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccProjectTreeSearchService _localProjectTreeSearchService = localProjectTreeSearchService;
    private readonly RemoteAccProjectTreeSearchService _remoteProjectTreeSearchService = remoteProjectTreeSearchService;

    public Task<AccProjectTreeSearchResult> SearchAsync(
        string projectId,
        string fileName,
        string? folderId = null,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteProjectTreeSearchService.SearchAsync(projectId, fileName, folderId, cancellationToken)
            : _localProjectTreeSearchService.SearchAsync(projectId, fileName, folderId, cancellationToken);
}
