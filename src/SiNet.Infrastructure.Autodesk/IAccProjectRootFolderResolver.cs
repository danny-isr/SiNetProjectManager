namespace SiNet.Infrastructure.Autodesk;

internal interface IAccProjectRootFolderResolver
{
    Task<string?> ResolveProjectFilesRootFolderIdAsync(
        string projectId,
        CancellationToken cancellationToken = default);
}
