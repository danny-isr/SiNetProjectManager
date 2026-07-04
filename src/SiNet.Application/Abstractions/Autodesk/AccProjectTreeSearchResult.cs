namespace SiNet.Application.Abstractions.Autodesk;

public sealed record AccProjectTreeSearchResult(
    IReadOnlyList<AccProjectTreeSearchMatch> Matches,
    int VisitedFolderCount,
    bool HitFolderLimit,
    bool HitResultLimit);
