using MyOffice.AutodeskConnector;

namespace SiNet.Infrastructure.Autodesk;

internal interface IAccTransferConnector
{
    Task<string> EnsureFolderPathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccFolderItem>> GetFolderItemsAsync(
        string projectId,
        string folderId,
        CancellationToken cancellationToken = default);

    Task<string?> GetFolderByNameAsync(
        string projectId,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken = default);

    Task<UploadResult> UploadFileFinalAsync(
        string projectId,
        string folderId,
        string localSourcePath,
        string? displayName,
        CancellationToken cancellationToken = default);

    Task<UploadResult> UploadNewVersionAsync(
        string projectId,
        string folderId,
        string itemId,
        string localSourcePath,
        CancellationToken cancellationToken = default);

    Task<(string TempFilePath, string FileName, string? TipVersionId)?> DownloadFileToTempAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<string?> GetItemTipVersionIdAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<string?> GetItemDisplayNameAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<int?> GetItemVersionCountAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<bool> HideItemAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>Renames an ACC Docs folder (id stable). DEV-008 Layer A.</summary>
    Task RenameFolderAsync(
        string projectId,
        string folderId,
        string newFolderName,
        CancellationToken cancellationToken = default);

    Task<AccMetadataResult<IReadOnlyDictionary<string, string?>>> GetItemCustomAttributesAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<AccMetadataResult> SetItemCustomAttributesAsync(
        string projectId,
        string folderId,
        string versionId,
        IReadOnlyDictionary<string, string?> attributes,
        CancellationToken cancellationToken = default);
}
