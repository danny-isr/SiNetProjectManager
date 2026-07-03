namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccProjectCatalogResponse(IReadOnlyList<RemoteAccProjectCatalogEntryResponse>? Projects);

internal sealed record RemoteAccProjectCatalogEntryResponse(
    string ProjectId,
    string DisplayName,
    string SourceLabel);
