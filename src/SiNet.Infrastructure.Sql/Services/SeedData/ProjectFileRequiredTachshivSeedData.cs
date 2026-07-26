using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Idempotent upsert of required catalog slot(s) for «תחשיב» under <see cref="ProjectFile"/>.
/// <para>
/// <c>uc_Title</c> is globally unique, so each <c>TypeProjId</c> gets a distinct catalog title
/// (<c>תחשיב</c> / <c>תחשיב · {id}</c>). Display name in ProjectWork is normalized to «תחשיב».
/// </para>
/// </summary>
public static class ProjectFileRequiredTachshivSeedData
{
    public const string DisplayTitle = "\u05EA\u05D7\u05E9\u05D9\u05D1"; // תחשיב
    public const string DefaultTypeFile = ".xlsx";

    /// <summary>
    /// Ensures one required «תחשיב» <see cref="ProjectFile"/> per distinct project type that appears
    /// in <c>TypeOfProjectInProjects</c> (fallback: all <see cref="JobType"/> rows). Returns a short summary.
    /// </summary>
    public static async Task<string> EnsureAsync(SiNetSQLDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var typeIds = await db.TypeOfProjectInProjects.AsNoTracking()
            .Where(t => t.ProjectTypeId != null)
            .Select(t => t.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (typeIds.Count == 0)
        {
            typeIds = await db.JobTypes.AsNoTracking()
                .Select(j => j.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        if (typeIds.Count == 0)
            return "Tachshiv catalog: skipped (no JobType / TypeOfProjectInProjects rows).";

        var folderId = await ResolveFolderIdAsync(db, ct).ConfigureAwait(false);
        if (folderId is null)
            return "Tachshiv catalog: skipped (no ProjectFolder available).";

        var maxNumber = await db.ProjectFiles
            .Where(f => f.Number != null)
            .Select(f => (float?)f.Number)
            .MaxAsync(ct)
            .ConfigureAwait(false);
        var nextNumber = (maxNumber ?? 0f) + 1f;

        var existing = await db.ProjectFiles
            .Where(f => f.Title != null && f.Title.StartsWith(DisplayTitle))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;

        foreach (var typeId in typeIds.OrderBy(id => id))
        {
            var catalogTitle = CatalogTitleFor(typeId, typeIds.Count);
            var row = existing.FirstOrDefault(f => f.TypeProjId == typeId)
                ?? existing.FirstOrDefault(f => string.Equals(f.Title, catalogTitle, StringComparison.Ordinal));

            if (row is null)
            {
                // Prefer a free Number for this TypeProjId (unique on Number+TypeProjId).
                var numberForType = nextNumber;
                while (await db.ProjectFiles.AnyAsync(
                           f => f.TypeProjId == typeId && f.Number == numberForType, ct)
                       .ConfigureAwait(false))
                {
                    numberForType += 1f;
                }

                row = new ProjectFile
                {
                    Title = catalogTitle,
                    Number = numberForType,
                    Folderid = folderId,
                    Typefile = DefaultTypeFile,
                    TypeProjId = typeId,
                    IsRequired = true,
                    StorageDestination = FileStorageDestination.FileServer,
                    Created = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                };
                db.ProjectFiles.Add(row);
                existing.Add(row);
                inserted++;
                nextNumber = Math.Max(nextNumber, numberForType + 1f);
            }
            else
            {
                var changed = false;
                if (!string.Equals(row.Title, catalogTitle, StringComparison.Ordinal))
                {
                    row.Title = catalogTitle;
                    changed = true;
                }

                if (row.TypeProjId != typeId)
                {
                    row.TypeProjId = typeId;
                    changed = true;
                }

                if (row.Folderid is null)
                {
                    row.Folderid = folderId;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(row.Typefile))
                {
                    row.Typefile = DefaultTypeFile;
                    changed = true;
                }

                if (!row.IsRequired)
                {
                    row.IsRequired = true;
                    changed = true;
                }

                if (changed)
                {
                    row.Modified = DateTime.UtcNow;
                    updated++;
                }
            }
        }

        if (inserted > 0 || updated > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return $"Tachshiv catalog: inserted={inserted}, updated={updated}, types={typeIds.Count}.";
    }

    /// <summary>Unique catalog title respecting <c>uc_Title</c>.</summary>
    public static string CatalogTitleFor(int typeProjId, int totalTypes) =>
        totalTypes <= 1 ? DisplayTitle : $"{DisplayTitle} \u00B7 {typeProjId}";

    /// <summary>True when a catalog title represents the תחשיב required slot.</summary>
    public static bool IsTachshivCatalogTitle(string? title) =>
        !string.IsNullOrEmpty(title)
        && title.StartsWith(DisplayTitle, StringComparison.Ordinal);

    private static async Task<int?> ResolveFolderIdAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        // Prefer a folder already used by document-like catalog files.
        var fromFiles = await db.ProjectFiles.AsNoTracking()
            .Where(f => f.Folderid != null)
            .GroupBy(f => f.Folderid!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => (int?)g.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (fromFiles is > 0)
            return fromFiles;

        const string projectRootTitle = "\u05EA\u05D9\u05E7\u05D9\u05EA \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8"; // תיקיית הפרויקט
        var syntheticRootIds = await db.ProjectFolders.AsNoTracking()
            .Where(f => f.Title == projectRootTitle)
            .Select(f => f.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (syntheticRootIds.Count > 0)
        {
            var firstChild = await db.ProjectFolders.AsNoTracking()
                .Where(f => f.Infolderid != null && syntheticRootIds.Contains(f.Infolderid.Value))
                .OrderBy(f => f.Title)
                .Select(f => (int?)f.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (firstChild is > 0)
                return firstChild;
        }

        return await db.ProjectFolders.AsNoTracking()
            .OrderBy(f => f.Id)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
