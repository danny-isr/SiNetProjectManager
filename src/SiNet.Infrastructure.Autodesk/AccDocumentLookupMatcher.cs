using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal static class AccDocumentLookupMatcher
{
    public static AccItemRef? Match(
        string projectId,
        IEnumerable<AccDocumentLookupResult> candidates,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(fileName);

        if (string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var exact = candidates
            .Where(candidate => string.Equals(candidate.DisplayName, fileName, StringComparison.Ordinal))
            .Select(candidate => ToRef(projectId, candidate))
            .FirstOrDefault(candidate => candidate is not null);
        if (exact is not null)
        {
            return exact;
        }

        return candidates
            .Where(candidate => string.Equals(candidate.DisplayName, fileName, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => ToRef(projectId, candidate))
            .FirstOrDefault(candidate => candidate is not null);
    }

    private static AccItemRef? ToRef(string projectId, AccDocumentLookupResult result)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(result.ItemId))
        {
            return null;
        }

        return new(projectId.Trim(), result.ItemId.Trim(), result.VersionId, result.ViewerUrl);
    }
}
