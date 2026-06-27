namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Discovers ACC projects. Implemented by <c>SiNet.Infrastructure.Autodesk</c>,
/// or temporarily by <c>SiNet.LegacyBridge</c> over the existing <c>Bim360Service</c>.
/// </summary>
public interface IAccProjectService
{
    Task<IReadOnlyList<string>> GetProjectIdsAsync(CancellationToken cancellationToken = default);
}
