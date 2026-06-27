namespace SiNetSQL.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Frozen snapshot of an <see cref="InspectionReport"/> captured only when the report
/// is officially sent (= successfully exported). One report can have multiple snapshots,
/// but at most one row is flagged as <see cref="IsCurrentSentSnapshot"/>.
/// </summary>
public class InspectionReportSnapshot
{
    [Key]
    public int SnapshotId { get; set; }

    public int ReportId { get; set; }

    public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }

    public string? ExportedSpreadsheetId { get; set; }

    public string? ExportedSpreadsheetUrl { get; set; }

    public int ReportNumber { get; set; }

    /// <summary>
    /// Serialized JSON capturing the report header + all notes (text, status, sub-index, …)
    /// at the moment of send. Format is intentionally simple; do not rely on a strict schema.
    /// </summary>
    public string? SnapshotJson { get; set; }

    /// <summary>
    /// True for the latest "current sent" snapshot of the report.
    /// When a new snapshot is created on re-export, the previous one is flipped to false.
    /// </summary>
    public bool IsCurrentSentSnapshot { get; set; }

    // Navigation
    public virtual InspectionReport Report { get; set; } = null!;
    public virtual Siuser? CreatedByUser { get; set; }
}
