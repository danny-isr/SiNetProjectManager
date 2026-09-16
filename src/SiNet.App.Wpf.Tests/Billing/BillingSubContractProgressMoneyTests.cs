using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingSubContractProgressMoneyTests
{
    [Fact]
    public void Full_progress_this_bill_reconciles_to_zero_remaining()
    {
        var money = BillingPreparationAmountCalculator.SubContractProgressAmounts(
            [Stage(220_000m, observed: 0.80m)],
            new Dictionary<int, decimal> { [1] = 0.20m });

        Assert.True(money.AlreadyBilled.IsPriced);
        Assert.Equal(176_000m, money.AlreadyBilled.Amount);
        Assert.Equal(44_000m, money.CurrentAddition.Amount);
        Assert.Equal(220_000m, money.AfterAccount.Amount);
        Assert.Equal(0m, money.Remaining.Amount);
        Assert.Equal(
            money.AlreadyBilled.Amount + money.CurrentAddition.Amount,
            money.AfterAccount.Amount);
        Assert.Equal(
            money.SubcontractTotal.Amount - money.AfterAccount.Amount,
            money.Remaining.Amount);
    }

    [Fact]
    public void Eighty_percent_already_billed_leaves_the_priced_remainder()
    {
        var money = BillingPreparationAmountCalculator.SubContractProgressAmounts(
            [Stage(91_460m, observed: 0.80m)],
            new Dictionary<int, decimal>());

        Assert.Equal(73_168m, money.AlreadyBilled.Amount);
        Assert.Equal(0m, money.CurrentAddition.Amount);
        Assert.Equal(73_168m, money.AfterAccount.Amount);
        Assert.Equal(18_292m, money.Remaining.Amount);
    }

    [Fact]
    public void Changing_addition_recalculates_group_money_without_save()
    {
        var (group, edit) = Group(220_000m, observed: 0.80m);
        group.RefreshSummary();
        Assert.Contains("₪ 176,000", group.ObservedSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 0", group.AdditionSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 176,000", group.AfterSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 44,000", group.RemainingSummaryText, StringComparison.Ordinal);

        edit.AdditionPercent = 20m;
        group.RefreshSummary();
        Assert.Contains("₪ 176,000", group.ObservedSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 44,000", group.AdditionSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 220,000", group.AfterSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 0", group.RemainingSummaryText, StringComparison.Ordinal);
        Assert.Equal("תוספת בחשבון הזה 20% · ₪ 44,000", group.AdditionSummaryText);
    }

    [Fact]
    public void Discarding_addition_restores_persisted_percent_and_money()
    {
        var (group, edit) = Group(91_460m, observed: 0.80m);
        group.RefreshSummary();
        var observed = group.ObservedSummaryText;
        var addition = group.AdditionSummaryText;
        var after = group.AfterSummaryText;
        var remaining = group.RemainingSummaryText;

        edit.AdditionPercent = 10m;
        group.RefreshSummary();
        Assert.NotEqual(addition, group.AdditionSummaryText);
        Assert.Contains("₪ 9,146", group.AdditionSummaryText, StringComparison.Ordinal);

        edit.AdditionPercent = 0m;
        group.RefreshSummary();
        Assert.Equal(observed, group.ObservedSummaryText);
        Assert.Equal(addition, group.AdditionSummaryText);
        Assert.Equal(after, group.AfterSummaryText);
        Assert.Equal(remaining, group.RemainingSummaryText);
        Assert.Contains("₪ 73,168", group.ObservedSummaryText, StringComparison.Ordinal);
        Assert.Contains("₪ 18,292", group.RemainingSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void FeeType_hourly_still_uses_hours_times_rate_not_percent_of_fee_sum()
    {
        var hourly = new BillingHourlySubContractDraft(
            99, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 280m);
        var priced = BillingPreparationAmountCalculator.Hourly(hourly, 10m);
        Assert.Equal(2_800m, priced.Amount);

        var stage = Stage(220_000m, observed: 0.80m) with { FeeTypeId = MasterPlanSnapshotFeeTypeIds.WorkingHours };
        var fromStageFormula = BillingPreparationAmountCalculator.StageAddition(stage, 0.20m);
        Assert.Equal(44_000m, fromStageFormula.Amount);
        Assert.NotEqual(priced.Amount, fromStageFormula.Amount);
    }

    [Fact]
    public void Missing_billable_basis_omits_money_and_keeps_the_percent()
    {
        var (group, _) = Group(null, observed: 0.80m);
        group.RefreshSummary();
        Assert.Equal("כבר חויב 80%", group.ObservedSummaryText);
        Assert.DoesNotContain("₪", group.ObservedSummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("₪", group.RemainingSummaryText, StringComparison.Ordinal);
    }

    private static (BillingPreparationSubContractGroupVm Group, BillingPreparationStageEditVm Edit) Group(
        decimal? billable,
        decimal observed)
    {
        var stage = Stage(billable, observed);
        var draft = new BillingSubContractStageGroupDraft(
            10,
            "תת חוזה",
            "1",
            1,
            "חוזה",
            "1",
            [stage],
            [stage],
            [],
            1m,
            true,
            false);
        var group = new BillingPreparationSubContractGroupVm(draft, showContract: false);
        var edit = BillingPreparationStageEditVm.FromCatalog(stage, new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
        group.AddEditable(edit);
        return (group, edit);
    }

    private static BillingPreparationStageDraft Stage(decimal? billable, decimal observed)
    {
        var observedProgress = BillingStageProgressCalculator.Observe([observed]);
        return new BillingPreparationStageDraft(
            1,
            10,
            "שלב",
            "תת חוזה",
            1m,
            observedProgress,
            observedProgress.Value,
            false,
            MasterPlanSnapshotFeeTypeIds.FixedPrice,
            1,
            "חוזה",
            SubContractBillableAmount: billable);
    }
}
