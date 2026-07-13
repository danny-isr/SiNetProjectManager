namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// UI-agnostic port for the Inspection screen (read side).
/// </summary>
public interface IInspectionWorkspace
{
    Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
        int projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InspectionReportRow>> GetReportsAsync(
        int projectId, int seriesId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
        int reportId, CancellationToken cancellationToken = default);

    /// <summary>Full report header for the working surface (locked/sent, reviewed version, etc.).</summary>
    Task<InspectionReportDetail?> GetReportDetailAsync(
        int reportId, CancellationToken cancellationToken = default);

    /// <summary>Chapter → section → note tree for a report.</summary>
    Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
        int reportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
        int reportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
        int reportId, CancellationToken cancellationToken = default);
}

public readonly record struct InspectionSeriesSummary(int SeriesId, string Name);

public readonly record struct InspectionReportRow(
    int ReportId, int ReportNumber, DateTime InspectionDate, string? InspectorName);

public readonly record struct InspectionNoteRow(
    long NoteId, string? Number, string? Text, string? Status);

public sealed record InspectionReportDetail(
    int ReportId,
    int ProjectId,
    int? SeriesId,
    int ReportNumber,
    DateTime InspectionDate,
    string? InspectorName,
    string? ReviewedVersion,
    bool IsLockedAfterSend,
    DateTime? SentAt,
    string? SentSpreadsheetUrl,
    string? SourceFileUrn,
    string? SourceFileVersion);

public sealed record InspectionChapterNode(
    int ChapterId,
    int ChapterNumber,
    string Title,
    IReadOnlyList<InspectionSectionNode> Sections);

public sealed record InspectionSectionNode(
    int SectionId,
    int SectionCode,
    string Title,
    IReadOnlyList<InspectionNoteTreeRow> Notes);

public sealed record InspectionNoteTreeRow(
    long NoteId,
    string? Number,
    string? Text,
    string? Status,
    int? StatusId,
    string? PlannerResponseText,
    string? LinkedFileName,
    string? LinkedAlternative,
    string? LinkedVersion,
    int AttachmentCount);

public sealed record InspectionDrawingRow(
    int Id,
    string FileName,
    string SourceFilePath,
    string FileType,
    string StampStatus,
    string? StampedFilePath,
    DateTime? StampedAt);

public sealed record InspectionReviewedFileRow(
    int Id,
    string FileName,
    string? Alternative,
    int SortOrder);
