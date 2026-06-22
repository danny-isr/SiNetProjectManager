using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services.Migration;

/// <summary>
/// Manages on-disk JSON caching of AI extraction results.
/// Folder structure: %APPDATA%\SiNet\ExtractionCache\{project_number}\{version}.json
/// Duplicate versions within the same project get suffixed: 2.1.json, 2.2.json
/// </summary>
public static class ExtractionCacheService
{
    private static readonly string _cacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SiNet", "ExtractionCache");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Saves a single extraction result to disk as a JSON cache file.
    /// </summary>
    /// <param name="result">The extraction result to cache.</param>
    /// <param name="projectNumber">Project number for the folder name.</param>
    /// <param name="versionIndex">1-based version index (position in the hyperlink list).</param>
    /// <param name="reportNumber">Report number from the index sheet row (e.g. "1", "2").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the saved JSON file.</returns>
    public static async Task<string> SaveAsync(
        ReportExtractionResult result,
        string projectNumber,
        int versionIndex,
        string reportNumber,
        CancellationToken cancellationToken = default)
    {
        var projectFolder = Path.Combine(_cacheRoot, SanitizeFolderName(projectNumber));
        Directory.CreateDirectory(projectFolder);

        // Build the filename: "R{reportNumber}_V{versionIndex}.json"
        // Duplicates get suffixed: R1_V2.1.json, R1_V2.2.json
        var baseName = $"R{reportNumber}_V{versionIndex}";
        var filePath = GetUniqueFilePath(projectFolder, baseName);

        var envelope = new ExtractionCacheEnvelope
        {
            ProjectNumber = projectNumber,
            ReportNumber = reportNumber,
            VersionIndex = versionIndex,
            TemplateSpreadsheetId = result.TemplateSpreadsheetId,
            ReportSpreadsheetId = result.ReportSpreadsheetId,
            ExtractedAtUtc = DateTime.UtcNow,
            SectionCount = result.Sections.Count,
            Sections = result.Sections,
            GeneralFields = result.GeneralFields,
            Warnings = result.Warnings
        };

        var json = JsonSerializer.Serialize(envelope, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        AppLogger.Info($"[ExtractionCache] Saved {result.Sections.Count} sections → {filePath}");
        return filePath;
    }

    /// <summary>
    /// Loads a cached extraction result from disk.
    /// Returns null if the file does not exist.
    /// </summary>
    public static async Task<ExtractionCacheEnvelope?> LoadAsync(
        string projectNumber,
        int versionIndex,
        string reportNumber,
        CancellationToken cancellationToken = default)
    {
        var projectFolder = Path.Combine(_cacheRoot, SanitizeFolderName(projectNumber));
        var baseName = $"R{reportNumber}_V{versionIndex}";
        var filePath = Path.Combine(projectFolder, $"{baseName}.json");

        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<ExtractionCacheEnvelope>(json, _jsonOptions);
    }

    /// <summary>
    /// Checks if a cache file already exists for the given project/version/report.
    /// </summary>
    public static bool Exists(string projectNumber, int versionIndex, string reportNumber)
    {
        var projectFolder = Path.Combine(_cacheRoot, SanitizeFolderName(projectNumber));
        var baseName = $"R{reportNumber}_V{versionIndex}";
        var filePath = Path.Combine(projectFolder, $"{baseName}.json");
        return File.Exists(filePath);
    }

    /// <summary>
    /// Returns the root cache folder path for display/diagnostics.
    /// </summary>
    public static string GetCacheRoot() => _cacheRoot;

    /// <summary>
    /// Returns the project-specific cache folder path.
    /// </summary>
    public static string GetProjectCacheFolder(string projectNumber) =>
        Path.Combine(_cacheRoot, SanitizeFolderName(projectNumber));

    // ── Export ──────────────────────────────────────────────────────────

    /// <summary>
    /// Exports all JSON files under the cache root into a ZIP archive.
    /// The ZIP preserves the relative folder structure: {project_number}/{filename}.json
    /// A manifest.json is included at the ZIP root with metadata.
    /// Does NOT include credentials, secrets, or Google tokens.
    /// </summary>
    /// <param name="targetZipPath">Full path of the ZIP file to create. Must not already exist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of JSON files exported.</returns>
    public static async Task<int> ExportToZipAsync(
        string targetZipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        if (File.Exists(targetZipPath))
            throw new InvalidOperationException($"Target ZIP already exists: {targetZipPath}. Delete it first or choose a different path.");

        if (!Directory.Exists(_cacheRoot))
            throw new DirectoryNotFoundException($"Cache root does not exist: {_cacheRoot}");

        var allJsonFiles = Directory.GetFiles(_cacheRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (allJsonFiles.Count == 0)
            throw new InvalidOperationException($"No JSON cache files found under {_cacheRoot}");

        using var zipStream = new FileStream(targetZipPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var file in allJsonFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Entry path relative to cache root: e.g. "1234/R1_V1.json"
            var relativePath = Path.GetRelativePath(_cacheRoot, file).Replace('\\', '/');
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fileStream = File.OpenRead(file);
            await fileStream.CopyToAsync(entryStream, cancellationToken);
        }

        // Write manifest
        var manifest = new
        {
            ExportedAtUtc = DateTime.UtcNow,
            CacheRootPath = _cacheRoot,
            ProjectCount = allJsonFiles.Select(f => Path.GetDirectoryName(f)).Distinct().Count(),
            FileCount = allJsonFiles.Count,
            Warning = "This file contains AI extraction cache data only. No credentials or secrets are included."
        };
        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, cancellationToken);

        AppLogger.Info($"[ExtractionCache] Exported {allJsonFiles.Count} files → {targetZipPath}");
        return allJsonFiles.Count;
    }

    // ── Import ──────────────────────────────────────────────────────────

    /// <summary>
    /// Imports JSON cache files from a ZIP archive into the local cache root.
    /// Existing files are NEVER overwritten — they are skipped and counted.
    /// </summary>
    /// <param name="sourceZipPath">Full path of the ZIP file to import from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result summary with counts of imported, skipped, and invalid entries.</returns>
    public static async Task<CacheImportResult> ImportFromZipAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZipPath);

