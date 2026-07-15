using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Default <see cref="IFolderPathResolver"/>: walks up <c>ProjectFolder.Infolderid</c> from the
/// slot's folder to the root, excluding the root container node. Native port of the legacy resolver.
/// </summary>
public sealed class FolderPathResolver : IFolderPathResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(
        SiNetSQLDbContext db,
        int projectFileId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var projectFile = await db.ProjectFiles
            .AsNoTracking()
            .Include(pf => pf.Folder)
            .FirstOrDefaultAsync(pf => pf.Id == projectFileId, ct);

        if (projectFile?.Folder == null)
            return Array.Empty<string>();

        var segments = new List<string>();
        var visited = new HashSet<int>();
        var current = projectFile.Folder;

        while (current != null && visited.Add(current.Id))
        {
            if (!current.Infolderid.HasValue || current.Infolderid.Value == current.Id)
                break;

            segments.Add(current.Title);

            current = await db.ProjectFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == current.Infolderid.Value, ct);
        }

        segments.Reverse();
        return segments;
    }
}
