namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Resolves ACC documents/items using layout-aware lookups. A null result means the item was
/// not found in ACC (do not fabricate viewer URLs from database identifiers).
/// </summary>
public interface IAccDocumentService
{
    Task<AccItemRef?> FindItemAsync(
        string projectId,
        string folderId,
        string fileName,
        CancellationToken cancellationToken = default);
}
