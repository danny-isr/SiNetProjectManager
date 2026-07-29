namespace SiNet.Application.MasterPlan.Reports;

/// <summary>One hour-report row for R02 (parity with legacy GoogleConnector R02DataRow).</summary>
public sealed record R02HoursRow(
    int HourReportId,
    DateTime ReportDate,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    decimal Hours,
    string? Description,
    int? EmployeeId,
    string? EmployeeName,
    int? ProjectId,
    string? ProjectNum,
    string? ProjectName,
    int? CustomerId,
    string? CustomerName,
    int? SubContractId,
    string? SubContractNum,
    string? SubContractName,
    int? SubContractStepId,
    string? SubContractStepName,
    string Source)
{
    public string PeriodLabel => ReportDate.ToString("yyyy-MM-dd");

    public static IList<object> GetHeaderRow(bool isClientExport = false)
    {
        if (isClientExport)
        {
            return
            [
                "מספר פרויקט",
                "שם פרויקט",
                "מספר תת-חוזה",
                "שם תת-חוזה",
                "שלב תת-חוזה",
                "תאריך",
                "שעות (עשרוני)",
                "תיאור",
            ];
        }

        return
        [
            "מזהה דיווח",
            "תאריך",
            "שעת התחלה",
            "שעת סיום",
            "שעות (עשרוני)",
            "תיאור",
            "מזהה עובד",
            "שם עובד",
            "מזהה פרויקט",
            "מספר פרויקט",
            "שם פרויקט",
            "מזהה לקוח",
            "שם לקוח",
            "מזהה תת-חוזה",
            "מספר תת-חוזה",
            "שם תת-חוזה",
            "מזהה שלב",
            "שם שלב",
            "תקופה",
            "מקור נתונים",
        ];
    }

    public IList<object?> ToSheetRow(bool isClientExport = false)
    {
        if (isClientExport)
        {
            return
            [
                ProjectNum,
                ProjectName,
                SubContractNum ?? "",
                SubContractName ?? "",
                SubContractStepName ?? "",
                ReportDate.ToString("yyyy-MM-dd"),
                Hours,
                Description ?? "",
            ];
        }

        return
        [
            HourReportId,
            ReportDate.ToString("yyyy-MM-dd"),
            StartTime?.ToString(@"hh\:mm"),
            EndTime?.ToString(@"hh\:mm"),
            Hours,
            Description ?? "",
            EmployeeId,
            EmployeeName,
            ProjectId,
            ProjectNum,
            ProjectName,
            CustomerId,
            CustomerName ?? "",
            SubContractId,
            SubContractNum ?? "",
            SubContractName ?? "",
            SubContractStepId,
            SubContractStepName ?? "",
            PeriodLabel,
            Source,
        ];
    }
}

/// <summary>Merged MasterPlan + Replica hours for R02 (one row per hour report).</summary>
public interface IR02ReportDataSource
{
    Task<IReadOnlyList<R02HoursRow>> GetMergedHoursAsync(
        R02ReportRequest request,
        CancellationToken cancellationToken = default);
}
