namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only DB-backed hint for manual ACC lookup testing. These values are convenience inputs only;
/// the actual ACC Docs URL must still be derived from a live resolve result.
/// </summary>
public sealed record AccDocumentLookupSeed(
    string ProjectId,
    string FolderId,
    string FileName,
    string? ItemId,
    string SourceLabel);
