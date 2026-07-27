namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Resolves the Autodesk Docs root folder for a known ACC project.
/// </summary>
public interface IAccProjectRootFolderResolver
{
    Task<string?> ResolveProjectFilesRootFolderIdAsync(
        string projectId,
        CancellationToken cancellationToken = default);
}
