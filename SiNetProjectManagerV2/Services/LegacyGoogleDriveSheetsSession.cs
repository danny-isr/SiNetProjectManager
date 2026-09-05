using Google.Apis.Drive.v3;
using Google.Apis.Sheets.v4;
using SiNet.Infrastructure.Google.Inspection;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services;

/// <summary>Bridges the V2 Google authentication owner to the shared Inspection exporter.</summary>
internal sealed class LegacyGoogleDriveSheetsSession(GoogleAuthService authService)
    : IGoogleDriveSheetsSession
{
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));

    public DriveService? DriveService => _authService.DriveService;

    public SheetsService? SheetsService => _authService.SheetsService;

    public Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default) =>
        _authService.EnsureAuthenticatedAsync(cancellationToken);
}

/// <summary>Adapts native exporter result DTOs to the legacy V2 Inspection view-model contract.</summary>
internal sealed class LegacyInspectionReportExportServiceAdapter(
    IInspectionGoogleReportExportService inner) : IReportExportService
{
    private readonly IInspectionGoogleReportExportService _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<ReportExportResult> ExportReportAsync(
        int reportId,
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .ExportReportAsync(reportId, templateSpreadsheetId, cancellationToken)
            .ConfigureAwait(false);
        return new ReportExportResult
        {
            DestinationSpreadsheetId = result.DestinationSpreadsheetId,
            DestinationUrl = result.DestinationUrl,
            TagsReplaced = result.TagsReplaced,
            RowsInjected = result.RowsInjected,
            PdfGenerated = result.PdfGenerated,
            Warnings = result.Warnings,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            NoteCellMap = result.NoteCellMap.Select(item => new ExportedNoteCellMap
            {
                NoteId = item.NoteId,
                SectionCode = item.SectionCode,
                NoteSubIndex = item.NoteSubIndex,
                SheetName = item.SheetName,
                ExportedRowIndex = item.ExportedRowIndex,
                ExportedNoteColumnIndex = item.ExportedNoteColumnIndex,
                PlannerResponseColumnIndex = item.PlannerResponseColumnIndex,
            }).ToList(),
        };
    }

    public Task<IReadOnlyList<SiNetSQL.Services.InspectionSync.TemplateScanTag>> ScanTemplateAsync(
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default) =>
        _inner.ScanTemplateAsync(templateSpreadsheetId, cancellationToken);

    public async Task<AnyoneWithLinkShareResult> ShareReportAnyoneWithLinkAsync(
        string spreadsheetId,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .ShareReportAnyoneWithLinkAsync(spreadsheetId, cancellationToken)
            .ConfigureAwait(false);
        return new AnyoneWithLinkShareResult
        {
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            ExistingAnyonePermissionFound = result.ExistingAnyonePermissionFound,
            SpreadsheetUrl = result.SpreadsheetUrl,
        };
    }
}
