namespace SiNetSQL.Models;

/// <summary>
/// One logical entry in the "reviewed plan" of an <see cref="InspectionReport"/>:
/// a file (and optionally an alternative / sheet) that this report is reviewing.
///
/// Stores **only logical identity** (no path / ACC / viewer / window).
/// Version is intentionally NOT stored here — it lives once on the parent report
/// (<see cref="InspectionReport.ReviewedVersion"/>). The full reviewed-plan identity
/// per row is therefore: <c>FileName + Alternative + parent ReviewedVersion</c>.
///
/// Source of these rows is <c>IActiveFileQueryService</c> from Work Window 2.
/// </summary>
public class InspectionReportReviewedFile
{
    public int Id { get; set; }

    public int ReportId { get; set; }

    /// <summary>Logical file name as exposed by Work Window 2 (no path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Alternative / sheet name. Null = file has no alternative grouping.</summary>
    public string? Alternative { get; set; }

    public int SortOrder { get; set; }

    // Navigation
    public virtual InspectionReport InspectionReport { get; set; } = null!;
}
