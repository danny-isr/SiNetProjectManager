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

        var exact = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayName, fileName, StringComparison.Ordinal));
        if (exact is not null)
        {
            return ToRef(projectId, exact);
        }

        var ignoreCase = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));
        return ignoreCase is null ? null : ToRef(projectId, ignoreCase);
    }

    private static AccItemRef ToRef(string projectId, AccDocumentLookupResult result) =>
        new(projectId, result.ItemId, result.VersionId, result.ViewerUrl);
}
