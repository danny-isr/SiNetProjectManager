namespace SiNet.Application.Abstractions.Autodesk;

public sealed record AccProjectTreeSearchMatch(
    string ProjectId,
    string FolderId,
    string FolderPath,
    string FileName);
