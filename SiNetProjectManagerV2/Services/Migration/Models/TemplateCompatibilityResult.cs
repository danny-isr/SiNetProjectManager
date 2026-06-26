namespace SiNetProjectManagerV2.Services.Migration.Models;

/// <summary>
/// Result of matching one JSON section against the target template.
/// Used by preview grid and double-click detail view.
/// </summary>
public sealed class SectionCompatibilityEntry
{
    /// <summary>Parent section code from JSON (e.g. "3.2").</summary>
    public string SectionCode { get; init; } = string.Empty;

    /// <summary>Section title from the JSON extraction.</summary>
    public string JsonSectionTitle { get; init; } = string.Empty;

    /// <summary>Section title from the target template (null = section not found in template).</summary>
    public string? TemplateSectionTitle { get; init; }

    /// <summary>Match result.</summary>
    public SectionMatchResult MatchResult { get; init; }

    /// <summary>Human-readable reason for the match/mismatch.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Human-readable display combining status + reason.</summary>
    public string DisplayStatus => MatchResult switch
    {
        SectionMatchResult.Matched => "✅ מתאים",
        SectionMatchResult.TitleMismatch => "⚠ אי-התאמת כותרת",
        SectionMatchResult.MissingInTemplate => "❌ לא נמצא בתבנית",
        SectionMatchResult.NoJson => "— אין JSON",
        _ => "—"
    };
}

/// <summary>
/// Match result for a single section code comparison.
/// </summary>
public enum SectionMatchResult
{
    /// <summary>Section code found in template AND title/description is compatible.</summary>
    Matched,

    /// <summary>Section code found in template but title/description does not match.</summary>
    TitleMismatch,

    /// <summary>Section code not found in the target template at all.</summary>
    MissingInTemplate,

    /// <summary>Template section exists but no JSON data for it (informational).</summary>
    NoJson
}

/// <summary>
/// Aggregate template compatibility result for a single preview row (project + version + report).
/// </summary>
public sealed class TemplateCompatibilityResult
{
    /// <summary>Per-section match entries.</summary>
    public List<SectionCompatibilityEntry> Entries { get; init; } = [];

    public int MatchedCount => Entries.Count(e => e.MatchResult == SectionMatchResult.Matched);
    public int MismatchCount => Entries.Count(e => e.MatchResult == SectionMatchResult.TitleMismatch);
    public int MissingCount => Entries.Count(e => e.MatchResult == SectionMatchResult.MissingInTemplate);
    public bool HasAnyMatch => MatchedCount > 0;

    /// <summary>
    /// Set of parent section codes (e.g. "3.2") that are eligible for import into the
    /// selected target template. A code is eligible only when its compatibility entry is
    /// <see cref="SectionMatchResult.Matched"/> (code found in template AND title compatible).
    /// This is the single source of truth used by the template-shaped report preview and any
    /// future import: a section/note is import-eligible only if its parent code is in this set.
    /// </summary>
    public IReadOnlySet<string> ImportEligibleSectionCodes => Entries
        .Where(e => e.MatchResult == SectionMatchResult.Matched)
        .Select(e => e.SectionCode)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the given parent section code is eligible for import into the
    /// selected target template (matched by code and title). A note must never be imported
    /// or previewed by section number alone when this returns false.
    /// </summary>
    public bool IsImportEligible(string? parentSectionCode) =>
        !string.IsNullOrWhiteSpace(parentSectionCode) &&
        ImportEligibleSectionCodes.Contains(parentSectionCode);

    /// <summary>Number of JSON sections that are NOT eligible for import (mismatch + missing).</summary>
    public int SkippedSectionCount => MismatchCount + MissingCount;

    /// <summary>Build a summary string for the TemplateWarnings field.</summary>
    public string BuildWarningsSummary()
    {
        var warnings = new List<string>();
        foreach (var entry in Entries.Where(e => e.MatchResult != SectionMatchResult.Matched && e.MatchResult != SectionMatchResult.NoJson))
        {
            warnings.Add($"[{entry.SectionCode}] {entry.DisplayStatus}: {entry.Reason}");
        }
        return string.Join(" | ", warnings);
    }
}
