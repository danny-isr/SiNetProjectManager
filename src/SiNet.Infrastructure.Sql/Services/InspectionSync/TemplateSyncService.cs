using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.Services.InspectionSync;

internal static class TemplateSyncLog
{
    public static void Info(string message) => System.Diagnostics.Trace.TraceInformation(message);
    public static void Warn(string message) => System.Diagnostics.Trace.TraceWarning(message);
    public static void Error(string message) => System.Diagnostics.Trace.TraceError(message);
    public static void Error(Exception ex, string message) => System.Diagnostics.Trace.TraceError(message + ": " + ex.Message);
}


/// <summary>
/// Syncs inspection template data (Chapters &amp; Sections) from an external source
/// (e.g., Google Sheet) into the database using a dictionary-based model:
/// <list type="bullet">
///   <item>Chapter/Section display names are stored in <see cref="ChapterName"/>/<see cref="SectionName"/> dictionaries.</item>
///   <item><b>Scenario A</b> — Section exists with identical name → set <c>IsActive = true</c>, no new row.</item>
///   <item><b>Scenario B</b> — Section exists but name changed → deactivate old, create new version with new SectionNameId.</item>
///   <item><b>Scenario C</b> — New section code → create a new active row.</item>
/// </list>
/// Sections present in DB but absent from the sheet are deactivated.
/// </summary>
public sealed class TemplateSyncService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _contextFactory;

    // Dictionary caches
    private Dictionary<string, ChapterName> _chapterNameByText = new(StringComparer.Ordinal);
    private Dictionary<string, SectionName> _sectionNameByText = new(StringComparer.Ordinal);

    // Structural caches
    private Dictionary<(int? SeriesId, int ChapterNumber), Chapter> _chapterByKey = new();
    private Dictionary<(int ChapterId, int SectionCode), List<Section>> _sectionsByChapterAndCode = new();

    public TemplateSyncService(IDbContextFactory<SiNetSQLDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <summary>
    /// Resolves or creates an <see cref="InspectionSeries"/> for the given project and template.
    /// Returns the <c>SeriesId</c> to pass into <see cref="SyncAsync"/> and report creation.
    /// </summary>
    public async ValueTask<int> EnsureSeriesAsync(
        int projectId,
        string? spreadsheetId,
        string? templateUrl,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        InspectionSeries? series = null;

        if (!string.IsNullOrWhiteSpace(spreadsheetId))
        {
            series = await context.InspectionSeries
                .FirstOrDefaultAsync(s => s.ProjectId == projectId
                    && s.TemplateSpreadsheetId == spreadsheetId, cancellationToken);
        }

        if (series != null)
        {
            // Update URL if it changed
            if (!string.IsNullOrWhiteSpace(templateUrl) && series.TemplateUrl != templateUrl)
            {
                series.TemplateUrl = templateUrl;
                series.Modified = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }

            TemplateSyncLog.Info($"[TemplateSyncService] Resolved existing series {series.SeriesId} for project {projectId}.");
            return series.SeriesId;
        }

        series = new InspectionSeries
        {
            ProjectId = projectId,
            TemplateSpreadsheetId = spreadsheetId,
            TemplateUrl = templateUrl,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        context.InspectionSeries.Add(series);
        await context.SaveChangesAsync(cancellationToken);

        TemplateSyncLog.Info($"[TemplateSyncService] Created new series {series.SeriesId} for project {projectId}.");
        return series.SeriesId;
    }

    /// <summary>
    /// Executes the full sync operation against the provided template rows.
    /// All changes are committed in a single transaction.
    /// </summary>
    /// <param name="rows">Template rows parsed from the source sheet.</param>
    /// <param name="seriesId">Optional series FK for scoping chapters to a specific template series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<TemplateSyncResult> SyncAsync(
        IReadOnlyList<TemplateSyncRow> rows,
        int? seriesId = null,
        CancellationToken cancellationToken = default)
    {
        var result = new TemplateSyncResult { TotalRows = rows.Count };

        if (rows.Count == 0)
        {
            var msg = "No rows provided for sync — the template sheet may be empty or unreadable.";
            result.Warnings.Add(msg);
            TemplateSyncLog.Warn($"[TemplateSyncService] {msg}");
            return result;
        }

        TemplateSyncLog.Info($"[TemplateSyncService] Starting sync of {rows.Count} rows (SeriesId={seriesId?.ToString() ?? "null"}).");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await LoadLookupsAsync(context, seriesId, cancellationToken);

            // Track which (ChapterId, SectionCode) pairs were seen in the sheet
            var seenKeys = new HashSet<(int ChapterId, int SectionCode)>();

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (string.IsNullOrWhiteSpace(row.SectionCode))
                    {
                        var msg = $"Row {row.RowNumber}: Skipped — empty SectionCode.";
                        result.Warnings.Add(msg);
                        TemplateSyncLog.Warn($"[TemplateSyncService] {msg}");
                        continue;
                    }

                    // ── Chapter 0: General tags (non-numeric codes like "ProjectName") ──
                    if (row.ChapterNumber == 0)
                    {
                        var ch0Name = await EnsureChapterNameAsync(context, "General", cancellationToken);
                        var chapter0 = await EnsureChapterAsync(context, seriesId, 0, ch0Name, result, cancellationToken);
                        if (chapter0 is null)
                        {
                            result.Errors.Add($"Row {row.RowNumber}: Failed to resolve Chapter 0.");
                            continue;
                        }

                        // For general tags, the label itself (e.g. "ProjectName") is the SectionName
                        var genLabel = CleanTagLabel(row.SectionCode);
                        var genSectionName = await EnsureSectionNameAsync(context, genLabel, cancellationToken);

                        // Auto-assign a unique int SectionCode for this general tag
                        var genSubCode = ResolveGeneralTagSectionCode(chapter0.ChapterId, genSectionName.Id);
                        var genKey = (chapter0.ChapterId, genSubCode);
                        seenKeys.Add(genKey);
                        ProcessSection(context, chapter0.ChapterId, genSectionName.Id, genSubCode, genKey, result);
                        continue;
                    }

                    // ── Numbered sections: Parse "3.1" → chapterNumber=3, subCode=1 ──
                    if (!TryParseSectionCode(row.SectionCode, out var chapterNumber, out var subCode))
                    {
                        var msg = $"Row {row.RowNumber}: Cannot parse SectionCode '{row.SectionCode}' into chapter.sub format.";
                        result.Warnings.Add(msg);
                        TemplateSyncLog.Warn($"[TemplateSyncService] {msg}");
                        continue;
                    }

                    var effectiveChapterNumber = row.ChapterNumber > 0 ? row.ChapterNumber : chapterNumber;

                    // Extract chapter title (text before brackets) and section name (bracket content)
                    var (chapterTitleFromTag, sectionTitleName) = ExtractChapterAndSectionTitle(row.SectionTitle);
                    var effectiveChapterTitle = row.ChapterTitle ?? chapterTitleFromTag ?? $"Chapter {effectiveChapterNumber}";

                    var chapterName = await EnsureChapterNameAsync(context, effectiveChapterTitle, cancellationToken);
                    var chapter = await EnsureChapterAsync(context, seriesId, effectiveChapterNumber, chapterName, result, cancellationToken);
                    if (chapter is null)
                    {
                        var msg = $"Row {row.RowNumber}: Failed to resolve chapter {effectiveChapterNumber}.";
                        result.Errors.Add(msg);
                        TemplateSyncLog.Error($"[TemplateSyncService] {msg}");
                        continue;
                    }

                    var sectionName = await EnsureSectionNameAsync(
                        context, sectionTitleName ?? row.SectionCode, cancellationToken);

                    var key = (chapter.ChapterId, subCode);
                    seenKeys.Add(key);

                    ProcessSection(context, chapter.ChapterId, sectionName.Id, subCode, key, result);
                }
                catch (Exception rowEx)
                {
                    var msg = $"Row {row.RowNumber}: Unexpected error processing SectionCode '{row.SectionCode}': {rowEx.Message}";
                    result.Errors.Add(msg);
                    TemplateSyncLog.Error(rowEx, $"[TemplateSyncService] {msg}");
                }
            }

            // Deactivate sections that exist in DB but were NOT in the sheet
            DeactivateAbsentSections(seenKeys, result);

            result.DbSavedCount = await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TemplateSyncLog.Info(
                $"[TemplateSyncService] Sync complete — Created={result.CreatedCount}, Versioned={result.VersionedCount}, " +
                $"Reactivated={result.ReactivatedCount}, Unchanged={result.UnchangedCount}, " +
                $"Deactivated={result.DeactivatedCount}, Chapters={result.ChaptersCreatedCount}, " +
                $"Warnings={result.Warnings.Count}, Errors={result.Errors.Count}.");
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            result.Errors.Add("Sync was cancelled.");
            TemplateSyncLog.Warn("[TemplateSyncService] Sync was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            result.Errors.Add($"Sync failed: {ex.Message}");
            TemplateSyncLog.Error(ex, "[TemplateSyncService] Sync transaction failed");
        }

        return result;
    }

    #region Lookups

    /// <summary>
    /// Pre-loads dictionary entries, chapters and sections into memory for fast matching.
    /// When <paramref name="seriesId"/> is provided, chapters and sections are scoped
    /// to that series only — preventing cross-series deactivation during sync.
    /// Dictionary names (ChapterName, SectionName) remain global since they are shared.
    /// </summary>
    private async Task LoadLookupsAsync(SiNetSQLDbContext context, int? seriesId, CancellationToken cancellationToken)
    {
        var chapterNames = await context.ChapterNames.ToListAsync(cancellationToken);
        _chapterNameByText = chapterNames.ToDictionary(c => c.Name, StringComparer.Ordinal);

        var sectionNames = await context.SectionNames.ToListAsync(cancellationToken);
        _sectionNameByText = sectionNames.ToDictionary(s => s.Name, StringComparer.Ordinal);

        // Scope structural caches by seriesId to prevent DeactivateAbsentSections
        // from deactivating sections belonging to other series.
        IQueryable<Chapter> chapterQuery = context.Chapters.Include(c => c.ChapterName);
        if (seriesId.HasValue)
            chapterQuery = chapterQuery.Where(c => c.SeriesId == seriesId.Value);

        var trackedChapters = await chapterQuery.ToListAsync(cancellationToken);
        _chapterByKey = trackedChapters.ToDictionary(c => ((int?)c.SeriesId, c.ChapterNumber));

        // Load only sections belonging to the scoped chapters.
        var chapterIds = trackedChapters.Select(c => c.ChapterId).ToHashSet();
        IQueryable<Section> sectionQuery = context.Sections;
        if (seriesId.HasValue)
            sectionQuery = sectionQuery.Where(s => chapterIds.Contains(s.ChapterId));

        var sections = await sectionQuery.ToListAsync(cancellationToken);
        _sectionsByChapterAndCode = sections
            .GroupBy(s => (s.ChapterId, s.SectionCode))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Version).ToList());
    }

    #endregion

    #region Dictionary Resolution

    /// <summary>
    /// Returns an existing <see cref="ChapterName"/> or creates a new dictionary entry.
    /// </summary>
    private async ValueTask<ChapterName> EnsureChapterNameAsync(
        SiNetSQLDbContext context,
        string name,
        CancellationToken cancellationToken)
    {
        if (_chapterNameByText.TryGetValue(name, out var existing))
        {
            TemplateSyncLog.Info($"[TemplateSyncService] ChapterName HIT: \"{name}\" → Id={existing.Id}");
            return existing;
        }

        var entry = new ChapterName { Name = name };
        context.ChapterNames.Add(entry);
        await context.SaveChangesAsync(cancellationToken);

        _chapterNameByText[name] = entry;
        TemplateSyncLog.Info($"[TemplateSyncService] ChapterName NEW: \"{name}\" → Id={entry.Id}");
        return entry;
    }

    /// <summary>
    /// Returns an existing <see cref="SectionName"/> or creates a new dictionary entry.
    /// </summary>
    private async ValueTask<SectionName> EnsureSectionNameAsync(
        SiNetSQLDbContext context,
        string name,
        CancellationToken cancellationToken)
    {
        if (_sectionNameByText.TryGetValue(name, out var existing))
        {
            TemplateSyncLog.Info($"[TemplateSyncService] SectionName HIT: \"{name}\" → Id={existing.Id}");
            return existing;
        }

        var entry = new SectionName { Name = name };
        context.SectionNames.Add(entry);
        await context.SaveChangesAsync(cancellationToken);

        _sectionNameByText[name] = entry;
        TemplateSyncLog.Info($"[TemplateSyncService] SectionName NEW: \"{name}\" → Id={entry.Id}");
        return entry;
    }

    #endregion

    #region Chapter Resolution

    /// <summary>
    /// Returns an existing chapter or creates a new one.
    /// If the chapter exists but with a different name, updates the FK.
    /// </summary>
    private async ValueTask<Chapter?> EnsureChapterAsync(
        SiNetSQLDbContext context,
        int? seriesId,
        int chapterNumber,
        ChapterName chapterName,
        TemplateSyncResult result,
        CancellationToken cancellationToken)
    {
        var key = (seriesId, chapterNumber);
        if (_chapterByKey.TryGetValue(key, out var chapter))
        {
            if (chapter.ChapterNameId != chapterName.Id)
            {
                chapter.ChapterNameId = chapterName.Id;
                chapter.ChapterName = chapterName;
            }
            return chapter;
        }

        var newChapter = new Chapter
        {
            SeriesId = seriesId,
            ChapterNumber = chapterNumber,
            ChapterNameId = chapterName.Id,
            ChapterName = chapterName
        };

        context.Chapters.Add(newChapter);
        await context.SaveChangesAsync(cancellationToken);

        _chapterByKey[key] = newChapter;
        result.ChaptersCreatedCount++;

        return newChapter;
    }

    #endregion

    #region Smart Sync Logic

    /// <summary>
    /// Applies the upsert strategy for a single section row.
    /// Comparison is based on <see cref="Section.SectionNameId"/> — if the
    /// name for a given (ChapterId, SectionCode) pair changes, a new version is created.
    /// </summary>
    private void ProcessSection(
        SiNetSQLDbContext context,
        int chapterId,
        int sectionNameId,
        int subCode,
        (int ChapterId, int SectionCode) cacheKey,
        TemplateSyncResult result)
    {
        if (_sectionsByChapterAndCode.TryGetValue(cacheKey, out var existingVersions) && existingVersions.Count > 0)
        {
            var latest = existingVersions[0];

            bool nameIdentical = latest.SectionNameId == sectionNameId;

            if (nameIdentical)
            {
                // ── Scenario A: Same SectionName — content identical ──
                if (!latest.IsActive)
                {
                    latest.IsActive = true;
                    result.ReactivatedCount++;
                }
                else
                {
                    result.UnchangedCount++;
                }
            }
            else
            {
                // ── Scenario B: SectionName changed for this (Chapter, SectionCode) ──
                if (latest.IsActive)
                {
                    latest.IsActive = false;
                }

                var newVersion = new Section
                {
                    ChapterId = chapterId,
                    SectionNameId = sectionNameId,
                    SectionCode = subCode,
                    Version = latest.Version + 1,
                    IsActive = true
                };

                context.Sections.Add(newVersion);
                existingVersions.Insert(0, newVersion);

                result.VersionedCount++;
            }
        }
        else
        {
            // ── Scenario C: New section code ──
            var newSection = new Section
            {
                ChapterId = chapterId,
                SectionNameId = sectionNameId,
                SectionCode = subCode,
                Version = 1,
                IsActive = true
            };

            context.Sections.Add(newSection);
            _sectionsByChapterAndCode[cacheKey] = [newSection];

            result.CreatedCount++;
        }
    }

    /// <summary>
    /// Deactivates sections that are currently active in DB but were not present in the sheet.
    /// </summary>
    private void DeactivateAbsentSections(
        HashSet<(int ChapterId, int SectionCode)> seenKeys,
        TemplateSyncResult result)
    {
        foreach (var (key, versions) in _sectionsByChapterAndCode)
        {
            if (seenKeys.Contains(key))
                continue;

            foreach (var version in versions.Where(v => v.IsActive))
            {
                version.IsActive = false;
                result.DeactivatedCount++;
            }
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Parses a section code string "3.1" into chapter number (3) and sub-code (1).
    /// Returns <c>false</c> if the format is invalid.
    /// </summary>
    private static bool TryParseSectionCode(string sectionCode, out int chapterNumber, out int subCode)
    {
        chapterNumber = 0;
        subCode = 0;

        // Strip bidi marks, <<>>, and whitespace
        var cleaned = sectionCode
            .Replace("\u200F", "", StringComparison.Ordinal)
            .Replace("\u200E", "", StringComparison.Ordinal)
            .Replace("<<", "", StringComparison.Ordinal)
            .Replace(">>", "", StringComparison.Ordinal)
            .Trim();

        // Extract leading numeric part "X.Y"
        var i = 0;
        while (i < cleaned.Length && (char.IsDigit(cleaned[i]) || cleaned[i] == '.'))
            i++;
        var numericPart = cleaned[..i].TrimEnd('.');

        var dotIdx = numericPart.IndexOf('.');
        if (dotIdx <= 0 || dotIdx == numericPart.Length - 1)
            return false;

        return int.TryParse(numericPart[..dotIdx], out chapterNumber)
               && int.TryParse(numericPart[(dotIdx + 1)..], out subCode);
    }

    /// <summary>
    /// Extracts the chapter title (text before brackets) and section name (bracket content)
    /// from a section tag string.
    /// <para>Example: <c>"&lt;&lt;3.1 Parking [Signage and Striping]&gt;&gt;"</c>
    /// → chapterTitle = "Parking", sectionTitleName = "Signage and Striping".</para>
    /// <para>If no brackets exist, the entire cleaned text becomes the section name
    /// (it also serves as the chapter title fallback).</para>
    /// </summary>
    private static (string? ChapterTitle, string? SectionTitleName) ExtractChapterAndSectionTitle(string? sectionTitle)
    {
        if (string.IsNullOrWhiteSpace(sectionTitle))
            return (null, null);

        var cleaned = sectionTitle
            .Replace("<<", "", StringComparison.Ordinal)
            .Replace(">>", "", StringComparison.Ordinal)
            .Trim();

        // Strip leading numeric code (e.g. "3.1 ")
        var i = 0;
        while (i < cleaned.Length && (char.IsDigit(cleaned[i]) || cleaned[i] == '.'))
            i++;
        if (i > 0 && i < cleaned.Length)
            cleaned = cleaned[i..].TrimStart();

        // Extract bracket content → this is the SectionName
        var bracketStart = cleaned.IndexOf('[');
        var bracketEnd = cleaned.IndexOf(']');
        if (bracketStart >= 0 && bracketEnd > bracketStart)
        {
            var sectionTitleName = cleaned[(bracketStart + 1)..bracketEnd].Trim();
            var chapterTitle = (cleaned[..bracketStart] + cleaned[(bracketEnd + 1)..]).Trim();

            return (
                string.IsNullOrWhiteSpace(chapterTitle) ? null : chapterTitle,
                string.IsNullOrWhiteSpace(sectionTitleName) ? null : sectionTitleName);
        }

        // No brackets — full text is both chapter title fallback and section name
        var fullText = string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        return (fullText, fullText);
    }

    /// <summary>
    /// Strips angle brackets, bidi marks and whitespace from a general tag label.
    /// </summary>
    private static string CleanTagLabel(string rawCode)
    {
        return rawCode
            .Replace("<<", "", StringComparison.Ordinal)
            .Replace(">>", "", StringComparison.Ordinal)
            .Replace("\u200F", "", StringComparison.Ordinal)
            .Replace("\u200E", "", StringComparison.Ordinal)
            .Trim();
    }

    /// <summary>
    /// Resolves a unique int SectionCode for a general tag within Chapter 0.
    /// Looks for an existing section with the given <paramref name="sectionNameId"/>; if none found,
    /// assigns the next available code (max existing code + 1).
    /// </summary>
    private int ResolveGeneralTagSectionCode(int chapterId, int sectionNameId)
    {
        // Check if there's already a section for this SectionName in Chapter 0
        foreach (var (key, versions) in _sectionsByChapterAndCode)
        {
            if (key.ChapterId != chapterId)
                continue;

            var anyVersion = versions.FirstOrDefault();
            if (anyVersion?.SectionNameId == sectionNameId)
                return key.SectionCode;
        }

        // Assign next available code
        var maxCode = 0;
        foreach (var (key, _) in _sectionsByChapterAndCode)
        {
            if (key.ChapterId == chapterId && key.SectionCode > maxCode)
                maxCode = key.SectionCode;
        }

        return maxCode + 1;
    }

    #endregion
}
