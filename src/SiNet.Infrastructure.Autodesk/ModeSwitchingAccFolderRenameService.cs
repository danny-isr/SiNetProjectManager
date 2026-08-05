using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Folder rename: Local → BIM360 connector; Remote → AccService HTTP (DEV-008 Layer A).
/// </summary>
internal sealed class ModeSwitchingAccFolderRenameService(
    IAccServiceModeProvider serviceModeProvider,
    LocalAccFolderRenameService local,
    RemoteAccFolderRenameService remote) : IAccFolderRenameService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly LocalAccFolderRenameService _local = local;
    private readonly RemoteAccFolderRenameService _remote = remote;

    public Task<AccFolderRenameOutcome> RenameFolderAsync(
        string accProjectId,
        string folderId,
        string newFolderName,
        CancellationToken cancellationToken = default) =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remote.RenameFolderAsync(accProjectId, folderId, newFolderName, cancellationToken)
            : _local.RenameFolderAsync(accProjectId, folderId, newFolderName, cancellationToken);
}
