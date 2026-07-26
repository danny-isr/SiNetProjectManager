using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.ProjectWork;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Bootstrap definitions for curated <see cref="ProjectFile"/> catalog slots.
/// Reconcile by <see cref="ProjectFileCatalogDefinition.Code"/>; Title/Number may change after insert.
/// </summary>
public static class ProjectFileCatalogSeedData
{
    public static readonly ProjectFileCatalogDefinition[] Definitions =
    [
        new ProjectFileCatalogDefinition(
            Code: ProjectFileCatalogCodes.QuoteEstimate,
            DefaultTitle: "\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", // אומדן הצעת מחיר
            JobTypeTitle: SqlProjectCreateService.DefaultJobTypeTitle, // חומר כללי
            FolderTitle: "\u05E0\u05D9\u05D4\u05D5\u05DC \u05DB\u05E1\u05E4\u05D9", // ניהול כספי
            TypeFile: ".xlsx",
            IsRequired: true,
            LegacyTitles: ["\u05EA\u05D7\u05E9\u05D9\u05D1"]), // תחשיב
    ];

    public sealed record ProjectFileCatalogDefinition(
        string Code,
        string DefaultTitle,
        string JobTypeTitle,
        string FolderTitle,
        string TypeFile,
        bool IsRequired,
        IReadOnlyList<string> LegacyTitles);

    /// <summary>
    /// Ensures every catalog definition exists and is linked by <c>Code</c>.
    /// Does not overwrite a non-empty Title that already differs from <see cref="ProjectFileCatalogDefinition.DefaultTitle"/>.
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
        var jobType = await db.JobTypes.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Title == def.JobTypeTitle, ct)
            .ConfigureAwait(false);
        if (jobType is null)
            return $"[{def.Code}] skipped (JobType '{def.JobTypeTitle}' not found).";

        var folderId = await db.ProjectFolders.AsNoTracking()
            .Where(f => f.Title == def.FolderTitle)
            .OrderBy(f => f.Id)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (folderId is null)
            return $"[{def.Code}] skipped (folder '{def.FolderTitle}' not found).";

        var typeId = jobType.Id;

        var byCode = await db.ProjectFiles
            .FirstOrDefaultAsync(f => f.Code == def.Code, ct)
            .ConfigureAwait(false);

        if (byCode is null)
        {
            // Attach Code to a legacy row for this job type (exact default title or legacy aliases).
            var legacyTitles = new HashSet<string>(def.LegacyTitles, StringComparer.Ordinal) { def.DefaultTitle };
            var candidates = await db.ProjectFiles
                .Where(f => f.TypeProjId == typeId && f.Title != null)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            byCode = candidates.FirstOrDefault(f => legacyTitles.Contains(f.Title!))
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

            var titleTaken = await db.ProjectFiles.AsNoTracking()
                .AnyAsync(f => f.Title == def.DefaultTitle, ct)
                .ConfigureAwait(false);
            if (titleTaken)
                return $"[{def.Code}] skipped (Title '{def.DefaultTitle}' already used).";

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
            return $"[{def.Code}] inserted Number={nextNumber} FolderId={folderId}.";
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

        // Only fill Title when empty — never overwrite an admin rename.
        if (string.IsNullOrWhiteSpace(byCode.Title))
        {
            byCode.Title = def.DefaultTitle;
            changed = true;
        }

        if (changed)
        {
            byCode.Modified = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return $"[{def.Code}] updated Id={byCode.Id}.";
        }

        return $"[{def.Code}] unchanged Id={byCode.Id} Number={byCode.Number}.";
    }
}
