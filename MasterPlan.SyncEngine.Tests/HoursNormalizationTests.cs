using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class HoursNormalizationTests
{
    [Theory]
    [InlineData(null)]
    public void MillisecondsToDecimalHours_null_returns_null(object? raw)
    {
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(raw));
    }

    [Fact]
    public void MillisecondsToDecimalHours_DBNull_returns_null()
    {
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(DBNull.Value));
    }

    [Fact]
    public void MillisecondsToDecimalHours_negative_returns_null()
    {
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(-1));
    }

    [Fact]
    public void MillisecondsToDecimalHours_zero_returns_zero()
    {
        Assert.Equal(0m, HoursNormalization.MillisecondsToDecimalHours(0));
    }

    [Theory]
    [InlineData(1_800_000d, 0.5000)]
    [InlineData(3_600_000d, 1.0000)]
    [InlineData(5_400_000d, 1.5000)]
    [InlineData(7_200_000d, 2.0000)]
    [InlineData(28_800_000d, 8.0000)]
    [InlineData(86_400_000d, 24.0000)]
    public void MillisecondsToDecimalHours_valid_ms(double raw, double expected)
    {
        Assert.Equal((decimal)expected, HoursNormalization.MillisecondsToDecimalHours(raw));
    }

    [Fact]
    public void MillisecondsToDecimalHours_above_24h_returns_null()
    {
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(86_400_001d));
    }

    [Fact]
    public void MillisecondsToDecimalHours_NaN_returns_null()
    {
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(double.NaN));
    }

    [Fact]
    public void MillisecondsToDecimalHours_Infinity_returns_null()
    {
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(double.PositiveInfinity));
        Assert.Null(HoursNormalization.MillisecondsToDecimalHours(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(0.5, 0, 30, 0)]
    [InlineData(1.0, 1, 0, 0)]
    [InlineData(8.0, 8, 0, 0)]
    public void DecimalHoursToTimeSpan_common_durations(double hours, int h, int m, int s)
    {
        var ts = HoursNormalization.DecimalHoursToTimeSpan((decimal)hours);
        Assert.NotNull(ts);
        Assert.Equal(new TimeSpan(h, m, s), ts);
    }

    [Fact]
    public void DecimalHoursToTimeSpan_exactly_24_returns_null()
    {
        Assert.Null(HoursNormalization.DecimalHoursToTimeSpan(24.0000m));
    }

    [Fact]
    public void Milliseconds_full_day_duration_24_totalhours_null()
    {
        var duration = HoursNormalization.MillisecondsToDecimalHours(86_400_000d);
        Assert.Equal(24.0000m, duration);
        Assert.Null(HoursNormalization.DecimalHoursToTimeSpan(duration));
    }
}
