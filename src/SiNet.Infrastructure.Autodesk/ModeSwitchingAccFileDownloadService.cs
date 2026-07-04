using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccFileDownloadService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccFileDownloadService localDownloadService,
    RemoteAccFileDownloadService remoteDownloadService) : IAccFileDownloadService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccFileDownloadService _localDownloadService = localDownloadService;
    private readonly RemoteAccFileDownloadService _remoteDownloadService = remoteDownloadService;

    public Task<AccFileDownloadResult?> DownloadToTempAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteDownloadService.DownloadToTempAsync(projectId, itemId, cancellationToken)
            : _localDownloadService.DownloadToTempAsync(projectId, itemId, cancellationToken);
}
