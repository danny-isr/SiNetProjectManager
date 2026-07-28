using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Diagnostics;

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
        // #region agent log
        AgentDebugNdjson.Write(
            "H5",
            "ModeSwitchingAccFileUploadService.UploadAsync",
            "upload routed",
            new Dictionary<string, object?>
            {
                ["mode"] = _serviceModeProvider.Mode.ToString(),
                ["displayName"] = request.DisplayName,
                ["hasTargetFolderId"] = !string.IsNullOrWhiteSpace(request.TargetFolderId),
                ["projectIdPrefix"] = string.IsNullOrEmpty(request.ProjectId)
                    ? null
                    : request.ProjectId.Length <= 12 ? request.ProjectId : request.ProjectId[..12] + "…",
            });
        // #endregion

        return _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteUploadService.UploadAsync(request, cancellationToken)
            : _localUploadService.UploadAsync(request, cancellationToken);
    }
}
