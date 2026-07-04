using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccProjectTreeSearchService(
    LocalAccFolderBrowserService folderBrowserService) : IAccProjectTreeSearchService
{
    private const string RootBrowseLabel = "Project Files";
    private const int MaxTreeSearchFolders = 250;
    private const int MaxTreeSearchResults = 50;

    private readonly LocalAccFolderBrowserService _folderBrowserService = folderBrowserService;

    public async Task<AccProjectTreeSearchResult> SearchAsync(
        string projectId,
        string fileName,
        string? folderId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(fileName))
        {
            return new AccProjectTreeSearchResult([], 0, false, false);
        }

        var query = fileName.Trim();
        var pendingFolders = new Queue<AccTreeSearchLocation>();
        var visitedFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<AccProjectTreeSearchMatch>();
        var visitedFolderCount = 0;
        pendingFolders.Enqueue(new AccTreeSearchLocation(
            string.IsNullOrWhiteSpace(folderId) ? null : folderId.Trim(),
            string.IsNullOrWhiteSpace(folderId) ? RootBrowseLabel : folderId!.Trim()));

        while (pendingFolders.Count > 0 && visitedFolderCount < MaxTreeSearchFolders && matches.Count < MaxTreeSearchResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentLocation = pendingFolders.Dequeue();
            var browseResult = await _folderBrowserService
                .BrowseAsync(projectId, currentLocation.FolderId, cancellationToken)
                .ConfigureAwait(false);

            if (browseResult is null || !visitedFolderIds.Add(browseResult.FolderId))
            {
                continue;
            }

            visitedFolderCount++;

            foreach (var entry in browseResult.Entries.Where(static entry => entry.Kind == AccFolderEntryKind.Item))
            {
                if (!entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(new AccProjectTreeSearchMatch(
                    browseResult.ProjectId,
                    browseResult.FolderId,
                    currentLocation.FolderPath,
                    entry.DisplayName));

                if (matches.Count >= MaxTreeSearchResults)
                {
                    break;
                }
            }

            if (matches.Count >= MaxTreeSearchResults || visitedFolderCount >= MaxTreeSearchFolders)
            {
                break;
            }

            foreach (var entry in browseResult.Entries.Where(static entry => entry.Kind == AccFolderEntryKind.Folder))
            {
                if (!visitedFolderIds.Contains(entry.Id))
                {
                    pendingFolders.Enqueue(new AccTreeSearchLocation(
                        entry.Id,
                        BuildChildPath(currentLocation.FolderPath, entry.DisplayName)));
                }
            }
        }

        return new AccProjectTreeSearchResult(
            matches,
            visitedFolderCount,
            pendingFolders.Count > 0 && visitedFolderCount >= MaxTreeSearchFolders,
            pendingFolders.Count > 0 && matches.Count >= MaxTreeSearchResults);
    }

    private static string BuildChildPath(string parentPath, string folderName)
    {
        var normalizedFolderName = folderName.Trim();
        return string.IsNullOrWhiteSpace(parentPath)
            ? normalizedFolderName
            : $"{parentPath} / {normalizedFolderName}";
    }

    private sealed record AccTreeSearchLocation(string? FolderId, string FolderPath);
}
