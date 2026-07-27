namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Reads the local cached ACC project catalog for mode selection.
/// </summary>
public interface ILocalAccProjectCatalogService
{
    Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(CancellationToken cancellationToken = default);
}
