using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services;

/// <summary>Lists Google Sheets templates from the admin-configured Drive folder.</summary>
internal sealed class V2InspectionTemplateCatalog(
    GoogleAuthService authService,
    ISystemSettingsQueryService settings) : IInspectionTemplateCatalog
{
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folderId = dto.Inspection.InspectionTemplatesFolderId;
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return [];
        }

        var provider = new GoogleInspectionTemplateProvider(_authService);
        var items = await provider
            .GetAvailableTemplatesAsync(folderId, cancellationToken)
            .ConfigureAwait(false);

        return items
            .Select(t => new InspectionTemplateCatalogItem(t.Name, t.SpreadsheetId, t.Url))
            .ToList();
    }
}

/// <summary>
/// V2 host bridge: create-report via legacy Google template sync + <see cref="IInspectionReportService"/>;
/// other commands forward to <see cref="SqlInspectionReportCommandService"/>.
/// </summary>
internal sealed class V2InspectionReportCommandService(
    SqlInspectionReportCommandService inner,
    IInspectionReportService reportService,
    TemplateSyncService templateSync,
    GoogleAuthService authService) : IInspectionReportCommandService
{
    private readonly SqlInspectionReportCommandService _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IInspectionReportService _reportService =
        reportService ?? throw new ArgumentNullException(nameof(reportService));
    private readonly TemplateSyncService _templateSync =
        templateSync ?? throw new ArgumentNullException(nameof(templateSync));
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));

    public async Task<InspectionReportCommandResult> CreateReportAsync(
        int projectId,
        string templateUrl,
        int? seriesId = null,
        string? inspectorName = null,
        int? inspectorId = null,
        string? spreadsheetId = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return InspectionReportCommandResult.Fail("לא נבחר פרויקט.");
        }

        if (string.IsNullOrWhiteSpace(templateUrl))
        {
            return InspectionReportCommandResult.Fail("יש לבחור תבנית לפני יצירת דוח.");
        }

        var sheetId = spreadsheetId;
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            sheetId = ExtractSpreadsheetId(templateUrl);
        }

        if (string.IsNullOrWhiteSpace(sheetId))
        {
            return InspectionReportCommandResult.Fail("לא ניתן לחלץ מזהה תבנית מהקישור.");
        }

        try
        {
            var provider = new GoogleInspectionTemplateProvider(_authService);

            var resolvedSeriesId = seriesId;
            if (resolvedSeriesId is null or <= 0)
            {
                resolvedSeriesId = await _templateSync
                    .EnsureSeriesAsync(projectId, sheetId, templateUrl, cancellationToken)
                    .ConfigureAwait(false);
            }

            var scanResult = await provider
                .ScanAndParseTemplateAsync(sheetId, cancellationToken)
                .ConfigureAwait(false);

            if (scanResult.HasErrors)
            {
                var summary = string.Join("; ", scanResult.ValidationErrors.Take(3).Select(e => e.Message));
                return InspectionReportCommandResult.Fail($"שגיאות בתבנית: {summary}");
            }

            if (scanResult.SyncRows.Count == 0)
            {
                return InspectionReportCommandResult.Fail(
                    "לא נמצאו שורות בתבנית — ודא שהגיליון מכיל פרקים וסעיפים.");
            }

            var syncResult = await _templateSync
                .SyncAsync(scanResult.SyncRows, resolvedSeriesId, cancellationToken)
                .ConfigureAwait(false);

            if (syncResult.HasErrors)
            {
                var summary = string.Join("; ", syncResult.Errors.Take(3));
                return InspectionReportCommandResult.Fail($"שגיאות בסנכרון תבנית: {summary}");
            }

            var report = await _reportService
                .CreateReportAsync(
                    projectId,
                    templateUrl,
                    inspectorName,
                    cancellationToken,
                    inspectorId,
                    resolvedSeriesId)
                .ConfigureAwait(false);

            return InspectionReportCommandResult.Ok(report.ReportId);
        }
        catch (Exception ex)
        {
            return InspectionReportCommandResult.Fail($"שגיאה ביצירת דוח: {ex.Message}");
        }
    }

    public Task<InspectionReportCommandResult> UnlockReportAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        _inner.UnlockReportAsync(reportId, cancellationToken);

    public Task<InspectionReportCommandResult> DeleteReportAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        _inner.DeleteReportAsync(reportId, cancellationToken);

    public Task<InspectionReportCommandResult> SetReviewedVersionAsync(
        int reportId, string? reviewedVersion, CancellationToken cancellationToken = default) =>
        _inner.SetReviewedVersionAsync(reportId, reviewedVersion, cancellationToken);

    public Task<InspectionReportCommandResult> ReplaceReviewedFilesAsync(
        int reportId,
        IReadOnlyList<InspectionReviewedFileRow> files,
        CancellationToken cancellationToken = default) =>
        _inner.ReplaceReviewedFilesAsync(reportId, files, cancellationToken);

    private static string? ExtractSpreadsheetId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            return null;
        }

        if (!urlOrId.Contains('/'))
        {
            return urlOrId;
        }

        var match = Regex.Match(urlOrId, @"/spreadsheets/d/([a-zA-Z0-9_-]+)");
        return match.Success ? match.Groups[1].Value : urlOrId;
    }
}

