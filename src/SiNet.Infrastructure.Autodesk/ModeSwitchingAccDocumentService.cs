using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccDocumentService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccDocumentService localDocumentService,
    RemoteAccDocumentService remoteDocumentService) : IAccDocumentService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccDocumentService _localDocumentService = localDocumentService;
    private readonly RemoteAccDocumentService _remoteDocumentService = remoteDocumentService;

    public Task<AccItemRef?> FindItemAsync(
        string projectId,
        string folderId,
        string fileName,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteDocumentService.FindItemAsync(projectId, folderId, fileName, cancellationToken)
            : _localDocumentService.FindItemAsync(projectId, folderId, fileName, cancellationToken);
}
