using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccDocumentService(IAccFolderItemsReader folderItemsReader) : IAccDocumentLookupBackend
{
    private readonly IAccFolderItemsReader _folderItemsReader = folderItemsReader;

    public async Task<AccItemRef?> FindItemAsync(
        string projectId,
        string folderId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(folderId)
            || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var items = await _folderItemsReader
            .GetFolderItemsAsync(projectId, folderId, cancellationToken)
            .ConfigureAwait(false);

        return AccDocumentLookupMatcher.Match(projectId, items, fileName);
    }
}
