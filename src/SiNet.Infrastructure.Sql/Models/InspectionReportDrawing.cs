namespace SiNetSQL.Models;

/// <summary>File format of the drawing attached to an inspection report.</summary>
public enum DrawingFileType
{
    Dwf = 0,
    Pdf = 1
}

/// <summary>Status of the stamp operation for a drawing.</summary>
public enum DrawingStampStatus
{
    NotStamped = 0,
    Stamped = 1,
    Failed = 2
}

/// <summary>
/// Associates a drawing file (DWF or PDF) with an inspection report,
/// tracking which layouts/pages are inspected and the stamping outcome.
/// </summary>
public class InspectionReportDrawing
{
    public int Id { get; set; }

    public int ReportId { get; set; }

    /// <summary>Physical path to the source drawing file.</summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>Original file name for display purposes.</summary>
    public string FileName { get; set; } = string.Empty;

    public DrawingFileType FileType { get; set; }

    /// <summary>
    /// JSON array of selected layout/page indices (e.g., "[0,2,5]").
    /// Empty array "[]" means all layouts/pages.
    /// </summary>
    public string SelectedLayoutIndices { get; set; } = "[]";

    public DrawingStampStatus StampStatus { get; set; } = DrawingStampStatus.NotStamped;

    /// <summary>Path to the stamped copy of the drawing file.</summary>
    public string? StampedFilePath { get; set; }

    public DateTime? StampedAt { get; set; }

    // Navigation
    public virtual InspectionReport Report { get; set; } = null!;
}
