namespace SiNet.Application.Billing;

/// <summary>
/// Replica freshness vs <paramref name="utcNow"/>. <c>AsOfDate</c> never drives the clock.
/// </summary>
public static class BillingReplicaFreshnessEvaluator
{
    public static BillingReplicaFreshnessDecision Evaluate(
        IReadOnlyList<string> missingRequiredTables,
        bool syncStateTablePresent,
        IReadOnlyDictionary<string, DateTime?> syncTimesByEntity,
        DateTime utcNow,
        DateTime localToday,
        DateTime? asOfDate,
        BillingReplicaFreshnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(missingRequiredTables);
        ArgumentNullException.ThrowIfNull(syncTimesByEntity);

        options ??= BillingReplicaFreshnessOptions.Default;
        var isCurrent = asOfDate is null || asOfDate.Value.Date >= localToday.Date;
        var missingTables = missingRequiredTables
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingTables.Count > 0)
        {
            return StructuralFatal(
                isCurrent,
                BillingDataQualityWarningCodes.ReplicaRequiredTableMissing,
                "חסרה טבלת Replica נדרשת: " + string.Join(", ", missingTables) +
                ". אין נפילה ל-MasterPlan או ל-MP_ProjectHours.",
                missingTables,
                Array.Empty<string>());
        }

        if (!syncStateTablePresent)
        {
            return StructuralFatal(
                isCurrent,
                BillingDataQualityWarningCodes.ReplicaSyncStateMissing,
                "טבלת Sync_State חסרה ב-Replica. לא ניתן לאמת רעננות — המועמדים נחסמו.",
                Array.Empty<string>(),
                BillingReplicaRequirements.RequiredSyncStateEntities);
        }

        var missingEntities = BillingReplicaRequirements.RequiredSyncStateEntities
            .Where(entity =>
                !syncTimesByEntity.TryGetValue(entity, out var stamp) || stamp is null)
            .ToList();

        if (missingEntities.Count > 0)
        {
            return StructuralFatal(
                isCurrent,
                BillingDataQualityWarningCodes.ReplicaSyncStateMissing,
                "חסרה חותמת Sync_State ליישויות: " + string.Join(", ", missingEntities) +
                ". המועמדים נחסמו.",
                Array.Empty<string>(),
                missingEntities);
        }

        var oldest = BillingReplicaRequirements.RequiredSyncStateEntities
            .Select(entity => ToUtc(syncTimesByEntity[entity]!.Value))
            .Min();
        var age = utcNow.ToUniversalTime() - oldest;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age > options.FatalAfter)
        {
            var hours = FormatHours(age);
            return new BillingReplicaFreshnessDecision(
                BillingReplicaFreshnessStatus.Stale,
                CandidatesBlocked: isCurrent,
                IsCurrentDashboardRequest: isCurrent,
                OldestSyncAge: age,
                Code: isCurrent
                    ? BillingDataQualityWarningCodes.ReplicaFreshnessFatal
                    : BillingDataQualityWarningCodes.ReplicaFreshnessWarning,
                Message: isCurrent
                    ? $"Replica לא מעודכן ({hours} מאז הסנכרון הישן ביותר). מועמדים נוכחיים נחסמו."
                    : $"Replica לא מעודכן ({hours} מאז הסנכרון הישן ביותר). בקשה היסטורית — המועמדים לא נחסמו רק בגלל גיל AsOfDate.",
                MissingRequiredTables: Array.Empty<string>(),
                MissingSyncStateEntities: Array.Empty<string>());
        }

        if (age > options.WarningAfter)
        {
            return new BillingReplicaFreshnessDecision(
                BillingReplicaFreshnessStatus.Warning,
                CandidatesBlocked: false,
                IsCurrentDashboardRequest: isCurrent,
                OldestSyncAge: age,
                Code: BillingDataQualityWarningCodes.ReplicaFreshnessWarning,
                Message: $"Replica בסטייה ({FormatHours(age)} מאז הסנכרון הישן ביותר). המועמדים מוחזרים עם אזהרה.",
                MissingRequiredTables: Array.Empty<string>(),
                MissingSyncStateEntities: Array.Empty<string>());
        }

        return new BillingReplicaFreshnessDecision(
            BillingReplicaFreshnessStatus.Healthy,
            CandidatesBlocked: false,
            IsCurrentDashboardRequest: isCurrent,
            OldestSyncAge: age,
            Code: string.Empty,
            Message: string.Empty,
            MissingRequiredTables: Array.Empty<string>(),
            MissingSyncStateEntities: Array.Empty<string>());
    }

    private static BillingReplicaFreshnessDecision StructuralFatal(
        bool isCurrent,
        string code,
        string message,
        IReadOnlyList<string> missingTables,
        IReadOnlyList<string> missingEntities) =>
        new(
            BillingReplicaFreshnessStatus.Stale,
            CandidatesBlocked: true,
            IsCurrentDashboardRequest: isCurrent,
            OldestSyncAge: null,
            Code: code,
            Message: message,
            MissingRequiredTables: missingTables,
            MissingSyncStateEntities: missingEntities);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string FormatHours(TimeSpan age) =>
        age.TotalHours.ToString("0.#") + " שעות";
}
