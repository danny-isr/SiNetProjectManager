using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingPreparationStageHierarchyTests
{
    [Fact]
    public void Stages_are_grouped_by_subcontract_and_never_duplicated()
    {
        var catalog = new[]
        {
            Draft(1, 10, "תכנון מוקדם", "כבישים", 0.25m, 0.10m, order: 1),
            Draft(2, 10, "תכנון מפורט", "כבישים", 0.25m, 0.20m, order: 2),
            Draft(3, 20, "תכנון מוקדם", "ניקוז", 0.40m, 0.10m, order: 1)
        };

        var groups = BillingSubContractStageGroupBuilder.Build(catalog);
        Assert.Equal(2, groups.Count);
        Assert.Equal([10, 20], groups.Select(g => g.SubContractId).ToArray());
        Assert.Equal(["תכנון מוקדם", "תכנון מפורט"], groups[0].EditableStages.Select(s => s.StageName).ToArray());
        var ids = groups.SelectMany(g => g.EditableStages.Select(s => s.MasterPlanStageId)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.False(BillingSubContractStageGroupBuilder.ShouldShowContractLevel(groups));
    }

    [Fact]
    public void OrderNum_order_is_preserved_inside_a_subcontract()
    {
        var catalog = new[]
        {
            Draft(2, 10, "מסירה", "כבישים", 0.25m, 0.10m, order: 30),
            Draft(1, 10, "תכנון מוקדם", "כבישים", 0.25m, 0.10m, order: 10),
            Draft(3, 10, "פיקוח", "כבישים", 0.25m, 0.10m, order: 20)
        };

        var group = Assert.Single(BillingSubContractStageGroupBuilder.Build(catalog));
        Assert.Equal([1, 3, 2], group.EditableStages.Select(s => s.MasterPlanStageId).ToArray());
    }

    [Fact]
    public void Contract_grouping_is_used_only_when_more_than_one_contract()
    {
        var one = BillingSubContractStageGroupBuilder.Build(
        [
            Draft(1, 10, "א", "כבישים", 0.5m, 0.10m, contractId: 7940),
            Draft(2, 20, "ב", "ניקוז", 0.5m, 0.10m, contractId: 7940)
        ]);
        Assert.False(BillingSubContractStageGroupBuilder.ShouldShowContractLevel(one));

        var two = BillingSubContractStageGroupBuilder.Build(
        [
            Draft(1, 10, "א", "כבישים", 0.5m, 0.10m, contractId: 1),
            Draft(2, 20, "ב", "ניקוז", 0.5m, 0.10m, contractId: 2)
        ]);
        Assert.True(BillingSubContractStageGroupBuilder.ShouldShowContractLevel(two));
    }

    [Fact]
    public void Stage_weight_display_scale_is_distinct_from_stage_progress()
    {
        Assert.Equal(25m, BillingStageContributionCalculator.ToDisplayPercent(0.25m));
        Assert.Equal(10m, BillingStageContributionCalculator.ToDisplayPercent(0.10m));
        Assert.NotEqual(
            BillingStageContributionCalculator.ToDisplayPercent(0.25m),
            BillingStageContributionCalculator.ToDisplayPercent(0.10m));
    }

    [Fact]
    public void Weighted_subcontract_contributions_match_the_accepted_example()
    {
        Assert.Equal(2.5m, BillingStageContributionCalculator.SubContractContributionPercent(0.25m, 0.10m));
        Assert.Equal(5m, BillingStageContributionCalculator.SubContractContributionPercent(0.25m, 0.20m));
        Assert.Equal(7.5m, BillingStageContributionCalculator.SubContractContributionPercent(0.25m, 0.30m));
        Assert.Equal(17.5m, BillingStageContributionCalculator.SubContractContributionPercent(0.25m, 0.70m));
    }

    [Fact]
    public void Subcontract_summary_includes_completed_hidden_stages_and_excludes_data_quality()
    {
        var catalog = new[]
        {
            Draft(1, 10, "הושלם", "כבישים", 0.50m, 1.00m),
            Draft(2, 10, "ניתן", "כבישים", 0.50m, 0.10m),
            Draft(3, 10, "חריג", "כבישים", 0.50m, 1.06m, observeRaw: true)
        };
        var group = Assert.Single(BillingSubContractStageGroupBuilder.Build(catalog));
        Assert.Single(group.EditableStages);
        Assert.Equal("ניתן", group.EditableStages[0].StageName);
        Assert.Equal(2, group.SummaryStages.Count);
        Assert.True(group.IsPartialBecauseOfDataQuality);
        var summary = BillingSubContractStageGroupBuilder.Summarize(
            group,
            new Dictionary<int, decimal> { [2] = 0.20m });
        Assert.Equal(55m, summary.ObservedPercent);
        Assert.Equal(10m, summary.AdditionPercent);
        Assert.Equal(65m, summary.AfterPercent);
        Assert.Equal(35m, summary.RemainingPercent);
        Assert.Contains("חלקי", BillingSubContractStageGroupBuilder.PartialSummaryWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_100_percent_weight_sum_triggers_warning_without_normalizing()
    {
        var group = Assert.Single(BillingSubContractStageGroupBuilder.Build(
        [
            Draft(1, 10, "א", "כבישים", 0.40m, 0.10m),
            Draft(2, 10, "ב", "כבישים", 0.30m, 0.10m)
        ]));
        Assert.False(group.WeightsSumApproximatelyToOne);
        Assert.Equal(0.70m, group.ValidWeightSum);
        var warning = BillingSubContractStageGroupBuilder.FormatWeightSumWarning(70m);
        Assert.Contains("70%", warning, StringComparison.Ordinal);
        Assert.Contains("ולא ב-100%", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Observed_90_plus_addition_10_is_valid_and_20_is_invalid_in_percent_wording()
    {
        var observed = BillingStageProgressCalculator.Observe([0.90m]);
        var ok = BillingStageProgressCalculator.ValidateAddition(observed, 0.10m);
        Assert.True(ok.IsValid);
        Assert.Equal(1.00m, ok.Target);

        var bad = BillingStageProgressCalculator.ValidateAddition(observed, 0.20m);
        Assert.False(bad.IsValid);
        var message = BillingStageProgressMessages.FormatAdditionError(observed, 0.20m);
        Assert.Contains("לא ניתן להוסיף 20%", message, StringComparison.Ordinal);
        Assert.Contains("נותרו רק 10%", message, StringComparison.Ordinal);
        Assert.False(BillingStageProgressMessages.IsUserFacingScaleLeak(message));
        Assert.Equal("ניתן להוסיף עד 10%", BillingStageProgressMessages.FormatMaxAdditionHint(90m));
    }

    [Fact]
    public void Hourly_subcontracts_are_not_stage_groups()
    {
        var groups = BillingSubContractStageGroupBuilder.Build(
        [
            Draft(1, 10, "שעתי", "שעות", 1m, 0.10m, feeTypeId: MasterPlanSnapshotFeeTypeIds.WorkingHours)
        ]);
        Assert.Empty(groups);
    }

    [Fact]
    public void Task_instructions_group_by_subcontract_and_skip_zero_addition()
    {
        var body = BillingPreparationTaskInstructions.Build(
            new BillingPreparationRequestRecord(
                1, 5905, 12, "2608", "פרויקט", "לקוח",
                BillingPreparationStatus.ReadyForApproval,
                new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
                7, "manager",
                null, null, null, null, null, null,
                false, null, null, null,
                [
                    new BillingPreparationStageLineSnapshot(
                        1, 10, "פיקוח", "כבישים", 0.25m,
                        0.10m, 0.30m, 0.20m, false,
                        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                        BillingConfirmationMode.None, null, null, null),
                    new BillingPreparationStageLineSnapshot(
                        2, 10, "מסירה", "כבישים", 0.25m,
                        0.10m, 0.10m, 0m, false,
                        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                        BillingConfirmationMode.None, null, null, null),
                    new BillingPreparationStageLineSnapshot(
                        3, 20, "פיקוח", "ניקוז", 0.40m,
                        0.10m, 0.30m, 0.20m, false,
                        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                        BillingConfirmationMode.None, null, null, null)
                ],
                []));
        Assert.Contains("תת חוזה: כבישים", body, StringComparison.Ordinal);
        Assert.Contains("תת חוזה: ניקוז", body, StringComparison.Ordinal);
        Assert.Contains("• פיקוח", body, StringComparison.Ordinal);
        Assert.DoesNotContain("מסירה", body, StringComparison.Ordinal);
        Assert.Contains("משקל השלב בתת החוזה: 25%", body, StringComparison.Ordinal);
        Assert.Contains("תרומת התוספת לתת החוזה: 5%", body, StringComparison.Ordinal);
        Assert.DoesNotContain("יעד מצטבר", body, StringComparison.Ordinal);
    }

    private static BillingPreparationStageDraft Draft(
        int stageId,
        int subId,
        string name,
        string subName,
        decimal weight,
        decimal observedOrOutlier,
        int feeTypeId = MasterPlanSnapshotFeeTypeIds.FixedPrice,
        int contractId = 7940,
        int order = 0,
        bool observeRaw = false)
    {
        var observed = BillingStageProgressCalculator.Observe([observedOrOutlier]);
        return new BillingPreparationStageDraft(
            stageId, subId, name, subName, weight, observed, observed.Value, false, feeTypeId,
            contractId, "חוזה ראשי", "1", "1234", order);
    }
}
