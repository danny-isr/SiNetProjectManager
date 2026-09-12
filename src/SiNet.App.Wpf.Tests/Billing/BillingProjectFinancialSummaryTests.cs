using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingProjectFinancialSummaryTests
{
    [Fact]
    public void Request_2_saved_bill_reconciles_to_replica_fee_and_monthly_balance()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: 4_142_969.59m,
            balanceBefore: 1_189_568.9787m,
            snapshotDate: new DateTime(2026, 9, 10),
            feeMix: BillingSnapshotFeeMix.Mixed,
            billsInCreation: 0,
            currentBill: 110_280m,
            currentBillIsComplete: true);

        Assert.Equal("₪ 4,142,969.59", summary.TotalFeeText);
        Assert.Equal("₪ 1,189,568.98", summary.BalanceBeforeText);
        Assert.Equal("₪ 2,953,400.61", summary.AlreadyBilledText);
        Assert.Equal("₪ 110,280", summary.CurrentBillText);
        Assert.Equal("₪ 1,079,288.98", summary.BalanceAfterText);
        Assert.True(summary.ShowBar);
        Assert.Equal(2_953_400.61m, summary.ObservedBarShare);
        Assert.Equal(110_280m, summary.AdditionBarShare);
        Assert.Equal(1_079_288.98m, summary.RemainingBarShare);
        Assert.Equal(
            summary.ObservedBarShare + summary.AdditionBarShare + summary.RemainingBarShare,
            summary.TotalFee);
        Assert.Equal("יתרה לפי snapshot MasterPlan מ־10/09/2026", summary.SourceLine);
        Assert.False(summary.ShowExceedsWarning);
        Assert.True(summary.ShowHourlyNote);
        Assert.Contains("FeeType=4", summary.HourlyNote, StringComparison.Ordinal);
        Assert.False(summary.ShowInCreationNote);
    }

    [Fact]
    public void Stage_percent_change_updates_current_and_after_without_clamping()
    {
        var before = BillingProjectFinancialSummaryCalculator.Build(
            4_142_969.59m, 1_189_568.98m, new DateTime(2026, 9, 10),
            BillingSnapshotFeeMix.Mixed, 0, 110_280m, true);
        var after = BillingProjectFinancialSummaryCalculator.Build(
            4_142_969.59m, 1_189_568.98m, new DateTime(2026, 9, 10),
            BillingSnapshotFeeMix.Mixed, 0, 115_780m, true);

        Assert.Equal("₪ 115,780", after.CurrentBillText);
        Assert.Equal("₪ 1,073,788.98", after.BalanceAfterText);
        Assert.Equal(110_280m, before.AdditionBarShare);
        Assert.Equal(115_780m, after.AdditionBarShare);
        Assert.True(after.RemainingBarShare < before.RemainingBarShare);
        Assert.Equal(before.BalanceBefore, after.BalanceBefore);
        Assert.Equal(before.TotalFee, after.TotalFee);
    }

    [Fact]
    public void Unknown_fee_and_balance_are_not_converted_to_zero()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: null,
            balanceBefore: null,
            snapshotDate: null,
            feeMix: null,
            billsInCreation: 0,
            currentBill: 110_280m,
            currentBillIsComplete: true);

        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, summary.TotalFeeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, summary.BalanceBeforeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, summary.AlreadyBilledText);
        Assert.Equal("₪ 110,280", summary.CurrentBillText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, summary.BalanceAfterText);
        Assert.False(summary.ShowBar);
        Assert.DoesNotContain("₪ 0", summary.TotalFeeText, StringComparison.Ordinal);
        Assert.Null(summary.TotalFee);
        Assert.Null(summary.BalanceBefore);
        Assert.Equal(
            "יתרה לפי snapshot MasterPlan מ־" + BillingProjectFinancialSummaryCalculator.UnknownSnapshotDate,
            summary.SourceLine);
    }

    [Fact]
    public void Null_total_fee_alone_does_not_invent_already_billed_or_bar()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: null,
            balanceBefore: 1_189_568.98m,
            snapshotDate: new DateTime(2026, 9, 10),
            feeMix: BillingSnapshotFeeMix.FixedPrice,
            billsInCreation: 0,
            currentBill: 110_280m,
            currentBillIsComplete: true);

        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, summary.TotalFeeText);
        Assert.Equal("₪ 1,189,568.98", summary.BalanceBeforeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, summary.AlreadyBilledText);
        Assert.Equal("₪ 1,079,288.98", summary.BalanceAfterText);
        Assert.False(summary.ShowBar);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.MissingTotalBlocksBar, summary.BarUnavailableReason);
        Assert.Null(summary.AlreadyBilled);
    }

    [Fact]
    public void Null_balance_alone_does_not_invent_after_or_bar()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: 4_142_969.59m,
            balanceBefore: null,
            snapshotDate: new DateTime(2026, 9, 10),
            feeMix: BillingSnapshotFeeMix.FixedPrice,
            billsInCreation: 0,
            currentBill: 110_280m,
            currentBillIsComplete: true);

        Assert.Equal("₪ 4,142,969.59", summary.TotalFeeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, summary.BalanceBeforeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, summary.AlreadyBilledText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, summary.BalanceAfterText);
        Assert.False(summary.ShowBar);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.MissingBalanceBlocksBar, summary.BarUnavailableReason);
        Assert.Null(summary.BalanceAfter);
        Assert.DoesNotContain("₪ 0", summary.BalanceBeforeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_bill_exceeding_balance_shows_negative_after_and_no_fake_bar()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: 500_000m,
            balanceBefore: 100_000m,
            snapshotDate: new DateTime(2026, 9, 2),
            feeMix: BillingSnapshotFeeMix.FixedPrice,
            billsInCreation: 0,
            currentBill: 110_280m,
            currentBillIsComplete: true);

        Assert.Equal("₪ -10,280", summary.BalanceAfterText);
        Assert.Equal(-10_280m, summary.BalanceAfter);
        Assert.False(summary.ShowBar);
        Assert.True(summary.ShowExceedsWarning);
        Assert.Contains("גדול מהיתרה", summary.ExceedsBalanceWarning, StringComparison.Ordinal);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.ExceedsBalanceBlocksBar, summary.BarUnavailableReason);
        Assert.False(summary.ShowHourlyNote);
    }

    [Fact]
    public void Partial_current_bill_does_not_treat_missing_as_zero()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: 500_000m,
            balanceBefore: 250_000m,
            snapshotDate: new DateTime(2026, 9, 2),
            feeMix: BillingSnapshotFeeMix.FixedPrice,
            billsInCreation: 0,
            currentBill: 110_000m,
            currentBillIsComplete: false);

        Assert.Equal("₪ 110,000", summary.CurrentBillText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, summary.BalanceAfterText);
        Assert.Null(summary.BalanceAfter);
        Assert.False(summary.ShowBar);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.PartialAmountBlocksAfter, summary.BarUnavailableReason);
    }

    [Fact]
    public void Hourly_without_fee_is_reported_not_invented()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: null,
            balanceBefore: 12_000m,
            snapshotDate: new DateTime(2026, 9, 2),
            feeMix: BillingSnapshotFeeMix.Hourly,
            billsInCreation: 1,
            currentBill: 280m,
            currentBillIsComplete: true);

        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, summary.TotalFeeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.HourlyNoFeeNote, summary.HourlyNote);
        Assert.True(summary.ShowInCreationNote);
        Assert.Contains("ביצירה", summary.InCreationNote, StringComparison.Ordinal);
        Assert.False(summary.ShowBar);
    }

    [Fact]
    public void Already_billed_is_total_minus_balance_including_open_not_approved_only()
    {
        var summary = BillingProjectFinancialSummaryCalculator.Build(
            totalFee: 500_000m,
            balanceBefore: 250_000m,
            snapshotDate: new DateTime(2026, 8, 2),
            feeMix: BillingSnapshotFeeMix.FixedPrice,
            billsInCreation: 0,
            currentBill: 110_280m,
            currentBillIsComplete: true);

        Assert.Equal(250_000m, summary.AlreadyBilled);
        Assert.Equal("₪ 250,000", summary.AlreadyBilledText);
        Assert.Equal("₪ 139,720", summary.BalanceAfterText);
        Assert.True(summary.ShowBar);
        Assert.Equal(500_000m, summary.ObservedBarShare + summary.AdditionBarShare + summary.RemainingBarShare);
    }
}
