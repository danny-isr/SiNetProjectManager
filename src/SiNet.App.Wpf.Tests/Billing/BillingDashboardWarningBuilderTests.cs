using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingDashboardWarningBuilderTests
{
    [Fact]
    public void When_OnlyInBasic_is_positive_then_hours_parity_warning_is_raised()
    {
        var warnings = BillingDashboardWarningBuilder.Build(
            syncStatePresent: true,
            hoursOnlyInBasicCount: 3,
            realBillsUsingLastUpdatedFallback: 0);

        var warning = Assert.Single(warnings);
        Assert.Equal(BillingDataQualityWarningCodes.HoursParityOnlyInBasic, warning.Code);
        Assert.Equal(3, warning.Count);
        Assert.Contains("OnlyInBasic=3", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void When_OnlyInBasic_is_zero_then_no_parity_warning()
    {
        var warnings = BillingDashboardWarningBuilder.Build(
            syncStatePresent: true,
            hoursOnlyInBasicCount: 0,
            realBillsUsingLastUpdatedFallback: 0);

        Assert.Empty(warnings);
    }

    [Fact]
    public void When_real_bill_uses_LastUpdated_fallback_then_warning_includes_count()
    {
        var warnings = BillingDashboardWarningBuilder.Build(
            syncStatePresent: true,
            hoursOnlyInBasicCount: 0,
            realBillsUsingLastUpdatedFallback: 4);

        var warning = Assert.Single(warnings);
        Assert.Equal(BillingDataQualityWarningCodes.RealBillSubmitDateFallback, warning.Code);
        Assert.Equal(4, warning.Count);
        Assert.Contains("LastUpdated", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void When_in_creation_bill_has_no_SubmitDate_then_it_is_not_a_real_bill_fallback()
    {
        var bills = new[]
        {
            new BillingBillFact(1, 1, "B1", 1m, MasterPlanBillStatusIds.InCreation, "ביצירה", null, DateTime.UtcNow),
            new BillingBillFact(2, 1, "B2", 1m, MasterPlanBillStatusIds.Submitted, "הוגש", DateTime.UtcNow, null),
        };

        Assert.Equal(0, BillingBillTimeline.CountRealBillsUsingLastUpdatedFallback(bills));
    }
}
