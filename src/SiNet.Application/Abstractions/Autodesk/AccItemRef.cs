namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Reference to an item stored in Autodesk Construction Cloud (ACC). ACC is the source of
/// truth for the physical existence of a file; database identifiers are cache/helper only.
/// </summary>
public sealed record AccItemRef(
    string ProjectId,
    string ItemId,
    string? VersionId,
    string? ViewerUrl);
