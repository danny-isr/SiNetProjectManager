using SiNet.Application.Abstractions.Logging;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Google.Reports;

public sealed class NativeR01ReportService(
    GmailClientProvider gmailClientProvider,
    GmailOptions options,
    IR01ReportDataSource dataSource,
    IAppLogger logger) : IMasterPlanR01ReportService
{
    private readonly GmailClientProvider _gmail = gmailClientProvider ?? throw new ArgumentNullException(nameof(gmailClientProvider));
    private readonly GmailOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IR01ReportDataSource _data = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<MasterPlanReportGenerationResult> GenerateAsync(
        R01ReportRequest request,
        IProgress<(string Phase, string Message, int Percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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

            progress?.Report(("data", "מושך תיק פרויקטים...", 25));
            var rows = await _data.GetPortfolioAsync(request, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
                return MasterPlanReportGenerationResult.Fail("לא נמצאו פרויקטים לדוח.");

            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var fileName = $"R01_Portfolio_All_{stamp}";
            var folderId = await drive.EnsureFolderPathAsync(
                    ["Reports", "R01-Portfolio", "_All", DateTime.Now.ToString("yyyy-MM")],
                    _options.ReportsRootFolderId,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(("sheet", "יוצר גיליון...", 50));
            string spreadsheetId;
            if (!string.IsNullOrWhiteSpace(_options.R01TemplateSpreadsheetId))
            {
                spreadsheetId = await drive.CopyTemplateAsync(
                        _options.R01TemplateSpreadsheetId!,
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
                        "ProjectID", "ProjectNum", "ProjectName", "Customer", "Status",
                        "FeeSum", "HoursSum", "Active", "HourPrice",
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var dataRows = rows.Select(r => (IList<object?>)new List<object?>
            {
                r.ProjectId,
                r.ProjectNum,
                r.ProjectName,
                r.CustomerName,
                r.StatusName,
                r.FeeSum,
                r.HoursSum,
                r.IsActive ? 1 : 0,
                request.HourPrice,
            }).ToList();

            progress?.Report(("write", "כותב נתונים...", 80));
            await sheets.WriteDataBatchedAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A3"),
                    dataRows,
                    cancellationToken)
                .ConfigureAwait(false);

            var url = await drive.GetFileUrlAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
            _logger.Info($"[R01] completed rows={rows.Count} url={url}");
            return MasterPlanReportGenerationResult.Ok(spreadsheetId, fileName, url, rows.Count);
        }
        catch (OperationCanceledException)
        {
            return MasterPlanReportGenerationResult.Fail("הפעולה בוטלה.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[R01] failed: {ex.Message}", ex);
            return MasterPlanReportGenerationResult.Fail("שגיאה: " + ex.Message);
        }
    }
}
