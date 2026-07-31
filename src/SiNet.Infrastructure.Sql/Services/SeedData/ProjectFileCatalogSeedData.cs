using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.ProjectWork;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Bootstrap definitions for curated <see cref="ProjectFile"/> catalog slots.
/// Reconcile by <see cref="ProjectFileCatalogDefinition.Code"/>.
/// Catalog titles use underscore instead of space; space forms are aliases only.
/// Never overwrites <see cref="ProjectFile.TemplateLocation"/>.
/// </summary>
public static class ProjectFileCatalogSeedData
{
    public static readonly ProjectFileCatalogDefinition[] Definitions =
    [
        new ProjectFileCatalogDefinition(
            Code: ProjectFileCatalogCodes.QuoteEstimate,
            DefaultTitle: "\u05D0\u05D5\u05DE\u05D3\u05DF_\u05D4\u05E6\u05E2\u05D4", // אומדן_הצעה
            JobTypeTitle: SqlProjectCreateService.DefaultJobTypeTitle, // חומר כללי
            FolderTitle: "\u05E0\u05D9\u05D4\u05D5\u05DC_\u05DB\u05E1\u05E4\u05D9", // ניהול_כספי
            ParentFolderTitle: "\u05EA\u05DB\u05EA\u05D5\u05D1\u05EA", // תכתובת
            TypeFile: ".xlsx",
            IsRequired: true,
            OutSidData: null,
            LegacyTitles:
            [
                "\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05D4", // אומדן הצעה (space alias)
                "\u05EA\u05D7\u05E9\u05D9\u05D1", // תחשיב
                "\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", // אומדן הצעת מחיר
                "\u05D0\u05D5\u05DE\u05D3\u05DF_\u05D4\u05E6\u05E2\u05EA_\u05DE\u05D7\u05D9\u05E8", // אומדן_הצעת_מחיר
                "\u05D0\u05D5\u05DE\u05D3\u05DF \u05EA\u05DB\u05E0\u05D5\u05DF", // אומדן תכנון
            ]),
        new ProjectFileCatalogDefinition(
            Code: ProjectFileCatalogCodes.QuoteDocument,
            DefaultTitle: "\u05D4\u05E6\u05E2\u05EA_\u05DE\u05D7\u05D9\u05E8", // הצעת_מחיר
            JobTypeTitle: SqlProjectCreateService.DefaultJobTypeTitle,
            FolderTitle: "\u05E0\u05D9\u05D4\u05D5\u05DC_\u05DB\u05E1\u05E4\u05D9", // ניהול_כספי
            ParentFolderTitle: "\u05EA\u05DB\u05EA\u05D5\u05D1\u05EA", // תכתובת
            TypeFile: ".docx",
            IsRequired: true,
            OutSidData: false,
            LegacyTitles:
            [
                "\u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", // הצעת מחיר (space alias)
            ]),
        new ProjectFileCatalogDefinition(
            Code: ProjectFileCatalogCodes.QuoteClientApproval,
            DefaultTitle: "\u05D0\u05D9\u05E9\u05D5\u05E8_\u05DC\u05E7\u05D5\u05D7_\u05DC\u05D4\u05E6\u05E2\u05D4", // אישור_לקוח_להצעה
            JobTypeTitle: SqlProjectCreateService.DefaultJobTypeTitle,
            FolderTitle: "\u05E0\u05D9\u05D4\u05D5\u05DC_\u05DB\u05E1\u05E4\u05D9", // ניהול_כספי
            ParentFolderTitle: "\u05EA\u05DB\u05EA\u05D5\u05D1\u05EA", // תכתובת
            TypeFile: ".pdf",
            IsRequired: true,
            OutSidData: false,
            LegacyTitles:
            [
                "\u05D0\u05D9\u05E9\u05D5\u05E8 \u05DC\u05E7\u05D5\u05D7 \u05DC\u05D4\u05E6\u05E2\u05D4", // אישור לקוח להצעה
            ]),
        // Nested under ניהול_כספי (parent must exist — created/resolved by the rows above).
        new ProjectFileCatalogDefinition(
            Code: ProjectFileCatalogCodes.QuoteClientRequest,
            DefaultTitle: "\u05D3\u05E8\u05D9\u05E9\u05EA_\u05D4\u05DE\u05D6\u05DE\u05D9\u05DF_\u05DC\u05D4\u05E6\u05E2\u05EA_\u05DE\u05D7\u05D9\u05E8", // דרישת_המזמין_להצעת_מחיר
            JobTypeTitle: SqlProjectCreateService.DefaultJobTypeTitle,
            FolderTitle: "\u05D4\u05E6\u05E2\u05EA_\u05DE\u05D7\u05D9\u05E8", // הצעת_מחיר
            ParentFolderTitle: "\u05E0\u05D9\u05D4\u05D5\u05DC_\u05DB\u05E1\u05E4\u05D9", // ניהול_כספי
            TypeFile: ".pdf",
            IsRequired: true,
            // Must be true: email ACC tagging picker only lists OutSidData catalog slots.
            OutSidData: true,
            LegacyTitles:
            [
                "\u05D3\u05E8\u05D9\u05E9\u05EA \u05D4\u05DE\u05D6\u05DE\u05D9\u05DF \u05DC\u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", // דרישת המזמין להצעת מחיר
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
        bool? OutSidData,
        IReadOnlyList<string> LegacyTitles);

    /// <summary>
    /// Ensures every catalog definition exists and is linked by <c>Code</c>.
    /// Prefers existing underscore folders/files; cleans space-named duplicates from older seeds.
    /// Never overwrites <see cref="ProjectFile.TemplateLocation"/>.
    /// </summary>
    public static async Task<string> EnsureAsync(SiNetSQLDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await ProjectFileSchemaPatches.EnsureCatalogColumnsAsync(db, ct).ConfigureAwait(false);

        var parts = new List<string>();
        foreach (var def in Definitions)
            parts.Add(await EnsureOneAsync(db, def, ct).ConfigureAwait(false));

        var cleanup = await CleanupSpaceNamedDuplicatesAsync(db, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cleanup))
            parts.Add(cleanup);

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

    /// <summary>Canonical title plus space/underscore swaps.</summary>
    public static IReadOnlyList<string> TitleAliases(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var set = new HashSet<string>(StringComparer.Ordinal) { title };
        if (title.Contains('_', StringComparison.Ordinal))
            set.Add(title.Replace('_', ' '));
        if (title.Contains(' ', StringComparison.Ordinal))
            set.Add(title.Replace(' ', '_'));
        return set.ToList();
    }

    private static HashSet<string> KnownFileTitles(ProjectFileCatalogDefinition def)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in TitleAliases(def.DefaultTitle))
            set.Add(t);
        foreach (var legacy in def.LegacyTitles)
        {
            set.Add(legacy);
            foreach (var t in TitleAliases(legacy))
                set.Add(t);
        }

        return set;
    }

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
            return $"[{def.Code}] skipped (folder '{def.FolderTitle}' under '{def.ParentFolderTitle}' not found; create parent «{def.ParentFolderTitle}» first).";

