namespace SiNet.Infrastructure.Autodesk;

internal sealed record AccDocumentLookupResult(
    string ProjectId,
    string ItemId,
    string DisplayName,
    string? VersionId,
    string? ViewerUrl);
