using SiNet.Application.Abstractions.Logging;

namespace SiNet.Application.Billing;

/// <summary>Builds dashboard data-quality warnings from Replica snapshot flags (no SQL).</summary>
public static class BillingDashboardWarningBuilder
{
    public static IReadOnlyList<BillingDataQualityWarning> Build(
        bool syncStatePresent,
        int hoursOnlyInBasicCount,
        int realBillsUsingLastUpdatedFallback,
        IAppLogger? logger = null)
    {
        var warnings = new List<BillingDataQualityWarning>();

        if (!syncStatePresent)
        {
            warnings.Add(new BillingDataQualityWarning(
                BillingDataQualityWarningCodes.SyncStateMissing,
                "טבלת Sync_State חסרה ב-Replica. לא ניתן להציג חותמת סנכרון."));
        }

        if (hoursOnlyInBasicCount > 0)
        {
            var message =
                $"OnlyInBasic={hoursOnlyInBasicCount}: שורות ב-MP_ProjectHours שלא קיימות ב-MP_ProjectHoursExtended.";
            warnings.Add(new BillingDataQualityWarning(
                BillingDataQualityWarningCodes.HoursParityOnlyInBasic,
                message,
                hoursOnlyInBasicCount));
            logger?.Warn($"[Billing] hours-parity {message}");
        }

        if (realBillsUsingLastUpdatedFallback > 0)
        {
            var message =
                $"RealBillSubmitDateFallback={realBillsUsingLastUpdatedFallback}: חשבונות בסטטוס הוגש/אושר/סגור בלי SubmitDate — LastUpdated משמש כ-EffectiveBillDate.";
            warnings.Add(new BillingDataQualityWarning(
                BillingDataQualityWarningCodes.RealBillSubmitDateFallback,
                message,
                realBillsUsingLastUpdatedFallback));
            logger?.Warn($"[Billing] submit-date-fallback {message}");
        }

        return warnings;
    }
}
