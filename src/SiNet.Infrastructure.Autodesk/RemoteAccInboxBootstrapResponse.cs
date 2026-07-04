namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccInboxBootstrapResponse(
    int AccHubDbId,
    string HubId,
    string AccProjectId,
    string AccRootFolderId,
    string AccInboxFolderId);
