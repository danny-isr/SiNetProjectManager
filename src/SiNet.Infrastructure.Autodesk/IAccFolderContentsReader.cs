using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal interface IAccFolderContentsReader
{
    Task<IReadOnlyList<AccFolderBrowseEntry>> GetFolderContentsAsync(
        string projectId,
        string folderId,
        CancellationToken cancellationToken = default);
}
