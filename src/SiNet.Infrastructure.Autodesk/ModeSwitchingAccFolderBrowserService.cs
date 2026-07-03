using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccFolderBrowserService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccFolderBrowserService localFolderBrowserService,
    RemoteAccFolderBrowserService remoteFolderBrowserService) : IAccFolderBrowserService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccFolderBrowserService _localFolderBrowserService = localFolderBrowserService;
    private readonly RemoteAccFolderBrowserService _remoteFolderBrowserService = remoteFolderBrowserService;

    public Task<AccFolderBrowseResult?> BrowseAsync(
        string projectId,
        string? folderId = null,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteFolderBrowserService.BrowseAsync(projectId, folderId, cancellationToken)
            : _localFolderBrowserService.BrowseAsync(projectId, folderId, cancellationToken);
}
