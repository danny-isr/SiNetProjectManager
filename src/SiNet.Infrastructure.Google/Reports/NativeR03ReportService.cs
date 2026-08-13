using SiNet.Application.Abstractions.Logging;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Google.Reports;

public sealed class NativeR03ReportService(
    GmailClientProvider gmailClientProvider,
    GmailOptions options,
    IR03ReportDataSource dataSource,
    IAppLogger logger) : IMasterPlanR03ReportService
{
    private readonly GmailClientProvider _gmail = gmailClientProvider ?? throw new ArgumentNullException(nameof(gmailClientProvider));
    private readonly GmailOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IR03ReportDataSource _data = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<IReadOnlyList<R03EmployeeInfo>> GetEmployeesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
        => _data.GetEmployeesAsync(activeOnly, cancellationToken);

    public async Task<R03PreviewResult> PreviewAsync(
        R03ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = request.Validate();
        if (errors.Count > 0)
            return R03PreviewResult.Fail(string.Join("\n", errors));

        try
        {
            var attendanceTask = _data.GetAttendanceHoursAsync(request, cancellationToken);
            var reportedTask = _data.GetReportedHoursAsync(request, cancellationToken);
            await Task.WhenAll(attendanceTask, reportedTask).ConfigureAwait(false);

            var employeeSheets = BuildEmployeeSheets(request, attendanceTask.Result, reportedTask.Result);
            if (employeeSheets.Count == 0)
                return R03PreviewResult.Fail("לא נמצאו נתונים עבור החודש שנבחר.");

            var rows = new List<R03DailyPreviewRow>();
            foreach (var emp in employeeSheets)
            {
                foreach (var day in emp.Days)
                {
                    rows.Add(new R03DailyPreviewRow(
                        emp.Id,
                        emp.Name,
                        day.Date,
                        day.DayName,
                        Math.Round(day.Attendance, 2),
                        Math.Round(day.Reported, 2)));
                }
            }

            var totalAtt = employeeSheets.Sum(e => e.Days.Sum(d => d.Attendance));
            var totalRep = employeeSheets.Sum(e => e.Days.Sum(d => d.Reported));
            _logger.Info($"[R03] preview rows={rows.Count} employees={employeeSheets.Count}");
            return R03PreviewResult.Ok(rows, Math.Round(totalAtt, 2), Math.Round(totalRep, 2));
        }
        catch (OperationCanceledException)
        {
            return R03PreviewResult.Fail("הפעולה בוטלה.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[R03] preview failed: {ex.Message}", ex);
            return R03PreviewResult.Fail("שגיאה: " + ex.Message);
        }
    }

    public async Task<MasterPlanReportGenerationResult> GenerateAsync(
        R03ReportRequest request,
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
            var writeDenied = await drive
                .GetWriteAccessFailureReasonAsync(_options.ReportsRootFolderId, cancellationToken)
                .ConfigureAwait(false);
            if (writeDenied is not null)
            {
                _logger.Warn($"[R03] {writeDenied}");
                return MasterPlanReportGenerationResult.Fail(writeDenied);
            }

            progress?.Report(("data", "מושך נתוני נוכחות ודיווח...", 15));
            var attendanceTask = _data.GetAttendanceHoursAsync(request, cancellationToken);
            var reportedTask = _data.GetReportedHoursAsync(request, cancellationToken);
            await Task.WhenAll(attendanceTask, reportedTask).ConfigureAwait(false);

            var employeeSheets = BuildEmployeeSheets(request, attendanceTask.Result, reportedTask.Result);
            if (employeeSheets.Count == 0)
                return MasterPlanReportGenerationResult.Fail("לא נמצאו נתונים עבור החודש שנבחר.");

            progress?.Report(("folder", "יוצר תיקיות...", 40));
            var folderId = await drive.EnsureFolderPathAsync(
                    ["דוחות", "R03 - השוואת נוכחות", "הנהלה"],
                    _options.ReportsRootFolderId,
                    cancellationToken)
                .ConfigureAwait(false);

            var fileName = $"R03 - השוואת נוכחות - {request.MonthDisplayName} {request.Year}";
            progress?.Report(("sheet", "מחפש/יוצר גיליון...", 50));
            var spreadsheetId = await drive.FindFileAsync(fileName, folderId, cancellationToken).ConfigureAwait(false)
                                ?? await drive.CreateSpreadsheetAsync(fileName, folderId, cancellationToken)
                                    .ConfigureAwait(false);

            for (var i = 0; i < employeeSheets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var emp = employeeSheets[i];
                var pct = 55 + (int)(40.0 * (i + 1) / employeeSheets.Count);
                progress?.Report(("write", $"כותב {emp.Name} ({i + 1}/{employeeSheets.Count})...", pct));
                await WriteEmployeeSheetAsync(sheets, spreadsheetId, emp, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(("summary", "כותב סיכום...", 96));
            await WriteSummaryAsync(sheets, spreadsheetId, employeeSheets, request, cancellationToken)
                .ConfigureAwait(false);

            await sheets.ApplyHebrewPresentationAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);

            var url = await drive.GetFileUrlAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
            var rowCount = employeeSheets.Sum(e => e.Days.Count);
            _logger.Info($"[R03] completed employees={employeeSheets.Count} url={url}");
            return MasterPlanReportGenerationResult.Ok(spreadsheetId, fileName, url, rowCount);
        }
        catch (OperationCanceledException)
        {
            return MasterPlanReportGenerationResult.Fail("הפעולה בוטלה.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[R03] failed: {ex.Message}", ex);
            return MasterPlanReportGenerationResult.Fail("שגיאה: " + ex.Message);
        }
    }

    private sealed record EmpSheet(int Id, string Name, List<DayRow> Days);

    private sealed record DayRow(DateTime Date, string DayName, decimal Attendance, decimal Reported)
    {
        public decimal Diff => Reported - Attendance;
    }

    private static List<EmpSheet> BuildEmployeeSheets(
        R03ReportRequest request,
        IReadOnlyList<R03AttendanceRow> attendance,
        IReadOnlyList<R03ReportedRow> reported)
    {
        var hebrewDays = new[] { "א'", "ב'", "ג'", "ד'", "ה'", "ו'", "ש'" };
        var attendLookup = attendance.ToDictionary(r => (r.EmployeeId, r.ReportDate.Date), r => r.TotalHours);
        var reportLookup = reported.ToDictionary(r => (r.EmployeeId, r.ReportDate.Date), r => r.TotalHours);
        var names = new Dictionary<int, string>();
        foreach (var r in attendance)
            names.TryAdd(r.EmployeeId, r.EmployeeName);
        foreach (var r in reported)
            names.TryAdd(r.EmployeeId, r.EmployeeName);

        var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);
        var dates = Enumerable.Range(1, daysInMonth).Select(d => new DateTime(request.Year, request.Month, d)).ToList();

        var sheets = new List<EmpSheet>();
        foreach (var (empId, empName) in names.OrderBy(kv => kv.Value))
        {
            var days = dates.Select(date => new DayRow(
                date,
                hebrewDays[(int)date.DayOfWeek],
                attendLookup.GetValueOrDefault((empId, date)),
                reportLookup.GetValueOrDefault((empId, date)))).ToList();
            sheets.Add(new EmpSheet(empId, empName, days));
        }

        return sheets;
    }

    private static async Task WriteEmployeeSheetAsync(
        NativeGoogleSheetsWriter sheets,
        string spreadsheetId,
        EmpSheet emp,
        CancellationToken cancellationToken)
    {
        var tab = string.IsNullOrWhiteSpace(emp.Name) ? $"Employee_{emp.Id}" : emp.Name;
        await sheets.EnsureSheetExistsAsync(spreadsheetId, tab, cancellationToken).ConfigureAwait(false);
        // Bound clear — do not clear A:F on a huge pre-allocated grid (API cell churn).
        await sheets.ClearRangeAsync(spreadsheetId, NativeGoogleSheetsWriter.BuildRange(tab, "A1:F200"), cancellationToken)
            .ConfigureAwait(false);
        await sheets.WriteHeadersAsync(
                spreadsheetId,
                NativeGoogleSheetsWriter.BuildRange(tab, "A1"),
                new List<object> { "תאריך", "יום", "שעות נוכחות", "שעות מדווחות", "הפרש" },
                cancellationToken)
            .ConfigureAwait(false);

        var rows = emp.Days.Select(d => (IList<object?>)new List<object?>
        {
            d.Date.ToString("dd/MM/yyyy"),
            d.DayName,
            Math.Round(d.Attendance, 2),
            Math.Round(d.Reported, 2),
            Math.Round(d.Diff, 2),
        }).ToList();

        rows.Add(new List<object?>
        {
            "סה\"כ",
            "",
            Math.Round(emp.Days.Sum(d => d.Attendance), 2),
            Math.Round(emp.Days.Sum(d => d.Reported), 2),
            Math.Round(emp.Days.Sum(d => d.Diff), 2),
        });

        await sheets.WriteDataBatchedAsync(
                spreadsheetId,
                NativeGoogleSheetsWriter.BuildRange(tab, "A2"),
                rows,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteSummaryAsync(
        NativeGoogleSheetsWriter sheets,
        string spreadsheetId,
        List<EmpSheet> employees,
        R03ReportRequest request,
        CancellationToken cancellationToken)
    {
        const string tab = "סיכום";
        await sheets.EnsureSheetExistsAsync(spreadsheetId, tab, cancellationToken).ConfigureAwait(false);
        await sheets.ClearRangeAsync(spreadsheetId, NativeGoogleSheetsWriter.BuildRange(tab, "A1:D200"), cancellationToken)
            .ConfigureAwait(false);

        await sheets.WriteValuesAsync(
                spreadsheetId,
                NativeGoogleSheetsWriter.BuildRange(tab, "A1"),
                new List<IList<object>>
                {
                    new List<object> { $"R03 — השוואת נוכחות מול דיווח — {request.MonthDisplayName} {request.Year}" },
                },
                cancellationToken)
            .ConfigureAwait(false);

        await sheets.WriteHeadersAsync(
                spreadsheetId,
                NativeGoogleSheetsWriter.BuildRange(tab, "A3"),
                new List<object> { "עובד", "שעות נוכחות", "שעות מדווחות", "הפרש" },
                cancellationToken)
            .ConfigureAwait(false);

        var rows = employees.Select(e => (IList<object?>)new List<object?>
        {
            e.Name,
            Math.Round(e.Days.Sum(d => d.Attendance), 2),
            Math.Round(e.Days.Sum(d => d.Reported), 2),
            Math.Round(e.Days.Sum(d => d.Diff), 2),
        }).ToList();

        rows.Add(new List<object?>
        {
            "סה\"כ",
            Math.Round(employees.Sum(e => e.Days.Sum(d => d.Attendance)), 2),
            Math.Round(employees.Sum(e => e.Days.Sum(d => d.Reported)), 2),
            Math.Round(employees.Sum(e => e.Days.Sum(d => d.Diff)), 2),
        });

        await sheets.WriteDataBatchedAsync(
                spreadsheetId,
                NativeGoogleSheetsWriter.BuildRange(tab, "A4"),
                rows,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
