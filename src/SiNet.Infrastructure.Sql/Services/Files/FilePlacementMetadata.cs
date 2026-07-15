namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Companion JSON metadata written alongside a placed FileServer file (file name:
/// <c>{placedFileName}.json</c>). Source of truth for FileServer version history: a placed file
/// without a companion JSON MUST be treated as <see cref="CurrentVersionNumber"/> = 1 (legacy file).
/// </summary>
public sealed class FilePlacementMetadata
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string ConventionFileName { get; set; } = string.Empty;
    public int CurrentVersionNumber { get; set; } = 1;
    public string? EmailSubject { get; set; }
    public string? EmailFrom { get; set; }
    public string? EmailDate { get; set; }
    public string PlacedAtUtc { get; set; } = string.Empty;
    public Dictionary<string, string?> Attributes { get; set; } = new(StringComparer.Ordinal);
}
