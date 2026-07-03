using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class Bim360AccFolderContentsReader(ITokenProvider? tokenProvider) : IAccFolderContentsReader
{
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public async Task<IReadOnlyList<AccFolderBrowseEntry>> GetFolderContentsAsync(
        string projectId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        if (_tokenProvider is null
            || string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(folderId))
        {
            return [];
        }

        var entries = await new Bim360Service(_tokenProvider)
            .GetFolderContentsAsync(projectId, folderId, cancellationToken)
            .ConfigureAwait(false);

        return entries
            .Select(static entry => new AccFolderBrowseEntry(
                entry.Id,
                entry.DisplayName,
                entry.IsFolder ? AccFolderEntryKind.Folder : AccFolderEntryKind.Item,
                entry.FileSize,
                entry.LastModifiedTime,
                entry.CreateTime))
            .ToArray();
    }
}
