namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Reads known ACC project identifiers from the local persistence cache.
/// This port keeps Autodesk mode selection independent of the SQL implementation.
/// </summary>
public interface ILocalAccProjectService
{
    Task<IReadOnlyList<string>> GetProjectIdsAsync(CancellationToken cancellationToken = default);
}
