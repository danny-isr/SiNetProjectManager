namespace SiNet.Application.Abstractions.Autodesk;

public sealed record AccInboxBootstrapResult(
    string HubId,
    string AccProjectId,
    string AccRootFolderId,
    string AccInboxFolderId);
