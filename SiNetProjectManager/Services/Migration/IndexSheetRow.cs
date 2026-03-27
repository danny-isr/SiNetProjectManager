namespace SiNetProjectManager.Services.Migration;

/// <summary>
/// DTO for a single row from a Google Sheets report index/tracking sheet.
/// Each row represents one inspection report visit.
/// </summary>
public sealed class IndexSheetRow
{
    /// <summary>Zero-based row index in the sheet.</summary>
    public int RowIndex { get; init; }

    /// <summary>Project ID or name extracted from the sheet (Column A/B).</summary>
    public string ProjectIdOrName { get; init; } = string.Empty;

    /// <summary>Report number (e.g. "1", "2").</summary>
    public string ReportNumber { get; init; } = string.Empty;

    /// <summary>Inspection date text (e.g. "01/01/2024").</summary>
    public string InspectionDate { get; init; } = string.Empty;

    /// <summary>Inspector/reviewer name.</summary>
    public string InspectorName { get; init; } = string.Empty;

    /// <summary>Inspector/reviewer email address (used for user assignment lookup).</summary>
    public string InspectorEmail { get; init; } = string.Empty;

    /// <summary>Hebrew status text from the index sheet (e.g. "ממתין לתיקון הערות").</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Link to the report Google Sheet.</summary>
    public string ReportLink { get; init; } = string.Empty;

    /// <summary>Additional notes/comments.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>True if the status text indicates the report is approved/completed.</summary>
    public bool IsApproved { get; init; }
}

/// <summary>
/// Full result of reading a Google Sheets report index.
/// </summary>
public sealed class IndexSheetResult
{
    /// <summary>All parsed data rows.</summary>
    public List<IndexSheetRow> Rows { get; init; } = [];

    /// <summary>Distinct non-empty status values found in the Status column.</summary>
    public List<string> UniqueStatuses { get; init; } = [];

    /// <summary>Warnings or diagnostic messages.</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>True if reading completed without critical failures.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Error message if reading failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Detected header row index (0-based).</summary>
    public int HeaderRow { get; init; }

    /// <summary>Mapping of canonical column key to column index.</summary>
    public Dictionary<string, int> ColumnMapping { get; init; } = new();
}

/// <summary>
/// A single row in the pre-migration review table shown to the user before committing.
/// Combines the sheet data with DB resolution results.
/// </summary>
public sealed class MigrationPreviewRow
{
    /// <summary>Zero-based sheet row index.</summary>
    public int RowIndex { get; init; }

    /// <summary>Project identifier as it appears in the sheet.</summary>
    public string SheetProjectRef { get; init; } = string.Empty;

    /// <summary>Resolved project ID from the database, or null if not found.</summary>
    public int? ResolvedProjectId { get; init; }

    /// <summary>Resolved project display name, or "❌ לא נמצא" if unresolved.</summary>
    public string ResolvedProjectName { get; init; } = string.Empty;

    /// <summary>Resolved project type name, or empty if project has no type assigned.</summary>
    public string ProjectTypeName { get; init; } = string.Empty;

    /// <summary>Report number from the sheet.</summary>
    public string ReportNumber { get; init; } = string.Empty;

    /// <summary>Status text from the sheet.</summary>
    public string SheetStatus { get; init; } = string.Empty;

    /// <summary>Whether the status indicates approval.</summary>
    public bool IsApproved { get; init; }

    /// <summary>Human-readable description of what the system will do for this row.</summary>
    public string ActionDescription { get; init; } = string.Empty;

    /// <summary>True if the row can be migrated (project resolved, not approved).</summary>
    public bool CanMigrate { get; init; }

    /// <summary>True if user has selected this row for migration (defaults to CanMigrate).</summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// Result of building the pre-migration preview (scan + DB resolution, before any writes).
/// </summary>
public sealed class MigrationPreviewResult
{
    public List<MigrationPreviewRow> Rows { get; init; } = [];
    public int TaskTypeId { get; init; }
    public bool TaskTypeCreated { get; init; }
    public string TaskTypeName { get; init; } = string.Empty;
    public Dictionary<string, int> StatusNameToId { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int NewStatusesCreated { get; init; }
    public List<string> Warnings { get; init; } = [];
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of task generation from approved preview rows.
/// Detailed per-row logs are written to <see cref="SiNetSQL.Services.AppLogger"/>.
/// </summary>
public sealed class TaskGenerationResult
{
    public int TasksCreated { get; init; }
    public int TasksSkipped { get; init; }
    public int TasksDuplicate { get; init; }
    public int TasksFailed { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Report hyperlinks extracted from one row of the index (tracking) sheet.
/// Each entry represents a project row with links to individual report Google Sheets.
/// </summary>
public sealed class IndexSheetReportLink
{
    /// <summary>Zero-based row index in the sheet.</summary>
    public int RowIndex { get; init; }

    /// <summary>Project identifier text from the sheet (name or number).</summary>
    public string ProjectRef { get; init; } = "";

    /// <summary>Report number from the sheet (e.g. "1", "2").</summary>
    public string ReportNumber { get; init; } = "";

    /// <summary>
    /// Google Sheets spreadsheet IDs extracted from hyperlinks in the links column, in order of appearance.
    /// Typically version numbers: the last entry is the latest version.
    /// </summary>
    public List<string> ReportSpreadsheetIds { get; init; } = [];
}
