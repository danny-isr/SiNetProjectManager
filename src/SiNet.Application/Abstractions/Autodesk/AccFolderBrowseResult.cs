namespace SiNet.Application.Abstractions.Autodesk;

public sealed record AccFolderBrowseResult(
    string ProjectId,
    string FolderId,
    IReadOnlyList<AccFolderBrowseEntry> Entries);
