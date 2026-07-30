using Microsoft.EntityFrameworkCore;
using SiNet.Application.ProjectWork;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// Creates <see cref="ProjectFolder"/> rows under an existing parent. Optionally creates the matching
/// FileServer directory when a path can be resolved for the project.
/// </summary>
internal sealed class SqlProjectFolderWriteService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IProjectFolderPathResolver folderPathResolver) : IProjectFolderWriteService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IProjectFolderPathResolver _folderPathResolver =
        folderPathResolver ?? throw new ArgumentNullException(nameof(folderPathResolver));

    public async Task<CreateProjectFolderResult> CreateChildFolderAsync(
        int parentFolderId,
        string folderTitle,
        int? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (parentFolderId <= 0)
            return CreateProjectFolderResult.Fail("תיקיית האב אינה תקפה.");

        var title = (folderTitle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
            return CreateProjectFolderResult.Fail("יש להזין שם תיקייה.");

        if (title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return CreateProjectFolderResult.Fail("שם התיקייה מכיל תווים לא חוקיים.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var parent = await db.ProjectFolders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == parentFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (parent is null)
            return CreateProjectFolderResult.Fail("תיקיית האב לא נמצאה.");

        var duplicate = await db.ProjectFolders
            .AsNoTracking()
            .AnyAsync(
                f => f.Infolderid == parentFolderId && f.Title == title,
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
            return CreateProjectFolderResult.Fail($"כבר קיימת תיקייה בשם '{title}' תחת אותה תיקיית אב.");

        var folder = new ProjectFolder
        {
            Title = title,
            Infolderid = parentFolderId,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        db.ProjectFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (projectId is int pid and > 0)
        {
            try
            {
                var parentPath = await _folderPathResolver
                    .ResolveFileServerFolderPathAsync(pid, parentFolderId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(parentPath))
                {
                    var dest = Path.Combine(parentPath, title);
                    Directory.CreateDirectory(dest);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // DB row is the catalog source of truth; disk is best-effort (same idea as V2).
                System.Diagnostics.Trace.TraceWarning(
                    $"[ProjectFolderWrite] Disk folder create failed under parent={parentFolderId}: {ex.Message}");
            }
        }

        return CreateProjectFolderResult.Ok(folder.Id);
    }
}
