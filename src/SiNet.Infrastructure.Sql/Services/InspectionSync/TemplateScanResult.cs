namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Result of scanning and validating a Google Sheet inspection template.
/// Contains the parsed sync rows, all discovered tags, and any validation errors.
/// </summary>
public sealed class TemplateScanResult
{
    /// <summary>
    /// Sync rows derived from header (definition) tags — ready for <see cref="TemplateSyncService"/>.
    /// Empty when <see cref="HasErrors"/> is <c>true</c>.
    /// </summary>
    public required IReadOnlyList<TemplateSyncRow> SyncRows { get; init; }

    /// <summary>All tags discovered during scanning (numbered + general).</summary>
    public required IReadOnlyList<TemplateScanTag> AllTags { get; init; }

    /// <summary>Validation errors that block the sync operation.</summary>
    public required IReadOnlyList<TemplateValidationError> ValidationErrors { get; init; }

    /// <summary><c>true</c> when at least one validation error was found.</summary>
    public bool HasErrors => ValidationErrors.Count > 0;

    /// <summary>
    /// Zero-based column index of the cell that carries the
    /// <c>&lt;&lt;תגובת המתכנן&gt;&gt;</c> tag, or <c>-1</c> when the tag was not found.
    /// This is the authoritative column used to write/read planner responses;
    /// the legacy "note column + 2" offset is no longer used.
    /// </summary>
    public int PlannerResponseColumnIndex { get; init; } = -1;

    /// <summary>
    /// Zero-based row index of the planner-response tag cell, or <c>-1</c> if not found.
    /// </summary>
    public int PlannerResponseRowIndex { get; init; } = -1;
}
