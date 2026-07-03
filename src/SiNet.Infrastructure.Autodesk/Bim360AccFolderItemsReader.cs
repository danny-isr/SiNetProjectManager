using MyOffice.AutodeskConnector;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class Bim360AccFolderItemsReader(ITokenProvider? tokenProvider) : IAccFolderItemsReader
{
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public async Task<IReadOnlyList<AccDocumentLookupResult>> GetFolderItemsAsync(
        string projectId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("ACC project id is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new ArgumentException("ACC folder id is required.", nameof(folderId));
        }

        if (_tokenProvider is null)
        {
            throw new InvalidOperationException(
                "Autodesk token provider is not registered for local ACC document lookup.");
        }

        var bim360 = new Bim360Service(_tokenProvider);
        var items = await bim360
            .GetFolderItemsAsync(projectId, folderId, cancellationToken)
            .ConfigureAwait(false);

        return items
            .Select(item => new AccDocumentLookupResult(
                projectId,
                item.ItemId,
                item.DisplayName,
                VersionId: null,
                ViewerUrl: null))
            .ToArray();
    }
}
