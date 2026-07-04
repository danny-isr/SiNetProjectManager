using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccFolderPathService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccFolderPathService localFolderPathService,
    RemoteAccFolderPathService remoteFolderPathService) : IAccFolderPathService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccFolderPathService _localFolderPathService = localFolderPathService;
    private readonly RemoteAccFolderPathService _remoteFolderPathService = remoteFolderPathService;

    public Task<string?> TryResolvePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteFolderPathService.TryResolvePathAsync(projectId, rootFolderId, pathSegments, cancellationToken)
            : _localFolderPathService.TryResolvePathAsync(projectId, rootFolderId, pathSegments, cancellationToken);

    public Task<string> EnsurePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteFolderPathService.EnsurePathAsync(projectId, rootFolderId, pathSegments, cancellationToken)
            : _localFolderPathService.EnsurePathAsync(projectId, rootFolderId, pathSegments, cancellationToken);
}
