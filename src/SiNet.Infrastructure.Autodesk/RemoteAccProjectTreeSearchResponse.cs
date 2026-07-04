using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccProjectTreeSearchResponse(
    IReadOnlyList<AccProjectTreeSearchMatch>? Matches,
    int VisitedFolderCount,
    bool HitFolderLimit,
    bool HitResultLimit);
