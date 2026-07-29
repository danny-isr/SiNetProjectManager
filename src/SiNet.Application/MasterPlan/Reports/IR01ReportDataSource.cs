namespace SiNet.Application.MasterPlan.Reports;

/// <summary>
/// Full R01 portfolio row (parity with GoogleConnector <c>R01DataRow</c> — 32 sheet columns).
/// </summary>
public sealed record R01PortfolioRow(
    int ProjectId,
    string? ProjectNum,
    string? ProjectName,
    bool IsActive,
    DateTime? StartDate,
    DateTime? EndDate,
    int? StatusId,
    string? StatusName,
    int? CustomerId,
    string? CustomerName,
    decimal? FeeSum,
    decimal? OpenBillSum,
    decimal? ApprovedBillSum,
    decimal? Balance,
    DateTime? LastBillDate,
    decimal? HourReported,
    decimal? HourAllotted,
    decimal? ProgressPercentage,
    DateTime? LastUpdated,
    string DataSource)
{
    public decimal? UtilizationPercent =>
        HourAllotted is > 0
            ? Math.Round((HourReported ?? 0m) / HourAllotted.Value * 100m, 1)
            : null;

    public bool IsOverdue => IsActive && EndDate.HasValue && EndDate.Value.Date < DateTime.Today;

    public bool HasNegativeBalance => Balance is < 0;

    public bool IsOverHours =>
        HourAllotted is > 0 && HourReported.HasValue && HourReported > HourAllotted;

    public bool HasNoRecentBilling =>
        IsActive && (!LastBillDate.HasValue || LastBillDate.Value.Date < DateTime.Today.AddDays(-90));

    public bool HasHighOpenBills => OpenBillSum is > 100_000m;

    public int RiskFlagCount =>
        (IsOverdue ? 1 : 0)
        + (HasNegativeBalance ? 1 : 0)
        + (IsOverHours ? 1 : 0)
        + (HasNoRecentBilling ? 1 : 0)
        + (HasHighOpenBills ? 1 : 0);

    /// <summary>Hebrew header row for the Data tab (must stay aligned with <see cref="ToSheetRow"/>).</summary>
    public static IList<object> GetHeaderRow() =>
    [
        "מזהה פרויקט",
        "מספר פרויקט",
        "שם פרויקט",
        "פעיל",
        "תאריך התחלה",
        "תאריך סיום",
        "מזהה סטטוס",
        "שם סטטוס",
        "מזהה לקוח",
        "שם לקוח",
        "סכום שכ\"ט",
        "חשבונות פתוחים",
        "חשבונות מאושרים",
        "מאזן",
        "תאריך חשבון אחרון",
        "שעות מדווחות",
        "שעות מתוכננות",
        "שעות לפי ערך החוזה",
        "שעות לפי מה ששולם עד עכשיו (כולל פתוח)",
        "הפרש שעות בפועל מול שעות לפי תשלום",
        "אחוז ניצול",
        "אחוז התקדמות",
        "באיחור",
        "מאזן שלילי",
        "חריגת שעות",
        "ללא חיוב לאחרונה",
        "חשבונות פתוחים גבוהים",
        "מספר סיכונים",
        "עדכון אחרון",
        "מקור נתונים",
        "תאריך מקור",
    ];

    /// <param name="rowNumber">1-based sheet row (data starts at 3).</param>
    public IList<object?> ToSheetRow(int rowNumber)
    {
        const string colFee = "K";
        const string colBalance = "N";
        const string colHourReported = "P";
        const string colPaid = "S";

        var calcHours = $"=IFERROR({colFee}{rowNumber}/Parameters!$B$1,\"\")";
        var paidHours =
            $"=IFERROR(MAX(0,MIN({colFee}{rowNumber}-{colBalance}{rowNumber},{colFee}{rowNumber}))/Parameters!$B$1,\"\")";
        var hoursDelta = $"=IFERROR({colPaid}{rowNumber}-{colHourReported}{rowNumber},\"\")";

        return
        [
            ProjectId,
            ProjectNum,
            ProjectName,
            IsActive ? "כן" : "לא",
            StartDate?.ToString("yyyy-MM-dd"),
            EndDate?.ToString("yyyy-MM-dd"),
            StatusId,
            StatusName,
            CustomerId,
            CustomerName,
            FeeSum,
            OpenBillSum,
            ApprovedBillSum,
            Balance,
            LastBillDate?.ToString("yyyy-MM-dd"),
            HourReported,
            HourAllotted,
            calcHours,
            paidHours,
            hoursDelta,
            UtilizationPercent,
            ProgressPercentage,
            IsOverdue ? "כן" : "",
            HasNegativeBalance ? "כן" : "",
            IsOverHours ? "כן" : "",
            HasNoRecentBilling ? "כן" : "",
            HasHighOpenBills ? "כן" : "",
            RiskFlagCount,
            LastUpdated?.ToString("yyyy-MM-dd HH:mm"),
            DataSource,
            null,
        ];
    }
}

/// <summary>Replica / MasterPlan portfolio rows for R01.</summary>
public interface IR01ReportDataSource
{
    Task<IReadOnlyList<R01PortfolioRow>> GetPortfolioAsync(
        R01ReportRequest request,
        CancellationToken cancellationToken = default);
}
