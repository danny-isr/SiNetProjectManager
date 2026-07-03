namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccDocumentLookupResponse(
    string ProjectId,
    string ItemId,
    string? VersionId,
    string? ViewerUrl);
