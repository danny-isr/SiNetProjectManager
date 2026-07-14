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

    /// <summary>
    /// Numbered questionnaire tree (Chapter → section → sub-note) for a report.
    /// Excludes Chapter 0 (general fields) and section-level placeholders (fewer than two dots in NoteSubIndex).
    /// </summary>
    Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
        int reportId, CancellationToken cancellationToken = default);

    /// <summary>Chapter 0 general template fields (label + text + Manual override flag).</summary>
    Task<IReadOnlyList<InspectionGeneralFieldRow>> GetGeneralFieldsAsync(
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
    int AttachmentCount,
    string? LastAttachmentUrl);

/// <summary>One Chapter-0 general field row (backed by an InspectionNote without dotted sub-index).</summary>
public sealed record InspectionGeneralFieldRow(
    long NoteId,
    int SectionId,
    string Label,
    string? Text,
    bool IsManualOverride);

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
