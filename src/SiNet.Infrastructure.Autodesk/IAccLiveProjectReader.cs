using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal interface IAccLiveProjectReader
{
    Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
        string hubId,
        CancellationToken cancellationToken = default);
}
