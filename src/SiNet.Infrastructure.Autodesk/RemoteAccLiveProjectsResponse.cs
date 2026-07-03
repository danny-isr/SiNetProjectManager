namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccLiveProjectsResponse(IReadOnlyList<RemoteAccLiveProjectEntryResponse>? Projects);

internal sealed record RemoteAccLiveProjectEntryResponse(
    string ProjectId,
    string DisplayName);
