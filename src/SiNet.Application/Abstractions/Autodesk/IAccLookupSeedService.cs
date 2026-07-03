namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Returns read-only candidate triples (`projectId + folderId + fileName`) already known in SQL, so
/// native manual testers can avoid inventing ACC identifiers by hand.
/// </summary>
public interface IAccLookupSeedService
{
    Task<IReadOnlyList<AccDocumentLookupSeed>> GetRecentSeedsAsync(CancellationToken cancellationToken = default);
}
