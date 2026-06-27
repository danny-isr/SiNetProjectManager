namespace SiNetSQL.Models;

/// <summary>
/// An inspection report for a project. Each report is a versioned inspection instance
/// identified by <see cref="ReportNumber"/> and links to an ACC source file via <see cref="SourceFileUrn"/>.
/// </summary>
public class InspectionReport
{
    public int ReportId { get; set; }

    /// <summary>
    /// Legacy direct FK to Project. Will be removed after data migration to <see cref="SeriesId"/>.
    /// </summary>
    public int ProjectId { get; set; }

    /// <summary>
    /// FK to the <see cref="InspectionSeries"/> that groups reports of the same type.
    /// Nullable during migration — will become required after data migration.
    /// </summary>
    public int? SeriesId { get; set; }

    public int ReportNumber { get; set; }

    public DateTime InspectionDate { get; set; }

    /// <summary>
    /// Legacy free-text inspector name. Will be removed after data migration to <see cref="InspectorId"/>.
    /// </summary>
    public string? InspectorName { get; set; }

    /// <summary>
    /// FK to the <see cref="Siuser"/> who performed the inspection.
    /// Nullable during migration — will become required after data migration.
    /// </summary>
    public int? InspectorId { get; set; }

    public string? SourceFileUrn { get; set; }

    public string? SourceFileVersion { get; set; }

    // ── Reviewed plan (logical, populated from Work Window 2 / IActiveFileQueryService) ──

    /// <summary>
    /// The single plan/file version this whole report is reviewing
    /// (e.g. "1", "2", "A"). Logical only — no path / ACC / viewer info.
    /// Required before export. NOT copied to the next round.
    /// </summary>
    public string? ReviewedVersion { get; set; }

    // ── Sent / Locked state ──
    /// <summary>UTC time the report was successfully exported (= "sent").</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>FK to <see cref="Siuser"/> who triggered the export.</summary>
    public int? SentByUserId { get; set; }

    /// <summary>Google Spreadsheet ID of the exported (sent) file.</summary>
    public string? SentSpreadsheetId { get; set; }

    /// <summary>Web URL of the exported (sent) file.</summary>
    public string? SentSpreadsheetUrl { get; set; }

    /// <summary>
    /// When true, notes belonging to this report are read-only.
    /// Set automatically on successful export. Can be cleared via
    /// <c>InspectionReportService.UnlockReportAsync</c> after explicit user confirmation.
    /// </summary>
    public bool IsLockedAfterSend { get; set; }

    /// <summary>UTC time of the most recent send-snapshot (mirrors <see cref="SentAt"/>).</summary>
    public DateTime? LastSnapshotAt { get; set; }

    // Navigation
    public virtual Project Project { get; set; } = null!;

    public virtual InspectionSeries? Series { get; set; }

    public virtual Siuser? Inspector { get; set; }

    public virtual Siuser? SentByUser { get; set; }

    public virtual ICollection<InspectionNote> InspectionNotes { get; set; } = new List<InspectionNote>();

    public virtual ICollection<InspectionReportDrawing> Drawings { get; set; } = new List<InspectionReportDrawing>();

    public virtual ICollection<InspectionReportSnapshot> Snapshots { get; set; } = new List<InspectionReportSnapshot>();

    /// <summary>
    /// Logical list of files / sheets / alternatives that make up the "reviewed plan"
    /// for this report. Version comes from <see cref="ReviewedVersion"/> at the report level.
    /// </summary>
    public virtual ICollection<InspectionReportReviewedFile> ReviewedFiles { get; set; }
        = new List<InspectionReportReviewedFile>();
}
