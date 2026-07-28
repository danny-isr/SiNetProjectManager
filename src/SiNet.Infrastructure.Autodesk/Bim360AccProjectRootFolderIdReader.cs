using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class Bim360AccProjectRootFolderIdReader(ITokenProvider? tokenProvider)
    : IAccProjectRootFolderIdReader
{
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public async Task<string?> GetProjectRootFolderIdAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (_tokenProvider is null
            || string.IsNullOrWhiteSpace(hubId)
            || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await new Bim360Service(_tokenProvider)
            .GetProjectRootFolderIdAsync(hubId, projectId)
            .ConfigureAwait(false);
    }
}
