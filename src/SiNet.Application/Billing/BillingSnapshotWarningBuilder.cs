using SiNet.Application.Abstractions.Logging;

namespace SiNet.Application.Billing;

/// <summary>Warnings for missing/undated monthly snapshot enrichment. Never blocks Replica candidates.</summary>
public static class BillingSnapshotWarningBuilder
{
    public static IReadOnlyList<BillingDataQualityWarning> Build(
        bool snapshotDatabaseConfigured,
        IReadOnlyList<string> missingSnapshotTables,
        DateTime? snapshotDate,
        IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(missingSnapshotTables);

        var warnings = new List<BillingDataQualityWarning>();

        if (!snapshotDatabaseConfigured)
        {
            warnings.Add(new BillingDataQualityWarning(
                BillingDataQualityWarningCodes.SnapshotEnrichmentUnavailable,
                "חיבור ה-snapshot החודשי (MasterPlanDatabase / Db_Mp_SiEng) אינו מוגדר. מועמדי Replica נשארים ללא העשרה."));
            return warnings;
        }

        var missing = missingSnapshotTables
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count > 0)
        {
            var message = "חסרות טבלאות snapshot חודשי: " + string.Join(", ", missing) +
                          ". מועמדי Replica לא הוסרו.";
            warnings.Add(new BillingDataQualityWarning(
                BillingDataQualityWarningCodes.SnapshotRequiredTableMissing,
                message,
                missing.Count));
            logger?.Warn($"[Billing] snapshot-tables {message}");
        }

        if (snapshotDate is null)
        {
            warnings.Add(new BillingDataQualityWarning(
                BillingDataQualityWarningCodes.SnapshotDateUnknown,
                "אין חותמת Sync_State.MonthlyRestore (BackupFinishDate). תאריך ה-snapshot הוא null — לא הומצא מתאריכי טבלאות."));
            logger?.Warn("[Billing] snapshot-date unknown: Sync_State.MonthlyRestore is missing.");
        }

        return warnings;
    }
}
