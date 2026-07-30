using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.ProjectWork;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Bootstrap definitions for curated <see cref="ProjectFile"/> catalog slots.
/// Reconcile by <see cref="ProjectFileCatalogDefinition.Code"/>; never deletes rows.
/// Title/Number may change after insert only for known catalog aliases.
/// </summary>
public static class ProjectFileCatalogSeedData
{
    public static readonly ProjectFileCatalogDefinition[] Definitions =
    [
        new ProjectFileCatalogDefinition(
            Code: ProjectFileCatalogCodes.QuoteEstimate,
            DefaultTitle: "\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05D4", // אומדן הצעה
            JobTypeTitle: SqlProjectCreateService.DefaultJobTypeTitle, // חומר כללי
            FolderTitle: "\u05E0\u05D9\u05D4\u05D5\u05DC \u05DB\u05E1\u05E4\u05D9", // ניהול כספי
            ParentFolderTitle: "\u05EA\u05DB\u05EA\u05D5\u05D1\u05EA", // תכתובת
            TypeFile: ".xlsx",
            IsRequired: true,
            LegacyTitles:
            [
                "\u05EA\u05D7\u05E9\u05D9\u05D1", // תחשיב
                "\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", // אומדן הצעת מחיר
                "\u05D0\u05D5\u05DE\u05D3\u05DF \u05EA\u05DB\u05E0\u05D5\u05DF", // אומדן תכנון (spoken/legacy alias)
            ]),
    ];

    public sealed record ProjectFileCatalogDefinition(
        string Code,
        string DefaultTitle,
        string JobTypeTitle,
        string FolderTitle,
        string ParentFolderTitle,
        string TypeFile,
        bool IsRequired,
        IReadOnlyList<string> LegacyTitles);

    /// <summary>
    /// Ensures every catalog definition exists and is linked by <c>Code</c>.
    /// Never deletes ProjectFile / ProjectFolder rows. Does not overwrite an arbitrary admin Title rename.
    /// Does <b>not</b> create «הצעת מחיר». Target folder is «ניהול כספי» under «תכתובת».
    /// </summary>
    public static async Task<string> EnsureAsync(SiNetSQLDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await ProjectFileSchemaPatches.EnsureCatalogColumnsAsync(db, ct).ConfigureAwait(false);

        var parts = new List<string>();
        foreach (var def in Definitions)
            parts.Add(await EnsureOneAsync(db, def, ct).ConfigureAwait(false));

        return string.Join(" ", parts);
    }

    /// <summary>Sync wrapper for V2 startup seeding.</summary>
    public static string Ensure(SiNetSQLDbContext db)
        => EnsureAsync(db).GetAwaiter().GetResult();

