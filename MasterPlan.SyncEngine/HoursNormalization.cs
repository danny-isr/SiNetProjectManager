namespace MasterPlan.SyncEngine;

/// <summary>
/// Shared normalization for hours data. Used by BOTH monthly ETL and daily API sync
/// to ensure Duration (decimal hours) and TotalHours (TimeSpan) are always consistent.
///
/// Source data formats:
///   DB ETL:  HoursReports.Hours (float) = raw MINUTES
///   Web API: Duration (decimal) = decimal HOURS (per API docs: "משך בשעות עשרוניות", 0.5 = 30 min)
///
/// Replica schema:
///   Duration   DECIMAL(10,4) — always in decimal hours
///   TotalHours TIME(0)       — always derived from Duration via DecimalHoursToTimeSpan
///
/// IMPORTANT: No duplication — both ETL and API paths converge through these functions.
/// </summary>
public static class HoursNormalization
{
    /// <summary>Maximum valid daily hours. Anything above is treated as invalid/corrupt data.</summary>
    private const decimal MaxDailyHours = 24m;

    /// <summary>
    /// Convert raw minutes (from DB source HoursReports.Hours float) to decimal hours.
    /// Used exclusively by monthly ETL pipeline.
    ///
    /// Example: Hours = 2.0 (minutes) → 0.0333 (decimal hours)
    /// </summary>
    /// <param name="rawMinutes">Value from dbo.HoursReports.Hours (float, stores minutes). May be null or DBNull.</param>
    /// <returns>Decimal hours rounded to 4 places, or null if invalid/out-of-range.</returns>
    public static decimal? MinutesToDecimalHours(object? rawMinutes)
    {
        if (rawMinutes is null or DBNull) return null;

        var minutes = Convert.ToDouble(rawMinutes);
        if (minutes < 0 || double.IsNaN(minutes) || double.IsInfinity(minutes)) return null;

        var hours = (decimal)(minutes / 60.0);
        if (hours > MaxDailyHours) return null;

        return Math.Round(hours, 4);
    }

    /// <summary>
    /// Validate decimal hours from Web API Duration field.
    /// Per API docs, Duration is "משך בשעות עשרוניות" (decimal hours), e.g. 0.5 = 30 min.
    /// However, some records return clearly invalid values (e.g. 2000 for a 2-minute entry).
    /// Values outside [0, 24] are rejected as invalid.
    /// Used exclusively by daily API sync.
    ///
    /// Example: Duration = 0.5 → 0.5 (valid)
    ///          Duration = 2000 → null (rejected, exceeds 24h)
    /// </summary>
    /// <param name="rawDecimalHours">API Duration field value.</param>
    /// <returns>Validated decimal hours, or null if out-of-range.</returns>
    public static decimal? ValidateDecimalHours(decimal? rawDecimalHours)
    {
        if (!rawDecimalHours.HasValue) return null;
        if (rawDecimalHours.Value < 0 || rawDecimalHours.Value > MaxDailyHours) return null;

        return Math.Round(rawDecimalHours.Value, 4);
    }

    /// <summary>
    /// Convert validated decimal hours to TimeSpan for TotalHours TIME(0) column.
    /// This is the SINGLE SOURCE OF TRUTH for TotalHours — used by both ETL and API sync.
    /// TotalHours must ALWAYS be derived from Duration, never stored independently.
    ///
    /// Example: 0.0333 hours → 00:02:00 (2 minutes)
    ///          0.5 hours    → 00:30:00 (30 minutes)
    ///          null         → null
    /// </summary>
    /// <param name="decimalHours">Validated decimal hours (output of MinutesToDecimalHours or ValidateDecimalHours).</param>
    /// <returns>TimeSpan representing hours:minutes:00, or null if input is null or exceeds 23:59.</returns>
    public static TimeSpan? DecimalHoursToTimeSpan(decimal? decimalHours)
    {
        if (!decimalHours.HasValue) return null;

        var totalMinutes = (int)Math.Round(decimalHours.Value * 60);
        if (totalMinutes < 0 || totalMinutes >= 1440) return null; // 1440 = 24 * 60

        return new TimeSpan(totalMinutes / 60, totalMinutes % 60, 0);
    }

    /// <summary>
    /// FALLBACK: Derive decimal hours from StartTime/EndTime when Duration is unavailable.
    /// Used when:
    ///   - API returns Duration=null or out-of-range (e.g., 2000)
    ///   - DB source hr.Hours is NULL
    ///   - But StartTime and EndTime ARE available
    ///
    /// Example: StartTime=09:00, EndTime=13:35 → 4.5833 hours
    ///          StartTime=17:00, EndTime=18:00 → 1.0 hours
    /// </summary>
    /// <param name="startTime">Start time of work entry.</param>
    /// <param name="endTime">End time of work entry.</param>
    /// <returns>Decimal hours rounded to 4 places, or null if inputs are null or result is invalid.</returns>
    public static decimal? DeriveDecimalHoursFromTimeRange(TimeSpan? startTime, TimeSpan? endTime)
    {
        if (!startTime.HasValue || !endTime.HasValue) return null;

        var duration = endTime.Value - startTime.Value;
        if (duration <= TimeSpan.Zero) return null; // EndTime before or equal to StartTime

        var hours = (decimal)duration.TotalHours;
        if (hours > MaxDailyHours) return null;

        return Math.Round(hours, 4);
    }
}
