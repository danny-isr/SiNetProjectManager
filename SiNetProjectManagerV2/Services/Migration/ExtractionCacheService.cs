using System.IO;
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
