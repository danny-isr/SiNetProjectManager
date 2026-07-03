namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccLiveHubsResponse(IReadOnlyList<RemoteAccLiveHubEntryResponse>? Hubs);

internal sealed record RemoteAccLiveHubEntryResponse(
    string HubId,
    string DisplayName,
    string? Region);
