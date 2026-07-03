using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccFolderBrowserService(
    IAccProjectRootFolderResolver projectRootFolderResolver,
    IAccFolderContentsReader folderContentsReader) : IAccFolderBrowserService
{
    private readonly IAccProjectRootFolderResolver _projectRootFolderResolver = projectRootFolderResolver;
    private readonly IAccFolderContentsReader _folderContentsReader = folderContentsReader;

    public async Task<AccFolderBrowseResult?> BrowseAsync(
        string projectId,
        string? folderId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var normalizedProjectId = NormalizeProjectId(projectId);
        var resolvedFolderId = string.IsNullOrWhiteSpace(folderId)
            ? await _projectRootFolderResolver.ResolveProjectFilesRootFolderIdAsync(normalizedProjectId, cancellationToken).ConfigureAwait(false)
            : folderId.Trim();
        if (string.IsNullOrWhiteSpace(resolvedFolderId))
        {
            return null;
        }

        var entries = await _folderContentsReader
            .GetFolderContentsAsync(normalizedProjectId, resolvedFolderId, cancellationToken)
            .ConfigureAwait(false);

        return new AccFolderBrowseResult(normalizedProjectId, resolvedFolderId, entries);
    }

    private static string NormalizeProjectId(string projectId)
    {
        var trimmed = projectId.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"b.{trimmed}";
    }
}