    public static bool IsKnownCatalogCode(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && Definitions.Any(d => string.Equals(d.Code, code, StringComparison.Ordinal));

    public static string? DefaultTitleForCode(string? code) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.Ordinal))?.DefaultTitle;

    private static async Task<string> EnsureOneAsync(
        SiNetSQLDbContext db,
        ProjectFileCatalogDefinition def,
        CancellationToken ct)
    {
        // Prefer legacy id 9 (same as project-create default), then title match.
        var jobType = await db.JobTypes.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == SqlProjectCreateService.LegacyDefaultJobTypeId, ct)
            .ConfigureAwait(false);
        if (jobType is null
            || !string.Equals(jobType.Title, def.JobTypeTitle, StringComparison.Ordinal))
        {
            jobType = await db.JobTypes.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Title == def.JobTypeTitle, ct)
                .ConfigureAwait(false);
        }

        if (jobType is null)
            return $"[{def.Code}] skipped (JobType '{def.JobTypeTitle}' / id {SqlProjectCreateService.LegacyDefaultJobTypeId} not found).";

        var folderId = await EnsureFolderUnderParentAsync(
                db,
                def.FolderTitle,
                def.ParentFolderTitle,
                ct)
            .ConfigureAwait(false);
        if (folderId is null)
            return $"[{def.Code}] skipped (folder '{def.FolderTitle}' under '{def.ParentFolderTitle}' not found; create parent «{def.ParentFolderTitle}» first — seed does not create «הצעת מחיר»).";

        var typeId = jobType.Id;
        var knownTitles = new HashSet<string>(def.LegacyTitles, StringComparer.Ordinal) { def.DefaultTitle };

        var byCode = await db.ProjectFiles
            .FirstOrDefaultAsync(f => f.Code == def.Code, ct)
            .ConfigureAwait(false);

        if (byCode is null)
        {
            // Attach Code to a legacy row for this job type (exact default title or legacy aliases).
            var candidates = await db.ProjectFiles
                .Where(f => f.TypeProjId == typeId && f.Title != null)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            byCode = candidates.FirstOrDefault(f => knownTitles.Contains(f.Title!))
                ?? candidates.FirstOrDefault(f => f.IsRequired && f.Code == null);
        }

        if (byCode is null)
        {
            var maxForType = await db.ProjectFiles
                .Where(f => f.TypeProjId == typeId && f.Number != null)
                .Select(f => (float?)f.Number)
                .MaxAsync(ct)
                .ConfigureAwait(false);
            var globalMax = await db.ProjectFiles
                .Where(f => f.Number != null)
                .Select(f => (float?)f.Number)
                .MaxAsync(ct)
                .ConfigureAwait(false);
            var nextNumber = Math.Max(maxForType ?? 0f, globalMax ?? 0f) + 1f;
            while (await db.ProjectFiles.AnyAsync(
                       f => f.TypeProjId == typeId && f.Number == nextNumber, ct)
                   .ConfigureAwait(false))
            {
                nextNumber += 1f;
            }

            var sameTypeTitle = await db.ProjectFiles
                .FirstOrDefaultAsync(f => f.Title == def.DefaultTitle && f.TypeProjId == typeId, ct)
                .ConfigureAwait(false);
            if (sameTypeTitle is not null)
            {
                byCode = sameTypeTitle;
            }
            else
            {
                db.ProjectFiles.Add(new ProjectFile
                {
                    Code = def.Code,
                    Title = def.DefaultTitle,
                    Number = nextNumber,
                    Folderid = folderId,
                    Typefile = def.TypeFile,
                    TypeProjId = typeId,
                    IsRequired = def.IsRequired,
                    StorageDestination = FileStorageDestination.FileServer,
                    Created = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return $"[{def.Code}] inserted Number={nextNumber} FolderId={folderId} Title='{def.DefaultTitle}' JobTypeId={typeId} Folder='{def.FolderTitle}'.";
            }
        }

        var changed = false;
        if (!string.Equals(byCode.Code, def.Code, StringComparison.Ordinal))
        {
            byCode.Code = def.Code;
            changed = true;
        }

        if (byCode.TypeProjId != typeId)
        {
            byCode.TypeProjId = typeId;
            changed = true;
        }

        if (byCode.Folderid != folderId)
        {
            byCode.Folderid = folderId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(byCode.Typefile))
        {
            byCode.Typefile = def.TypeFile;
            changed = true;
        }

        if (def.IsRequired && !byCode.IsRequired)
        {
            byCode.IsRequired = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(byCode.Title))
        {
            byCode.Title = def.DefaultTitle;
            changed = true;
        }
        else if (knownTitles.Contains(byCode.Title)
                 && !string.Equals(byCode.Title, def.DefaultTitle, StringComparison.Ordinal))
        {
            byCode.Title = def.DefaultTitle;
            changed = true;
        }

        if (changed)
        {
            byCode.Modified = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return $"[{def.Code}] updated Id={byCode.Id} Title='{byCode.Title}' FolderId={folderId} Folder='{def.FolderTitle}'.";
        }

        return $"[{def.Code}] unchanged Id={byCode.Id} Number={byCode.Number} Title='{byCode.Title}' Folder='{def.FolderTitle}'.";
    }

    /// <summary>
    /// Resolves <paramref name="folderTitle"/> under <paramref name="parentFolderTitle"/>.
    /// Re-parents an existing folder when it is not under the expected parent.
    /// Creates the child folder only when the parent already exists — never creates «הצעת מחיר»
    /// and never invents the parent «תכתובת».
    /// </summary>
    private static async Task<int?> EnsureFolderUnderParentAsync(
        SiNetSQLDbContext db,
        string folderTitle,
        string parentFolderTitle,
        CancellationToken ct)
    {
        var parent = await db.ProjectFolders
            .AsNoTracking()
            .Where(f => f.Title == parentFolderTitle)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (parent is null)
            return null;

        var existing = await db.ProjectFolders
            .Where(f => f.Title == folderTitle)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.Infolderid != parent.Id && existing.Id != parent.Id)
            {
                existing.Infolderid = parent.Id;
                existing.Modified = DateTime.UtcNow;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return existing.Id;
        }

        var folder = new ProjectFolder
        {
            Title = folderTitle,
            Infolderid = parent.Id,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        db.ProjectFolders.Add(folder);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return folder.Id;
    }
}
