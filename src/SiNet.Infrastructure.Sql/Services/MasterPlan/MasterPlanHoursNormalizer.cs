namespace SiNet.Infrastructure.Sql.Services.MasterPlan;

/// <summary>
/// Shared hours conversion for Replica Extended / MasterPlan hour reports.
/// Billing and R02 must use this helper so they cannot disagree about hours.
/// </summary>
public static class MasterPlanHoursNormalizer
{
    /// <summary>
    /// Converts DB hours payloads to decimal hours (parity with GoogleConnector R02ReportService).
    /// Handles TimeSpan, decimal hours, minutes, milliseconds, and .NET ticks; falls back to start/end.
    /// </summary>
    public static decimal ConvertHoursRaw(
        object? hoursRaw,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        if (hoursRaw is null or DBNull)
            return CalculateFromStartEnd(startTime, endTime);

        if (hoursRaw is TimeSpan ts)
            return Math.Round((decimal)ts.TotalHours, 2);

        var numericValue = hoursRaw switch
        {
            long l => l,
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            short s => s,
            byte b => b,
            _ => TryParseToDecimal(hoursRaw),
        };

        return ConvertNumericToHours(numericValue, startTime, endTime);
    }

    /// <summary>
    /// Replica Extended: valid decimal Duration (0–24) first, otherwise TotalHours, otherwise start/end.
    /// </summary>
    public static decimal ConvertExtendedHours(
        object? duration,
        object? totalHours,
        TimeSpan? startTime,
        TimeSpan? endTime)
    {
        object? hoursRaw = null;
        if (duration is not null and not DBNull)
        {
            var numeric = duration switch
            {
                decimal d => d,
                double dbl => (decimal)dbl,
                float f => (decimal)f,
                int i => i,
                long l => l,
                short s => s,
                byte b => b,
                _ => TryParseToDecimal(duration),
            };
            if (numeric >= 0m && numeric <= 24m)
                hoursRaw = duration;
        }

        hoursRaw ??= totalHours is null or DBNull ? null : totalHours;
        return ConvertHoursRaw(hoursRaw, startTime, endTime);
    }

    public static TimeSpan? ReadTimeValue(object? value)
    {
        if (value is null or DBNull)
            return null;

        return value switch
        {
            TimeSpan ts => ts,
            DateTime dt => dt.TimeOfDay,
            _ => TimeSpan.TryParse(Convert.ToString(value), out var parsed) ? parsed : null,
        };
    }

    /// <summary>Heuristic conversion matching legacy GoogleConnector (plus start/end before ms).</summary>
    private static decimal ConvertNumericToHours(decimal value, TimeSpan? startTime, TimeSpan? endTime)
    {
        const decimal MillisecondsPerHour = 3_600_000m;
        const decimal TicksPerHour = 36_000_000_000m;
        // TIME/ticks sometimes leak as Ticks/1e6 (2h → 72_000).
        const decimal ScaledTicksPerHour = 36_000m;
        const decimal MaxReasonableHours = 24m;
        const decimal MinMilliseconds = 60_000m;
        const decimal MaxMilliseconds = 86_400_000m;
        const decimal MinTicks = 36_000_000_000m;

        var absValue = Math.Abs(value);

        if (absValue <= MaxReasonableHours)
            return Math.Round(value, 2);

        if (absValue >= MinTicks)
            return Math.Round(value / TicksPerHour, 2);

        // Prefer wall-clock duration when the raw number is clearly not decimal-hours.
        var fromRange = CalculateFromStartEnd(startTime, endTime);
        if (fromRange > 0)
            return fromRange;

        // Scaled ticks (Ticks / 1_000_000): 2h → 72_000. Check before ms (72_000 is also in ms range).
        if (absValue >= ScaledTicksPerHour)
        {
            var scaledHours = absValue / ScaledTicksPerHour;
            if (scaledHours <= MaxReasonableHours)
                return Math.Round(value / ScaledTicksPerHour, 2);
        }

        if (absValue >= MinMilliseconds && absValue <= MaxMilliseconds)
            return Math.Round(value / MillisecondsPerHour, 2);

        // MasterPlan HoursReports.Hours is raw minutes.
        if (absValue > MaxReasonableHours && absValue < MinMilliseconds)
            return Math.Round(value / 60m, 2);

        return Math.Round(value, 2);
    }

    private static decimal CalculateFromStartEnd(TimeSpan? startTime, TimeSpan? endTime)
    {
        if (!startTime.HasValue || !endTime.HasValue)
            return 0m;

        var duration = endTime.Value - startTime.Value;
        if (duration < TimeSpan.Zero)
            duration = duration.Add(TimeSpan.FromHours(24));

        if (duration <= TimeSpan.Zero)
            return 0m;

        var hours = (decimal)duration.TotalHours;
        return hours > 24m ? 0m : Math.Round(hours, 2);
    }

    private static decimal TryParseToDecimal(object? value)
    {
        if (value is null)
            return 0m;
        return decimal.TryParse(Convert.ToString(value), out var parsed) ? parsed : 0m;
    }
}
