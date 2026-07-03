using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal interface IAccDocumentLookupBackend
{
    Task<AccItemRef?> FindItemAsync(
        string projectId,
        string folderId,
        string fileName,
        CancellationToken cancellationToken = default);
}
