using System.Globalization;

namespace SiNet.Application.Billing;

/// <summary>Hebrew one-line explanation for a candidate row. Not a formula dump.</summary>
public static class BillingCandidateReasonFormatter
{
    public static string Format(BillingCandidateState state, BillingCandidateMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return state switch
        {
            BillingCandidateState.BillInPreparation =>
                "יש עבודה פעילה, אך כבר קיים חשבון ביצירה",
            BillingCandidateState.CoveredByLatestBill =>
                $"{Hours(metrics.Hours30)} שעות דווחו ב-30 הימים האחרונים, אך החשבון האחרון כבר מכסה את כל העבודה המתועדת עד לתאריכו",
            BillingCandidateState.ReviewNow or BillingCandidateState.AccumulatedWork =>
                FormatWorkSinceBill(metrics),
            BillingCandidateState.NotUrgent =>
                "אין עבודה שמצדיקה בדיקת חיוב כעת",
            _ => state.ToString()
        };
    }

    private static string FormatWorkSinceBill(BillingCandidateMetrics metrics)
    {
        var lastBill = metrics.DaysSinceLastBill is int days
            ? $"חשבון אחרון לפני {days} ימים"
            : "אין חשבון קודם (סטטוס הוגש/אושר/סגור)";

        return
            $"{Hours(metrics.HoursSinceLastBill)} שעות מאז החשבון האחרון · " +
            $"{Hours(metrics.Hours30)} שעות ב-30 הימים האחרונים · " +
            $"{metrics.WorkDays30} ימי עבודה · {lastBill} · אין חשבון ביצירה";
    }

    private static string Hours(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>Inputs for <see cref="BillingCandidateReasonFormatter"/>.</summary>
public sealed record BillingCandidateMetrics(
    decimal Hours30,
    decimal HoursSinceLastBill,
    int WorkDays30,
    int? DaysSinceLastBill);
