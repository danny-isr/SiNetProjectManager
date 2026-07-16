using Microsoft.EntityFrameworkCore;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// EF-backed <see cref="IProjectDriveFolderResolver"/>. Mirrors the folder-walk half of the legacy
/// <c>GoogleDriveStore.ResolveFolderHandleAsync</c>: project root name (last segment of the
/// file-server project path) plus <c>ProjectFolder</c> titles up to the synthetic project root.
/// </summary>
public sealed class ProjectDriveFolderResolver : IProjectDriveFolderResolver
{
    private const string ProjectRootFolderTitle = "\u05EA\u05D9\u05E7\u05D9\u05EA \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8";
    private const int MaxDepth = 32;

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IFileServerRootResolver _rootResolver;

    public ProjectDriveFolderResolver(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IFileServerRootResolver rootResolver)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(rootResolver);
        _dbFactory = dbFactory;
        _rootResolver = rootResolver;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> ResolveRelativeSegmentsAsync(
        int projectId,
        int projectFolderId,
        CancellationToken cancellationToken = default)
    {
        if (projectFolderId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var projectRoot = await _rootResolver.ResolveAsync(db, projectId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(projectRoot))
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
            {
                if (!foundLeaf)
                    return null;
                break;
            }

            foundLeaf = true;

            if (folder.Title == ProjectRootFolderTitle)
                break;

            if (!string.IsNullOrWhiteSpace(folder.Title))
                segments.Add(SanitizeFolderName(folder.Title!));

            if (!folder.Infolderid.HasValue || folder.Infolderid.Value == folder.Id)
                break;
            currentId = folder.Infolderid.Value;
        }

        segments.Reverse();

        var projectRootName = SanitizeFolderName(GetLastPathSegment(projectRoot));
        var all = new List<string>(segments.Count + 1) { projectRootName };
        all.AddRange(segments);
        return all;
    }

    private static string SanitizeFolderName(string name)
    {
        var trimmed = name.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrEmpty(trimmed))
            return "_";

        var sb = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
            sb.Append(char.IsControl(ch) ? '_' : ch);
        return sb.ToString();
    }

    private static string GetLastPathSegment(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('\\', '/');
        var lastSlash = trimmed.LastIndexOfAny(['\\', '/']);
        return lastSlash < 0 ? trimmed : trimmed[(lastSlash + 1)..];
    }
}
