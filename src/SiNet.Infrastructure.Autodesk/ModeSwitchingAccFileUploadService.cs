using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccFileUploadService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccFileUploadService localUploadService,
    RemoteAccFileUploadService remoteUploadService) : IAccFileUploadService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccFileUploadService _localUploadService = localUploadService;
    private readonly RemoteAccFileUploadService _remoteUploadService = remoteUploadService;

    public Task<AccFileUploadResult> UploadAsync(
        AccFileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        return _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteUploadService.UploadAsync(request, cancellationToken)
            : _localUploadService.UploadAsync(request, cancellationToken);
    }
}
