namespace SiNet.Application.Abstractions.Inspection;

public interface IInspectionNoteCommandService
{
    Task<InspectionNoteCommandResult> SaveNoteTextAsync(
        long noteId, string? text, CancellationToken cancellationToken = default);

    Task<InspectionNoteCommandResult> SaveNoteStatusAsync(
        long noteId, int? statusId, string? statusText, CancellationToken cancellationToken = default);

    Task<InspectionNoteCommandResult> AddNoteAsync(
        int reportId, int sectionId, string? text, CancellationToken cancellationToken = default);

    Task<InspectionNoteCommandResult> SetNoteLinkedFileAsync(
        long noteId,
        string? fileName,
        string? alternative,
        string? version,
        CancellationToken cancellationToken = default);

    /// <summary>Persists NoteSubIndex changes after reordering notes in a section.</summary>
    Task<InspectionNoteCommandResult> RenumberNotesAsync(
        IReadOnlyList<(long NoteId, string SubIndex)> renumberings,
        CancellationToken cancellationToken = default);
}

public interface IInspectionReportCommandService
{
    /// <summary>
    /// Creates a new inspection report from a template URL (syncs template structure when the host supports it).
    /// </summary>
    Task<InspectionReportCommandResult> CreateReportAsync(
        int projectId,
        string templateUrl,
        int? seriesId = null,
        string? inspectorName = null,
        int? inspectorId = null,
        string? spreadsheetId = null,
        CancellationToken cancellationToken = default);

    Task<InspectionReportCommandResult> UnlockReportAsync(
        int reportId, CancellationToken cancellationToken = default);

    Task<InspectionReportCommandResult> DeleteReportAsync(
        int reportId, CancellationToken cancellationToken = default);

    Task<InspectionReportCommandResult> SetReviewedVersionAsync(
        int reportId, string? reviewedVersion, CancellationToken cancellationToken = default);

    Task<InspectionReportCommandResult> ReplaceReviewedFilesAsync(
        int reportId,
        IReadOnlyList<InspectionReviewedFileRow> files,
        CancellationToken cancellationToken = default);
}

public interface IInspectionDrawingCommandService
{
    Task<InspectionDrawingCommandResult> AddDrawingAsync(
        int reportId,
        string sourceFilePath,
        string fileName,
        string fileType,
        CancellationToken cancellationToken = default);

    Task<InspectionDrawingCommandResult> RemoveDrawingAsync(
        int drawingId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Host/LegacyBridge seam for Google Sheets export/share until native Drive/Sheets exists.
/// </summary>
public interface IInspectionReportExportPort
{
    Task<InspectionExportResult> ExportAsync(
        int reportId, CancellationToken cancellationToken = default);

    Task<InspectionExportResult> ShareAsync(
        int reportId, CancellationToken cancellationToken = default);

    Task OpenTemplateAsync(int seriesId, CancellationToken cancellationToken = default);
}

public sealed record InspectionNoteCommandResult(bool Succeeded, string? ErrorMessage = null, long? NoteId = null)
{
    public static InspectionNoteCommandResult Ok(long? noteId = null) => new(true, NoteId: noteId);
    public static InspectionNoteCommandResult Fail(string message) => new(false, message);
}

public sealed record InspectionReportCommandResult(bool Succeeded, string? ErrorMessage = null, int? ReportId = null)
{
    public static InspectionReportCommandResult Ok(int? reportId = null) => new(true, ReportId: reportId);
    public static InspectionReportCommandResult Fail(string message) => new(false, message);
}

public sealed record InspectionDrawingCommandResult(bool Succeeded, string? ErrorMessage = null, int? DrawingId = null)
{
    public static InspectionDrawingCommandResult Ok(int? drawingId = null) => new(true, DrawingId: drawingId);
    public static InspectionDrawingCommandResult Fail(string message) => new(false, message);
}

public sealed record InspectionExportResult(bool Succeeded, string? ErrorMessage = null, string? SpreadsheetUrl = null)
{
    public static InspectionExportResult Ok(string? url = null) => new(true, SpreadsheetUrl: url);
    public static InspectionExportResult Fail(string message) => new(false, message);
    public static InspectionExportResult NotAvailable() =>
        Fail("ייצוא Google Sheets עדיין לא מחובר במערכת החדשה.");
}
