using MyOffice.AutodeskConnector;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class Bim360AccTransferConnector(ITokenProvider? tokenProvider) : IAccTransferConnector
{
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public Task<string> EnsureFolderPathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default) =>
        CreateService().EnsureFolderPathAsync(projectId, rootFolderId, pathSegments, cancellationToken);

    public async Task<IReadOnlyList<AccFolderItem>> GetFolderItemsAsync(
        string projectId,
        string folderId,
        CancellationToken cancellationToken = default) =>
        await CreateService().GetFolderItemsAsync(projectId, folderId, cancellationToken).ConfigureAwait(false);

    public Task<string?> GetFolderByNameAsync(
        string projectId,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken = default) =>
        CreateService().GetFolderByNameAsync(projectId, parentFolderId, folderName);

    public Task<UploadResult> UploadFileFinalAsync(
        string projectId,
        string folderId,
        string localSourcePath,
        string? displayName,
        CancellationToken cancellationToken = default) =>
        CreateService().UploadFileFinalAsync(projectId, folderId, localSourcePath, displayName);

    public Task<UploadResult> UploadNewVersionAsync(
        string projectId,
        string folderId,
        string itemId,
        string localSourcePath,
        CancellationToken cancellationToken = default) =>
        CreateService().UploadNewVersionAsync(projectId, folderId, itemId, localSourcePath);

    public Task<(string TempFilePath, string FileName)?> DownloadFileToTempAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        CreateService().DownloadFileToTempAsync(projectId, itemId, cancellationToken);

    public Task<string?> GetItemDisplayNameAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        CreateService().GetItemDisplayNameAsync(projectId, itemId, cancellationToken);

    public Task<int?> GetItemVersionCountAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        CreateService().GetItemVersionCountAsync(projectId, itemId, cancellationToken);

    public Task<bool> HideItemAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        CreateService().HideItemAsync(projectId, itemId, cancellationToken);

    public Task RenameFolderAsync(
        string projectId,
        string folderId,
        string newFolderName,
        CancellationToken cancellationToken = default) =>
        CreateService().RenameFolderAsync(projectId, folderId, newFolderName, cancellationToken);

    public Task<AccMetadataResult<IReadOnlyDictionary<string, string?>>> GetItemCustomAttributesAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        CreateService().GetItemCustomAttributesAsync(projectId, itemId, cancellationToken);

    public Task<AccMetadataResult> SetItemCustomAttributesAsync(
        string projectId,
        string folderId,
        string versionId,
        IReadOnlyDictionary<string, string?> attributes,
        CancellationToken cancellationToken = default) =>
        CreateService().SetItemCustomAttributesAsync(projectId, folderId, versionId, attributes, cancellationToken);

    private Bim360Service CreateService() => new(_tokenProvider);
}
