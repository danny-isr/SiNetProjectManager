namespace SiNet.Infrastructure.Autodesk;

internal sealed record RemoteAccFolderBrowseResponse(
    string ProjectId,
    string FolderId,
    IReadOnlyList<RemoteAccFolderBrowseEntryResponse>? Entries);

internal sealed record RemoteAccFolderBrowseEntryResponse(
    string Id,
    string DisplayName,
    int Kind,
    long FileSize,
    DateTime? LastModifiedTime,
    DateTime? CreateTime);
