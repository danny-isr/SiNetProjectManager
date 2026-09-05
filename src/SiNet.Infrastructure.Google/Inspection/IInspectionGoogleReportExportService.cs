using SiNetSQL.Services.InspectionSync;

namespace SiNet.Infrastructure.Google.Inspection;

/// <summary>Google-specific report generation contract shared by native and V2 host adapters.</summary>
public interface IInspectionGoogleReportExportService
{
    Task<GoogleInspectionExportResult> ExportReportAsync(
        int reportId,
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateScanTag>> ScanTemplateAsync(
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default);

    Task<GoogleAnyoneWithLinkShareResult> ShareReportAnyoneWithLinkAsync(
        string spreadsheetId,
        CancellationToken cancellationToken = default);
}

public sealed record GoogleInspectionExportResult
{
    public string? DestinationSpreadsheetId { get; init; }
    public string? DestinationUrl { get; init; }
    public int TagsReplaced { get; init; }
    public int RowsInjected { get; init; }
    public bool PdfGenerated { get; init; }
    public List<string> Warnings { get; init; } = [];
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public List<GoogleExportedNoteCellMap> NoteCellMap { get; init; } = [];
}

public sealed record GoogleAnyoneWithLinkShareResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ExistingAnyonePermissionFound { get; init; }
    public string? SpreadsheetUrl { get; init; }
}

public sealed record GoogleExportedNoteCellMap
{
    public long NoteId { get; init; }
    public string? SectionCode { get; init; }
    public string? NoteSubIndex { get; init; }
    public string? SheetName { get; init; }
    public int ExportedRowIndex { get; init; }
    public int ExportedNoteColumnIndex { get; init; }
    public int PlannerResponseColumnIndex { get; init; }
}
