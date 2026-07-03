namespace SiNet.Infrastructure.Autodesk;

internal interface IAccFolderItemsReader
{
    Task<IReadOnlyList<AccDocumentLookupResult>> GetFolderItemsAsync(
        string projectId,
        string folderId,
        CancellationToken cancellationToken = default);
}
