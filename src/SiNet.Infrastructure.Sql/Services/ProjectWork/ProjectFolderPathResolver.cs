using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// EF-backed <see cref="IProjectFolderPathResolver"/>. Reproduces the folder-resolution half of the
/// legacy <c>FileServerStore.ResolveFolderHandleAsync</c>: it combines the project's file-server root
/// (via <see cref="IFileServerRootResolver"/>) with the folder-title segments walked up the folder
/// hierarchy, stopping at the synthetic project-root folder.
/// </summary>
public sealed class ProjectFolderPathResolver : IProjectFolderPathResolver
{
    private const int MaxDepth = 32;

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IFileServerRootResolver _rootResolver;

    public ProjectFolderPathResolver(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IFileServerRootResolver rootResolver)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(rootResolver);
        _dbFactory = dbFactory;
        _rootResolver = rootResolver;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveFileServerFolderPathAsync(int projectId, int projectFolderId, CancellationToken cancellationToken = default)
    {
        // Synthetic / user-created folders use non-positive ids that don't exist in the DB.
        if (projectFolderId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var root = await _rootResolver.ResolveAsync(db, projectId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var segments = new List<string>();
        var currentId = projectFolderId;
        var foundLeaf = false;

        for (var safety = 0; safety < MaxDepth; safety++)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            var folder = await db.ProjectFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == currentId, cancellationToken)
                .ConfigureAwait(false);
            if (folder is null)
                return foundLeaf ? BuildPath(root, segments) : null;

            foundLeaf = true;

            // The synthetic DB root is not present on disk; stop climbing here.
            if (ProjectFolderTitles.IsProjectRoot(folder.Title))
                break;

            if (!string.IsNullOrWhiteSpace(folder.Title))
                segments.Add(folder.Title!);

            if (!folder.Infolderid.HasValue || folder.Infolderid.Value == folder.Id)
                break;
            currentId = folder.Infolderid.Value;
        }

        return BuildPath(root, segments);
    }

    private static string BuildPath(string root, List<string> segments)
    {
        segments.Reverse();
        var full = root;
        foreach (var s in segments)
            full = Path.Combine(full, s);
        return full;
    }
}
