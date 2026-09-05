using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Inspection;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Google.Inspection;

/// <summary>
/// Native Standalone Inspection export/share adapter over the canonical Google Sheets exporter.
/// Export persists only the artifact identity; sending and locking remain email lifecycle concerns.
/// </summary>
internal sealed partial class GoogleSheetsInspectionReportExportPort(
    IInspectionGoogleReportExportService exportService,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ILogger<GoogleSheetsInspectionReportExportPort>? logger = null)
    : IInspectionReportExportPort
{
    private readonly IInspectionGoogleReportExportService _exportService =
        exportService ?? throw new ArgumentNullException(nameof(exportService));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ILogger<GoogleSheetsInspectionReportExportPort>? _logger = logger;

    public async Task<InspectionExportResult> ExportAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var report = await context.InspectionReports
                .SingleOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
                .ConfigureAwait(false);
            if (report is null)
            {
                return InspectionExportResult.Fail($"דוח {reportId} לא נמצא.");
            }

            var templateId = ExtractSpreadsheetId(report.SourceFileUrn);
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return InspectionExportResult.Fail("לא נמצא מזהה תבנית בדוח.");
            }

            var result = await _exportService
                .ExportReportAsync(reportId, templateId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return InspectionExportResult.Fail(result.ErrorMessage ?? "ייצוא נכשל.");
            }

            if (string.IsNullOrWhiteSpace(result.DestinationSpreadsheetId)
                || string.IsNullOrWhiteSpace(result.DestinationUrl))
            {
                return InspectionExportResult.Fail("הייצוא הסתיים ללא מזהה או כתובת גיליון.");
            }

            report.SentSpreadsheetId = result.DestinationSpreadsheetId;
            report.SentSpreadsheetUrl = result.DestinationUrl;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Inspection report {ReportId} exported to spreadsheet {SpreadsheetId}; send and lock state unchanged.",
                reportId,
                result.DestinationSpreadsheetId);

            return InspectionExportResult.Ok(result.DestinationUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Inspection report export failed for report {ReportId}.", reportId);
            return InspectionExportResult.Fail($"שגיאת ייצוא: {ex.Message}");
        }
    }

    public async Task<InspectionExportResult> ShareAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var artifact = await context.InspectionReports
                .AsNoTracking()
                .Where(r => r.ReportId == reportId)
                .Select(r => new { r.SentSpreadsheetId, r.SentSpreadsheetUrl })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (artifact is null)
            {
                return InspectionExportResult.Fail($"דוח {reportId} לא נמצא.");
            }

            var spreadsheetId = ExtractSpreadsheetId(artifact.SentSpreadsheetId)
                ?? ExtractSpreadsheetId(artifact.SentSpreadsheetUrl);
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                return InspectionExportResult.Fail("אין גיליון מיוצא לשיתוף — ייצא דוח קודם.");
            }

            var result = await _exportService
                .ShareReportAnyoneWithLinkAsync(spreadsheetId, cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess
                ? InspectionExportResult.Ok(result.SpreadsheetUrl)
                : InspectionExportResult.Fail(result.ErrorMessage ?? "שיתוף נכשל.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Inspection report share failed for report {ReportId}.", reportId);
            return InspectionExportResult.Fail($"שגיאת שיתוף: {ex.Message}");
        }
    }

    public Task OpenTemplateAsync(
        int seriesId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    internal static string? ExtractSpreadsheetId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            return null;
        }

        if (!urlOrId.Contains('/'))
        {
            return urlOrId;
        }

        var match = SpreadsheetIdRegex().Match(urlOrId);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"/spreadsheets/d/([a-zA-Z0-9_-]+)")]
    private static partial Regex SpreadsheetIdRegex();
}
