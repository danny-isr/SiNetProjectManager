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
            var writeDenied = await drive
                .GetWriteAccessFailureReasonAsync(_options.ReportsRootFolderId, cancellationToken)
                .ConfigureAwait(false);
            if (writeDenied is not null)
            {
                _logger.Warn($"[R01] {writeDenied}");
                return MasterPlanReportGenerationResult.Fail(writeDenied);
            }

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

            progress?.Report(("params", "יוצר גיליון פרמטרים...", 60));
            await sheets.WriteParametersSheetAsync(spreadsheetId, request.HourPrice, cancellationToken)
                .ConfigureAwait(false);

            // Keep template header row 2 intact when possible; clear only data body A3:AZ
            // then rewrite the full 31-column Hebrew header (legacy parity).
            await sheets.ClearRangeAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A2:AZ"),
                    cancellationToken)
                .ConfigureAwait(false);

            await sheets.WriteHeadersAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A2"),
                    R01PortfolioRow.GetHeaderRow(),
                    cancellationToken)
                .ConfigureAwait(false);

            var dataRows = new List<IList<object?>>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                // Sheet data starts at row 3.
                dataRows.Add(rows[i].ToSheetRow(rowNumber: i + 3));
            }

            progress?.Report(("write", "כותב נתונים...", 80));
            await sheets.WriteDataBatchedAsync(
                    spreadsheetId,
                    NativeGoogleSheetsWriter.BuildRange("Data", "A3"),
                    dataRows,
                    cancellationToken)
                .ConfigureAwait(false);

            var url = await drive.GetFileUrlAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
            _logger.Info($"[R01] completed rows={rows.Count} cols={R01PortfolioRow.GetHeaderRow().Count} source={rows[0].DataSource} url={url}");
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
