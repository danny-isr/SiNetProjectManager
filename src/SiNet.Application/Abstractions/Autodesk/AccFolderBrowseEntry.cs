namespace SiNet.Application.Abstractions.Autodesk;

public sealed record AccFolderBrowseEntry(
    string Id,
    string DisplayName,
    AccFolderEntryKind Kind,
    long FileSize,
    DateTime? LastModifiedTime,
    DateTime? CreateTime);
