using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Autodesk.Metadata;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Routes ACC item metadata operations to the privileged in-process implementation
/// (<see cref="LocalAccItemMetadataService"/>) or to the HTTP client
/// (<see cref="RemoteAccItemMetadataService"/>) based on the configured
/// <see cref="IAccServiceModeProvider.Mode"/>. Preserves the structural client/server
/// separation: the WPF client runs Remote and never touches the ACC SDK directly, while
/// <c>SiOffice.AccService</c> runs Local.
/// </summary>
internal sealed class ModeSwitchingAccItemMetadataService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccItemMetadataService localService,
    RemoteAccItemMetadataService remoteService) : IAccItemMetadataService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccItemMetadataService _localService = localService;
    private readonly RemoteAccItemMetadataService _remoteService = remoteService;

    public ValueTask<AccItemMetadataReadResult> ReadAttributesAsync(
        string accProjectId,
        string itemId,
        string? fileNameForLogging,
        CancellationToken cancellationToken) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteService.ReadAttributesAsync(accProjectId, itemId, fileNameForLogging, cancellationToken)
            : _localService.ReadAttributesAsync(accProjectId, itemId, fileNameForLogging, cancellationToken);

    public ValueTask<AccItemMetadataResult> WriteAttributesAsync(
        string accProjectId,
        string accFolderId,
        string versionId,
        string itemId,
        IReadOnlyDictionary<string, string?> attributes,
        string? fileNameForLogging,
        CancellationToken cancellationToken) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteService.WriteAttributesAsync(accProjectId, accFolderId, versionId, itemId, attributes, fileNameForLogging, cancellationToken)
            : _localService.WriteAttributesAsync(accProjectId, accFolderId, versionId, itemId, attributes, fileNameForLogging, cancellationToken);
}