        var typeId = jobType.Id;
        var knownTitles = KnownFileTitles(def);

        var byCode = await db.ProjectFiles
            .FirstOrDefaultAsync(f => f.Code == def.Code, ct)
            .ConfigureAwait(false);

        // Prefer an existing office row (underscore / template) over a wrongly seeded coded duplicate.
        byCode = await PreferExistingCatalogRowAsync(db, def, typeId, knownTitles, byCode, ct)
            .ConfigureAwait(false);

        if (byCode is null)
        {
            var candidates = await db.ProjectFiles
                .Where(f => f.TypeProjId == typeId && f.Title != null)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            byCode = PreferCandidate(candidates.Where(f => knownTitles.Contains(f.Title!)).ToList())
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
                .Where(f => f.TypeProjId == typeId && f.Title != null)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var match = PreferCandidate(sameTypeTitle.Where(f => knownTitles.Contains(f.Title!)).ToList());
            if (match is not null)
            {
                byCode = match;
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
                    OutSidData = def.OutSidData,
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

        if (def.OutSidData is { } outSid && byCode.OutSidData != outSid)
        {
            byCode.OutSidData = outSid;
            changed = true;
        }

        // TemplateLocation is never written here.

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
    /// When Code sits on a wrongly seeded space-named row, move Code to the office
    /// underscore / templated peer and drop the spurious coded duplicate.
    /// </summary>
    private static async Task<ProjectFile?> PreferExistingCatalogRowAsync(
        SiNetSQLDbContext db,
        ProjectFileCatalogDefinition def,
        int typeId,
        HashSet<string> knownTitles,
        ProjectFile? byCode,
        CancellationToken ct)
    {
        var peers = await db.ProjectFiles
            .Where(f => f.TypeProjId == typeId && f.Title != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var titledPeers = peers.Where(f => knownTitles.Contains(f.Title!)).ToList();
        if (titledPeers.Count == 0)
            return byCode;

        var preferred = PreferCandidate(
            byCode is null
                ? titledPeers
                : titledPeers.Append(byCode).DistinctBy(f => f.Id).ToList());

        if (preferred is null)
            return byCode;

        if (byCode is null || byCode.Id == preferred.Id)
            return preferred;

        // Move Code onto preferred; remove the wrongly seeded duplicate.
        preferred.Code = def.Code;
        preferred.Modified = DateTime.UtcNow;
        db.ProjectFiles.Remove(byCode);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return preferred;
    }

    private static ProjectFile? PreferCandidate(IReadOnlyList<ProjectFile> candidates)
    {
        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderByDescending(f => !string.IsNullOrWhiteSpace(f.TemplateLocation))
            .ThenByDescending(f => f.Title is not null && f.Title.Contains('_', StringComparison.Ordinal))
            .ThenByDescending(f => f.Code != null)
            .ThenBy(f => f.Id)
            .First();
    }

    /// <summary>
    /// Resolves <paramref name="folderTitle"/> under <paramref name="parentFolderTitle"/> (alias-aware).
    /// Renames space-named matches to the underscore canonical title.
    /// Creates the child only when missing — never invents a missing parent.
    /// </summary>
    private static async Task<int?> EnsureFolderUnderParentAsync(
        SiNetSQLDbContext db,
        string folderTitle,
        string parentFolderTitle,
        CancellationToken ct)
    {
        var parentAliases = TitleAliases(parentFolderTitle);
        var parent = await db.ProjectFolders
            .AsNoTracking()
            .Where(f => f.Title != null && parentAliases.Contains(f.Title))
            .OrderByDescending(f => f.Title!.Contains('_'))
            .ThenBy(f => f.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (parent is null)
            return null;

        var childAliases = TitleAliases(folderTitle);
        var matches = await db.ProjectFolders
            .Where(f => f.Title != null && childAliases.Contains(f.Title))
            .OrderBy(f => f.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var underParent = matches.Where(f => f.Infolderid == parent.Id).ToList();
        var existing = PreferFolder(underParent)
                       ?? PreferFolder(matches);

        if (existing is not null)
        {
            var changed = false;
            if (existing.Infolderid != parent.Id && existing.Id != parent.Id)
            {
                // Only reparent when no alias already sits under the correct parent.
                if (underParent.Count == 0)
                {
                    existing.Infolderid = parent.Id;
                    changed = true;
                }
                else
                {
                    existing = PreferFolder(underParent)!;
                }
            }

            if (!string.Equals(existing.Title, folderTitle, StringComparison.Ordinal))
            {
                existing.Title = folderTitle;
                changed = true;
            }

            if (changed)
            {
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

    private static ProjectFolder? PreferFolder(IReadOnlyList<ProjectFolder> folders)
    {
        if (folders.Count == 0)
            return null;

        return folders
            .OrderByDescending(f => f.Title is not null && f.Title.Contains('_', StringComparison.Ordinal))
            .ThenBy(f => f.Id)
            .First();
    }

    /// <summary>
    /// Removes space-named duplicate file defs and empty duplicate folders left by older seeds.
    /// Keeper rows (current <c>Code</c> owners) are never deleted.
    /// </summary>
    private static async Task<string> CleanupSpaceNamedDuplicatesAsync(
        SiNetSQLDbContext db,
        CancellationToken ct)
    {
        var deletedFiles = 0;
        var deletedFolders = 0;
        var keeperIds = new HashSet<int>();

        foreach (var def in Definitions)
        {
            var keeper = await db.ProjectFiles.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Code == def.Code, ct)
                .ConfigureAwait(false);
            if (keeper is not null)
                keeperIds.Add(keeper.Id);
        }

        var spaceFileTitles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in Definitions)
        {
            foreach (var alias in TitleAliases(def.DefaultTitle))
            {
                if (alias.Contains(' ', StringComparison.Ordinal))
                    spaceFileTitles.Add(alias);
            }

            foreach (var legacy in def.LegacyTitles)
            {
                if (legacy.Contains(' ', StringComparison.Ordinal))
                    spaceFileTitles.Add(legacy);
            }
        }

        var fileDupes = await db.ProjectFiles
            .Where(f => f.Title != null && spaceFileTitles.Contains(f.Title))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var dupe in fileDupes)
        {
            if (keeperIds.Contains(dupe.Id))
                continue;

            // Only remove uncoded leftovers or rows whose Code is a known catalog code
            // but are not the keeper (should not happen after PreferExisting).
            if (dupe.Code is not null && !IsKnownCatalogCode(dupe.Code))
                continue;

            if (dupe.Code is not null && keeperIds.Contains(dupe.Id))
                continue;

            db.ProjectFiles.Remove(dupe);
            deletedFiles++;
        }

        if (deletedFiles > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Space-named folder aliases for catalog paths (not single-word parents like תכתובת).
        var spaceFolderTitles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in Definitions)
        {
            foreach (var alias in TitleAliases(def.FolderTitle))
            {
                if (alias.Contains(' ', StringComparison.Ordinal))
                    spaceFolderTitles.Add(alias);
            }

            foreach (var alias in TitleAliases(def.ParentFolderTitle))
            {
                if (alias.Contains(' ', StringComparison.Ordinal)
                    && !string.Equals(alias, "\u05EA\u05DB\u05EA\u05D5\u05D1\u05EA", StringComparison.Ordinal))
                    spaceFolderTitles.Add(alias);
            }
        }

        // Delete deepest empty space folders first (children before parents).
        for (var pass = 0; pass < 8; pass++)
        {
            var spaceFolders = await db.ProjectFolders
                .Where(f => f.Title != null && spaceFolderTitles.Contains(f.Title))
                .OrderByDescending(f => f.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var removedThisPass = 0;
            foreach (var folder in spaceFolders)
            {
                var canonicalAliases = TitleAliases(folder.Title!.Replace(' ', '_'));
                var hasCanonicalSibling = await db.ProjectFolders.AsNoTracking()
                    .AnyAsync(
                        f => f.Id != folder.Id
                             && f.Title != null
                             && canonicalAliases.Contains(f.Title)
                             && f.Title.Contains('_'),
                        ct)
                    .ConfigureAwait(false);
                if (!hasCanonicalSibling)
                    continue;

                var hasFiles = await db.ProjectFiles.AsNoTracking()
                    .AnyAsync(f => f.Folderid == folder.Id, ct)
                    .ConfigureAwait(false);
                if (hasFiles)
                    continue;

                var hasChildren = await db.ProjectFolders.AsNoTracking()
                    .AnyAsync(f => f.Infolderid == folder.Id, ct)
                    .ConfigureAwait(false);
                if (hasChildren)
                    continue;

                db.ProjectFolders.Remove(folder);
                deletedFolders++;
                removedThisPass++;
            }

            if (removedThisPass == 0)
                break;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (deletedFiles == 0 && deletedFolders == 0)
            return string.Empty;

        return $"[cleanup] removed space-named duplicates: files={deletedFiles} folders={deletedFolders}.";
    }
}