        if (!File.Exists(sourceZipPath))
            throw new FileNotFoundException($"ZIP file not found: {sourceZipPath}");

        int imported = 0, skipped = 0, invalid = 0;
        var conflicts = new List<string>();

        using var zipStream = File.OpenRead(sourceZipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip manifest and directory entries
            if (entry.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { invalid++; continue; }

            // Entry path must be: {projectFolder}/{filename}.json
            var parts = entry.FullName.Replace('\\', '/').Split('/');
            if (parts.Length != 2) { invalid++; continue; }

            var targetPath = Path.Combine(_cacheRoot, parts[0], parts[1]);
            var targetDir = Path.GetDirectoryName(targetPath)!;

            if (File.Exists(targetPath))
            {
                skipped++;
                conflicts.Add(entry.FullName);
                AppLogger.Info($"[ExtractionCache] Import skipped (already exists): {entry.FullName}");
                continue;
            }

            Directory.CreateDirectory(targetDir);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write);
            await entryStream.CopyToAsync(fileStream, cancellationToken);
            imported++;
        }

        AppLogger.Info($"[ExtractionCache] Import complete: {imported} imported, {skipped} skipped (already exist), {invalid} invalid.");
        return new CacheImportResult(imported, skipped, invalid, conflicts);
    }

    // ── Validate ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans all JSON files under the cache root and validates each envelope.
    /// Returns a summary: total files, valid, invalid (parse error or missing required fields).
    /// </summary>
    public static async Task<(int Total, int Valid, int Invalid, List<string> InvalidPaths)> ValidateCacheAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheRoot))
            return (0, 0, 0, []);

        var allFiles = Directory.GetFiles(_cacheRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        int valid = 0, invalid = 0;
        var invalidPaths = new List<string>();

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var envelope = JsonSerializer.Deserialize<ExtractionCacheEnvelope>(json, _jsonOptions);
                if (envelope == null || string.IsNullOrWhiteSpace(envelope.ProjectNumber))
                {
                    invalid++;
                    invalidPaths.Add(file);
                    AppLogger.Info($"[ExtractionCache] Validate: null or missing ProjectNumber in {file}");
                }
                else
                {
                    valid++;
                }
            }
            catch (Exception ex)
            {
                invalid++;
                invalidPaths.Add(file);
                AppLogger.Info($"[ExtractionCache] Validate: parse error in {file}: {ex.Message}");
            }
        }

        AppLogger.Info($"[ExtractionCache] Validate complete: {allFiles.Count} total, {valid} valid, {invalid} invalid.");
        return (allFiles.Count, valid, invalid, invalidPaths);
    }

    /// <summary>
    /// Searches all JSON cache files for an envelope matching the given report and template
    /// spreadsheet IDs. Returns the first matching envelope, or null if none found.
    /// This is used by the single-report AI extraction flow to avoid re-sending to AI.
    /// </summary>
    public static async Task<ExtractionCacheEnvelope?> FindBySpreadsheetIdsAsync(
        string reportSpreadsheetId,
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheRoot)) return null;

        var allFiles = Directory.GetFiles(_cacheRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var envelope = JsonSerializer.Deserialize<ExtractionCacheEnvelope>(json, _jsonOptions);
                if (envelope == null) continue;

                if (string.Equals(envelope.ReportSpreadsheetId, reportSpreadsheetId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(envelope.TemplateSpreadsheetId, templateSpreadsheetId, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Info($"[ExtractionCache] Cache hit for report={reportSpreadsheetId} in {file}");
                    return envelope;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Info($"[ExtractionCache] FindBySpreadsheetIds: parse error in {file}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a unique file path by appending .1, .2, etc. if the base name already exists.
    /// </summary>
    private static string GetUniqueFilePath(string folder, string baseName)
    {
        var candidate = Path.Combine(folder, $"{baseName}.json");
        if (!File.Exists(candidate)) return candidate;

        // Duplicate handling: baseName.1.json, baseName.2.json, ...
        var suffix = 1;
        while (File.Exists(Path.Combine(folder, $"{baseName}.{suffix}.json")))
            suffix++;

        return Path.Combine(folder, $"{baseName}.{suffix}.json");
    }

    /// <summary>
    /// Removes invalid filename characters from the project number.
    /// </summary>
    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_unknown" : sanitized;
    }
}

/// <summary>
/// JSON envelope wrapping the extraction result with metadata for cache identification.
/// </summary>
public sealed class ExtractionCacheEnvelope
{
    /// <summary>Project number used as folder name.</summary>
    public string ProjectNumber { get; init; } = string.Empty;

    /// <summary>Report number from the index sheet (e.g. "1", "2").</summary>
    public string ReportNumber { get; init; } = string.Empty;

    /// <summary>1-based version index within the report's hyperlink list.</summary>
    public int VersionIndex { get; init; }

    /// <summary>Template spreadsheet ID used for extraction.</summary>
    public string TemplateSpreadsheetId { get; init; } = string.Empty;

    /// <summary>Report spreadsheet ID that was extracted.</summary>
    public string ReportSpreadsheetId { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the extraction was performed and cached.</summary>
    public DateTime ExtractedAtUtc { get; init; }

    /// <summary>Total number of sections extracted.</summary>
    public int SectionCount { get; init; }

    /// <summary>All extracted sections.</summary>
    public List<ExtractedSectionData> Sections { get; init; } = [];

    /// <summary>General field values (tag label → value).</summary>
    public Dictionary<string, string> GeneralFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Warnings/diagnostics from extraction.</summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of a JSON cache import operation.
/// </summary>
public sealed record CacheImportResult(
    int Imported,
    int Skipped,
    int Invalid,
    List<string> SkippedPaths);
