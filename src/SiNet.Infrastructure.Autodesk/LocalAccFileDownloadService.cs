using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccFileDownloadService(IAccTransferConnector connector) : IAccFileDownloadService
{
    private readonly IAccTransferConnector _connector = connector;

    public async Task<AccFileDownloadResult?> DownloadToTempAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var result = await _connector
            .DownloadFileToTempAsync(projectId, itemId, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? null
            : new AccFileDownloadResult(
                result.Value.TempFilePath,
                result.Value.FileName,
                result.Value.TipVersionId);
    }
}