/// <summary>V2 host adapters for Inspection file picker / email / screenshot / export seams.</summary>
internal sealed class V2InspectionFileTreePickerHost : IInspectionFileTreePickerHost
{
    public Task<InspectionFilePickResult?> PickReviewedPlanAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionFilePickResult?>(null);

    public Task<InspectionFilePickResult?> PickNoteLinkedFileAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionFilePickResult?>(null);
}

internal sealed class V2InspectionReportEmailHost : IInspectionReportEmailHost
{
    public Task<bool> SendReportEmailAsync(int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class V2InspectionNoteScreenshotHost : IInspectionNoteScreenshotHost
{
    public Task<InspectionScreenshotUploadResult> UploadFromClipboardAsync(
        long noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionScreenshotUploadResult.Fail("העלאת צילום מסך תחובר בסלייס הבא."));
}

internal sealed class V2InspectionReportExportPort(
    GoogleAuthService authService,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ISystemSettingsQueryService settings,
    IInspectionReportService reportService,
    ILoggerFactory? loggerFactory = null) : IInspectionReportExportPort
{
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IInspectionReportService _reportService =
        reportService ?? throw new ArgumentNullException(nameof(reportService));
    private readonly ILoggerFactory? _loggerFactory = loggerFactory;

    public async Task<InspectionExportResult> ExportAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _reportService
                .GetReportWithSeriesAsync(reportId, cancellationToken)
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

            var export = await CreateExportServiceAsync(cancellationToken).ConfigureAwait(false);
            var result = await export
                .ExportReportAsync(reportId, templateId, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? InspectionExportResult.Ok(result.DestinationUrl)
                : InspectionExportResult.Fail(result.ErrorMessage ?? "ייצוא נכשל.");
        }
        catch (Exception ex)
        {
            return InspectionExportResult.Fail($"שגיאת ייצוא: {ex.Message}");
        }
    }

    public async Task<InspectionExportResult> ShareAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _reportService
                .GetReportWithSeriesAsync(reportId, cancellationToken)
                .ConfigureAwait(false);
            if (report is null)
            {
                return InspectionExportResult.Fail($"דוח {reportId} לא נמצא.");
            }

            var spreadsheetId = ExtractSpreadsheetId(report.SentSpreadsheetId)
                ?? ExtractSpreadsheetId(report.SentSpreadsheetUrl);
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                return InspectionExportResult.Fail("אין גיליון מיוצא לשיתוף — ייצא דוח קודם.");
            }

            var export = await CreateExportServiceAsync(cancellationToken).ConfigureAwait(false);
            var result = await export
                .ShareReportAnyoneWithLinkAsync(spreadsheetId, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? InspectionExportResult.Ok(result.SpreadsheetUrl)
                : InspectionExportResult.Fail(result.ErrorMessage ?? "שיתוף נכשל.");
        }
        catch (Exception ex)
        {
            return InspectionExportResult.Fail($"שגיאת שיתוף: {ex.Message}");
        }
    }

    public Task OpenTemplateAsync(int seriesId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private async Task<GoogleReportExportService> CreateExportServiceAsync(CancellationToken cancellationToken)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var logger = _loggerFactory?.CreateLogger<GoogleReportExportService>();
        return new GoogleReportExportService(_authService, _dbContextFactory, logger)
        {
            ReportsFolderId = dto.Inspection.InspectionReportsFolderId,
        };
    }

    private static string? ExtractSpreadsheetId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            return null;
        }

        if (!urlOrId.Contains('/'))
        {
            return urlOrId;
        }

        var match = Regex.Match(urlOrId, @"/spreadsheets/d/([a-zA-Z0-9_-]+)");
        return match.Success ? match.Groups[1].Value : urlOrId;
    }
}
