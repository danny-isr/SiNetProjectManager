using SiNet.Application.Abstractions.Logging;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Google.Reports;

public sealed class NativeR02ReportService(
    GmailClientProvider gmailClientProvider,
    GmailOptions options,
    IR02ReportDataSource dataSource,
    IAppLogger logger) : IMasterPlanR02ReportService
{
    private readonly GmailClientProvider _gmail = gmailClientProvider ?? throw new ArgumentNullException(nameof(gmailClientProvider));
    private readonly GmailOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IR02ReportDataSource _data = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<MasterPlanReportGenerationResult> GenerateAsync(
        R02ReportRequest request,
        IProgress<(string Phase, string Message, int Percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = request.Validate();
        if (errors.Count > 0)
            return MasterPlanReportGenerationResult.Fail(string.Join("\n", errors));

        if (!_options.IsReportsConfigured)
            return MasterPlanReportGenerationResult.Fail("GoogleReports לא מוגדר (SharedDriveId / RootReportsFolderId).");

        try
        {
            progress?.Report(("auth", "מתחבר ל-Google...", 5));
            var driveApi = await _gmail.TryGetDriveServiceAsync(cancellationToken).ConfigureAwait(false);
            var sheetsApi = await _gmail.TryGetSheetsServiceAsync(cancellationToken).ConfigureAwait(false);
            if (driveApi is null || sheetsApi is null)
                return MasterPlanReportGenerationResult.Fail(
                    "אין חיבור Google. התחבר מחדש (ייתכן שנדרש אישור Spreadsheets).");

            var drive = new NativeReportsDriveHelper(driveApi, _options.EffectiveReportsSharedDriveId!);
            var sheets = new NativeGoogleSheetsWriter(sheetsApi, _options.ReportsBatchSize, _options.ReportsBatchDelayMs);

            progress?.Report(("access", "בודק הרשאות...", 10));
            if (!await drive.CheckWriteAccessAsync(cancellationToken).ConfigureAwait(false))
                return MasterPlanReportGenerationResult.Fail("אין הרשאות כתיבה ל-Shared Drive.");

            progress?.Report(("data", "מושך שעות עבודה...", 25));
            var rows = await _data.GetMergedHoursAsync(request, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
                return MasterPlanReportGenerationResult.Fail("לא נמצאו שעות בטווח שנבחר.");

            var stamp = $"{request.StartDate:yyyyMMdd}-{request.EndDate:yyyyMMdd}_{DateTime.Now:HHmmss}";
            var suffix = request.IsClientExport ? "_Client" : "";
            var fileName = $"R02_Hours_All_{stamp}{suffix}";
            var folderId = await drive.EnsureFolderPathAsync(
                    ["Reports", "R02-Hours", "_All", request.StartDate.ToString("yyyy-MM")],
                    _options.ReportsRootFolderId,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(("sheet", "יוצר גיליון...", 50));
            string spreadsheetId;
            if (!string.IsNullOrWhiteSpace(_options.R02TemplateSpreadsheetId))
            {
                spreadsheetId = await drive.CopyTemplateAsync(
                        _options.R02TemplateSpreadsheetId!,
                        fileName,
                        folderId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                spreadsheetId = await drive.CreateSpreadsheetAsync(fileName, folderId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await sheets.EnsureSheetNamedDataAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
            await sheets.ClearRangeAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A2:AZ"),
                    cancellationToken)
                .ConfigureAwait(false);

            await sheets.WriteHeadersAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A2"),
                    new List<object>
                    {
                        "Date", "ProjectID", "ProjectNum", "ProjectName",
                        "EmployeeID", "EmployeeName", "Hours", "Source",
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var dataRows = rows.Select(r => (IList<object?>)new List<object?>
            {
                r.ReportDate.ToString("yyyy-MM-dd"),
                r.ProjectId,
                r.ProjectNum,
                r.ProjectName,
                r.EmployeeId,
                r.EmployeeName,
                Math.Round(r.Hours, 2),
                r.Source,
            }).ToList();

            progress?.Report(("write", "כותב נתונים...", 80));
            await sheets.WriteDataBatchedAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A3"),
                    dataRows,
                    cancellationToken)
                .ConfigureAwait(false);

            var url = await drive.GetFileUrlAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
            _logger.Info($"[R02] completed rows={rows.Count} url={url}");
            return MasterPlanReportGenerationResult.Ok(spreadsheetId, fileName, url, rows.Count);
        }
        catch (OperationCanceledException)
        {
            return MasterPlanReportGenerationResult.Fail("הפעולה בוטלה.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[R02] failed: {ex.Message}", ex);
            return MasterPlanReportGenerationResult.Fail("שגיאה: " + ex.Message);
        }
    }
}
