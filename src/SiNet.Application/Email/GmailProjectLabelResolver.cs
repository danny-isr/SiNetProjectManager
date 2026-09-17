namespace SiNet.Application.Email;

public enum GmailProjectLabelResolveKind
{
    CreateCanonical,
    ReuseExisting,
    DuplicateConflict
}

public sealed record GmailProjectLabelResolveDecision(
    GmailProjectLabelResolveKind Kind,
    ProjectLabelEntry? Existing,
    IReadOnlyList<ProjectLabelEntry> Matches);

/// <summary>
/// ProjectNumber wins over FullPath. Filing must not pick an arbitrary duplicate.
/// </summary>
public static class GmailProjectLabelResolver
{
    public static GmailProjectLabelResolveDecision Resolve(
        GmailProjectLabelIndex index,
        int projectNumber)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (projectNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectNumber), projectNumber, "ProjectNumber must be positive.");
        }

        var matches = index.Find(projectNumber);
        return matches.Count switch
        {
            0 => new GmailProjectLabelResolveDecision(
                GmailProjectLabelResolveKind.CreateCanonical, null, matches),
            1 => new GmailProjectLabelResolveDecision(
                GmailProjectLabelResolveKind.ReuseExisting, matches[0], matches),
            _ => new GmailProjectLabelResolveDecision(
                GmailProjectLabelResolveKind.DuplicateConflict, null, matches)
        };
    }
}
