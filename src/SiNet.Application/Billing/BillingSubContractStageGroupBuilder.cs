using System.Globalization;

namespace SiNet.Application.Billing;

public sealed record BillingSubContractStageGroupDraft(
    int SubContractId,
    string SubContractName,
    string? SubContractNumber,
    int ContractId,
    string? ContractName,
    string? ContractNumber,
    IReadOnlyList<BillingPreparationStageDraft> SummaryStages,
    IReadOnlyList<BillingPreparationStageDraft> EditableStages,
    IReadOnlyList<BillingPreparationStageDraft> DataQualityStages,
    decimal ValidWeightSum,
    bool WeightsSumApproximatelyToOne,
    bool IsPartialBecauseOfDataQuality);

public sealed record BillingSubContractWeightedSummary(
    decimal ObservedPercent,
    decimal AdditionPercent,
    decimal AfterPercent,
    decimal RemainingPercent,
    decimal ValidWeightSumPercent,
    bool WeightsSumApproximatelyToOne,
    bool IsPartialBecauseOfDataQuality);

/// <summary>
/// Groups payment stages by SubContract. Completed 100% stages stay in the summary
/// and out of the editable list. Data-quality rows never enter totals silently.
/// </summary>
public static class BillingSubContractStageGroupBuilder
{
    public const decimal WeightSumTolerance = 0.02m;

    public static IReadOnlyList<BillingSubContractStageGroupDraft> Build(
        IReadOnlyList<BillingPreparationStageDraft> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var groups = new List<BillingSubContractStageGroupDraft>();
        foreach (var bucket in catalog.GroupBy(s => s.MasterPlanSubContractId))
        {
            var ordered = bucket
                .OrderBy(s => s.OrderNum)
                .ThenBy(s => s.MasterPlanStageId)
                .ToList();
            if (ordered.Count == 0)
                continue;
            if (ordered.All(s => s.FeeTypeId == MasterPlanSnapshotFeeTypeIds.WorkingHours))
                continue;

            var classified = ordered
                .Select(s => (Stage: s, Decision: BillingPreparationStageEditorFilter.Classify(s)))
                .ToList();
            var summary = classified
                .Where(c => c.Decision.IsEditable
                            || c.Decision.Exclusion == BillingPreparationStageExclusionKind.Completed)
                .Select(c => c.Stage)
                .ToList();
            var editable = classified
                .Where(c => c.Decision.IsEditable)
                .Select(c => c.Stage)
                .ToList();
            var dataQuality = classified
                .Where(c => c.Decision.ShowInDataQualityWarning)
                .Select(c => c.Stage)
                .ToList();
            if (summary.Count == 0 && editable.Count == 0 && dataQuality.Count == 0)
                continue;

            var weightSum = summary.Sum(s => s.StageWeightWithinSubContract);
            var first = ordered[0];
            groups.Add(new BillingSubContractStageGroupDraft(
                first.MasterPlanSubContractId,
                first.SubContractName,
                first.SubContractNumber,
                first.MasterPlanContractId,
                first.ContractName,
                first.ContractNumber,
                summary,
                editable,
                dataQuality,
                weightSum,
                Math.Abs(weightSum - 1m) <= WeightSumTolerance,
                dataQuality.Count > 0));
        }

        return groups;
    }

    public static bool ShouldShowContractLevel(IReadOnlyList<BillingSubContractStageGroupDraft> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups
            .Select(g => g.ContractId)
            .Where(id => id > 0)
            .Distinct()
            .Count() > 1;
    }

    public static BillingSubContractWeightedSummary Summarize(
        BillingSubContractStageGroupDraft group,
        IReadOnlyDictionary<int, decimal> additionFractionsByStageId)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(additionFractionsByStageId);

        decimal observed = 0m;
        decimal addition = 0m;
        decimal after = 0m;
        decimal remaining = 0m;
        foreach (var stage in group.SummaryStages)
        {
            var observedFrac = stage.Observed.Value ?? 0m;
            additionFractionsByStageId.TryGetValue(stage.MasterPlanStageId, out var addFrac);
            if (addFrac < 0m)
                addFrac = 0m;
            var afterFrac = observedFrac + addFrac;
            var remainingFrac = 1m - afterFrac;
            if (afterFrac > BillingStageProgressCalculator.ScaleMax)
            {
                afterFrac = observedFrac;
                remainingFrac = 1m - observedFrac;
                addFrac = 0m;
            }

            observed += BillingStageContributionCalculator.SubContractContributionPercent(
                stage.StageWeightWithinSubContract, observedFrac);
            addition += BillingStageContributionCalculator.SubContractContributionPercent(
                stage.StageWeightWithinSubContract, addFrac);
            after += BillingStageContributionCalculator.SubContractContributionPercent(
                stage.StageWeightWithinSubContract, afterFrac);
            remaining += BillingStageContributionCalculator.SubContractContributionPercent(
                stage.StageWeightWithinSubContract, remainingFrac);
        }

        return new BillingSubContractWeightedSummary(
            observed,
            addition,
            after,
            remaining,
            group.ValidWeightSum * 100m,
            group.WeightsSumApproximatelyToOne,
            group.IsPartialBecauseOfDataQuality);
    }

    public static string FormatWeightSumWarning(decimal validWeightSumPercent) =>
        "משקלי שלבי התשלום בתת החוזה מסתכמים ב-"
        + BillingStageProgressMessages.FormatPercent(validWeightSumPercent)
        + " ולא ב-100%";

    public static string PartialSummaryWarning =>
        "סיכום תת החוזה חלקי — קיימים שלבים עם בעיית איכות נתונים";

    public static string FormatNumberLabel(string? number) =>
        string.IsNullOrWhiteSpace(number) ? string.Empty : "מספר: " + number.Trim();

    public static string FormatStageCount(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + " שלבי תשלום";
}
