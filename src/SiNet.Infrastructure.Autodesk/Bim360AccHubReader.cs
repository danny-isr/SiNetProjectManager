using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class Bim360AccHubReader(ITokenProvider? tokenProvider) : IAccHubReader
{
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public async Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenProvider is null)
        {
            return [];
        }

        var hubs = await new Bim360Service(_tokenProvider)
            .ListHubsAsync(cancellationToken)
            .ConfigureAwait(false);

        return hubs
            .Where(static hub => !string.IsNullOrWhiteSpace(hub.Id))
            .Select(static hub => new AccHubCatalogEntry(
                hub.Id.Trim(),
                string.IsNullOrWhiteSpace(hub.Name) ? hub.Id.Trim() : hub.Name.Trim(),
                hub.Region))
            .OrderBy(static hub => hub.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static hub => hub.HubId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
