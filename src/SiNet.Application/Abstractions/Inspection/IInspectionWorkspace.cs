namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// UI-agnostic port for the new Inspection screen. This is the future seam through which the
/// rebuilt WPF Inspection UI will reach reusable inspection logic (currently embodied by the
/// legacy-extracted services/builders such as <c>IInspectionDrawingManagementService</c>,
/// <c>InspectionReviewedPlanBuilder</c>, etc.). A LegacyBridge adapter will implement this in a
/// later phase; nothing references the legacy stack from the new app yet. No WPF types here.
/// </summary>
public interface IInspectionWorkspace
{
    /// <summary>
    /// Loads the inspection series available for a project, newest first. Intentionally minimal
    /// for the foundation; richer operations (drawings, reviewed plans, notes, report) are added
    /// as each sub-area is migrated off the legacy window.
    /// </summary>
    Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
        int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the inspection reports under a series, newest first. Read-only projection for the new
    /// screen's series detail; no editing/generation/sent-locked behaviour is exposed here.
    /// </summary>
    Task<IReadOnlyList<InspectionReportRow>> GetReportsAsync(
        int projectId, int seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the notes under a report. Read-only projection for the new screen's notes area; no
    /// editing/creation/deletion/reordering or status writes are exposed here.
    /// </summary>
    Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
        int reportId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight, UI-agnostic projection of an inspection series for the new screen's tree/header.
/// </summary>
/// <param name="SeriesId">The series identifier.</param>
/// <param name="Name">Display name of the series.</param>
public readonly record struct InspectionSeriesSummary(int SeriesId, string Name);

/// <summary>
/// Read-only projection of an inspection report row under a series for the new screen's detail list.
/// </summary>
/// <param name="ReportId">The report identifier.</param>
/// <param name="ReportNumber">The sequential report (round) number.</param>
/// <param name="InspectionDate">The inspection date.</param>
/// <param name="InspectorName">The inspector display name, if any.</param>
public readonly record struct InspectionReportRow(
    int ReportId, int ReportNumber, DateTime InspectionDate, string? InspectorName);

/// <summary>
/// Read-only projection of an inspection note row under a report for the new screen's notes list.
/// </summary>
/// <param name="NoteId">The note identifier.</param>
/// <param name="Number">The note sub-index/number (e.g. 1.1.1), if any.</param>
/// <param name="Text">The note text, if any.</param>
/// <param name="Status">The note status, if any.</param>
public readonly record struct InspectionNoteRow(
    long NoteId, string? Number, string? Text, string? Status);
