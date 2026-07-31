using Microsoft.EntityFrameworkCore;
using SiNet.Application.FileCatalog;
using SiNet.Infrastructure.Sql.Services.ProjectWork;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SqlDest = SiNetSQL.Models.FileStorageDestination;

namespace SiNet.Infrastructure.Sql.Services.FileCatalog;

internal sealed class SqlFileCatalogWriteService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IFileCatalogWriteService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<FileCatalogWriteResult> CreateJobTypeAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return FileCatalogWriteResult.Fail("יש להזין שם לסוג עבודה.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var exists = await db.JobTypes.AsNoTracking()
            .AnyAsync(j => j.Title == trimmed, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return FileCatalogWriteResult.Fail($"כבר קיים סוג עבודה בשם '{trimmed}'.");

        var row = new JobType { Title = trimmed, Created = DateTime.UtcNow };
        db.JobTypes.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok(row.Id);
    }

    public async Task<FileCatalogWriteResult> RenameJobTypeAsync(
        int jobTypeId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (jobTypeId <= 0)
            return FileCatalogWriteResult.Fail("סוג העבודה אינו תקף.");

        var trimmed = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return FileCatalogWriteResult.Fail("יש להזין שם לסוג עבודה.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.JobTypes.FirstOrDefaultAsync(j => j.Id == jobTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return FileCatalogWriteResult.Fail("סוג העבודה לא נמצא.");

        row.Title = trimmed;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok(row.Id);
    }

    public async Task<FileCatalogWriteResult> CreateFolderAsync(
        int parentFolderId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (parentFolderId <= 0)
            return FileCatalogWriteResult.Fail("תיקיית האב אינה תקפה.");

        var trimmed = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return FileCatalogWriteResult.Fail("יש להזין שם תיקייה.");

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return FileCatalogWriteResult.Fail("שם התיקייה מכיל תווים לא חוקיים.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var parentExists = await db.ProjectFolders.AsNoTracking()
            .AnyAsync(f => f.Id == parentFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (!parentExists)
            return FileCatalogWriteResult.Fail("תיקיית האב לא נמצאה.");

        var duplicate = await db.ProjectFolders.AsNoTracking()
            .AnyAsync(f => f.Infolderid == parentFolderId && f.Title == trimmed, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
            return FileCatalogWriteResult.Fail($"כבר קיימת תיקייה בשם '{trimmed}' תחת אותה תיקיית אב.");

        var folder = new ProjectFolder
        {
            Title = trimmed,
            Infolderid = parentFolderId,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        db.ProjectFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok(folder.Id);
    }

    public async Task<FileCatalogWriteResult> DeleteFolderAsync(
        int folderId,
        CancellationToken cancellationToken = default)
    {
        if (folderId <= 0)
            return FileCatalogWriteResult.Fail("התיקייה אינה תקפה.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var folder = await db.ProjectFolders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            .ConfigureAwait(false);
        if (folder is null)
            return FileCatalogWriteResult.Fail("התיקייה לא נמצאה.");

        if (ProjectFolderTitles.IsProjectRoot(folder.Title))
            return FileCatalogWriteResult.Fail("לא ניתן למחוק את תיקיית השורש של הפרויקט.");

        var hasChildFolders = await db.ProjectFolders.AsNoTracking()
            .AnyAsync(f => f.Infolderid == folderId, cancellationToken)
            .ConfigureAwait(false);
        if (hasChildFolders)
            return FileCatalogWriteResult.Fail("לא ניתן למחוק תיקייה שמכילה תיקיות משנה. רוקן אותה קודם.");

        var hasFiles = await db.ProjectFiles.AsNoTracking()
            .AnyAsync(f => f.Folderid == folderId, cancellationToken)
            .ConfigureAwait(false);
        if (hasFiles)
            return FileCatalogWriteResult.Fail("לא ניתן למחוק תיקייה שמכילה הגדרות קבצים. העבר או מחק אותן קודם.");

        db.ProjectFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok(folderId);
    }

    public async Task<FileCatalogWriteResult> CreateFileAsync(
        int folderId,
        int jobTypeId,
        CancellationToken cancellationToken = default)
    {
        if (folderId <= 0)
            return FileCatalogWriteResult.Fail("יש לבחור תיקייה.");
        if (jobTypeId <= 0)
            return FileCatalogWriteResult.Fail("יש לבחור סוג עבודה (לא «הכל»).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var folderOk = await db.ProjectFolders.AsNoTracking()
            .AnyAsync(f => f.Id == folderId, cancellationToken)
            .ConfigureAwait(false);
        if (!folderOk)
            return FileCatalogWriteResult.Fail("התיקייה לא נמצאה.");

        var jobOk = await db.JobTypes.AsNoTracking()
            .AnyAsync(j => j.Id == jobTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!jobOk)
            return FileCatalogWriteResult.Fail("סוג העבודה לא נמצא.");

        var maxForType = await db.ProjectFiles
            .Where(f => f.TypeProjId == jobTypeId && f.Number != null)
            .Select(f => (float?)f.Number)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        var nextNum = (maxForType ?? 0f) + 1f;

        var file = new ProjectFile
        {
            Title = "קובץ חדש",
            Number = nextNum,
            TypeProjId = jobTypeId,
            Folderid = folderId,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            StorageDestination = SqlDest.FileServer,
        };
        db.ProjectFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok(file.Id);
    }

    public async Task<FileCatalogWriteResult> DeleteFileAsync(
        int fileId,
        CancellationToken cancellationToken = default)
    {
        if (fileId <= 0)
            return FileCatalogWriteResult.Fail("הקובץ אינו תקף.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
            return FileCatalogWriteResult.Fail("הקובץ לא נמצא.");

        // Catalog Code rows may be deleted (UI confirms). Restore via Seed בסיסי.
        try
        {
            db.ProjectFiles.Remove(file);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return FileCatalogWriteResult.Ok();
        }
        catch (DbUpdateException ex)
        {
            return FileCatalogWriteResult.Fail(
                "מחיקה נכשלה (ייתכן שיש הפניות לקובץ): " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task<FileCatalogWriteResult> AssignFileToFolderAsync(
        int fileId,
        int folderId,
        CancellationToken cancellationToken = default)
    {
        if (fileId <= 0 || folderId <= 0)
            return FileCatalogWriteResult.Fail("בחירת קובץ/תיקייה אינה תקפה.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
            return FileCatalogWriteResult.Fail("הקובץ לא נמצא.");

        var folderOk = await db.ProjectFolders.AsNoTracking()
            .AnyAsync(f => f.Id == folderId, cancellationToken)
            .ConfigureAwait(false);
        if (!folderOk)
            return FileCatalogWriteResult.Fail("התיקייה לא נמצאה.");

        file.Folderid = folderId;
        file.Modified = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok(file.Id);
    }

    public async Task<FileCatalogWriteResult> SaveFileEditsAsync(
        IReadOnlyList<FileCatalogFileEditDto> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
            return FileCatalogWriteResult.Ok();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var ids = edits.Select(e => e.FileId).Distinct().ToList();
        var rows = await db.ProjectFiles
            .Where(f => ids.Contains(f.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byId = rows.ToDictionary(f => f.Id);

        foreach (var edit in edits)
        {
            if (!byId.TryGetValue(edit.FileId, out var row))
                return FileCatalogWriteResult.Fail($"קובץ Id={edit.FileId} לא נמצא.");

            var title = edit.Title?.Trim();
            if (!string.IsNullOrEmpty(title) && title.Length > 24)
                title = title[..24];

            row.Title = title;
            row.Typefile = string.IsNullOrWhiteSpace(edit.Typefile) ? null : edit.Typefile.Trim();
            row.LookAtDes = edit.LookAtDes;
            row.OutSidData = edit.OutSidData;
            row.StorageDestination = (SqlDest)(int)edit.StorageDestination;
            row.TemplateLocation = string.IsNullOrWhiteSpace(edit.TemplateLocation)
                ? null
                : edit.TemplateLocation.Trim();
            row.Des = edit.Description;
            row.IsRequired = edit.IsRequired;
            row.Modified = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FileCatalogWriteResult.Ok();
    }
}
