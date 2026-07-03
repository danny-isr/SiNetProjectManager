using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal interface IAccHubReader
{
    Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default);
}
