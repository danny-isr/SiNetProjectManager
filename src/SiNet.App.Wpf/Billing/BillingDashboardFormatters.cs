using SiNet.Application.Billing;

namespace SiNet.App.Wpf.Billing;

/// <summary>Presentation formatters for the read-only billing dashboard. No candidate resolver.</summary>
public static class BillingDashboardFormatters
{
    public const string EmDash = "—";
    public const string UnknownSnapshotDate = "תאריך snapshot לא ידוע";
    public const string BlockedHeadline = "נתוני MasterPlan אינם עדכניים ולכן לא מוצגות המלצות לחיוב.";
    public const string ReceivedThisMonthLabel = "תקבולים החודש — כלל החברה";
    public const string CurrentBalanceForbiddenLabel = "יתרה נוכחית";

    public static string Money(decimal? value) =>
        value is decimal amount ? amount.ToString("N2") : EmDash;

    public static string Hours(decimal value) => value.ToString("0.##");

    public static string Date(DateTime? value) =>
        value is DateTime date ? date.ToString("dd/MM/yyyy") : EmDash;

    public static string SnapshotDateText(DateTime? snapshotDate) =>
        snapshotDate is DateTime date ? date.ToString("dd/MM/yyyy") : UnknownSnapshotDate;

    public static string SnapshotBalanceCaption(DateTime? snapshotDate) =>
        "יתרה לפי snapshot " + SnapshotDateText(snapshotDate);

    public static string FeeMix(BillingSnapshotFeeMix? mix) => mix switch
    {
        BillingSnapshotFeeMix.FixedPrice => "מחיר קבוע",
        BillingSnapshotFeeMix.Hourly => "שעות",
        BillingSnapshotFeeMix.Mixed => "מעורב",
        BillingSnapshotFeeMix.Other => "אחר",
        _ => EmDash
    };

    public static string FeeTooltip(BillingFeeTypeSummary? summary)
    {
        if (summary is null || summary.Counts.Count == 0)
            return EmDash;

        return string.Join(", ", summary.Counts.Select(c => $"{c.DisplayName} ×{c.Count}"));
    }

    public static string Freshness(BillingReplicaFreshnessStatus status) => status switch
    {
        BillingReplicaFreshnessStatus.Healthy => "תקין",
        BillingReplicaFreshnessStatus.Warning => "חריגה",
        BillingReplicaFreshnessStatus.Stale => "לא עדכני",
        _ => status.ToString()
    };

    public static string DateTimeStamp(DateTime? value) =>
        value is DateTime date ? date.ToString("dd/MM/yyyy HH:mm") : EmDash;
}
