namespace SiNet.Application.Billing;

public sealed record BillingPricedValue(decimal? Amount, string? UnavailableReason)
{
    public bool IsPriced => Amount is not null && string.IsNullOrWhiteSpace(UnavailableReason);
}

public sealed record BillingSubContractMoneySummary(
    BillingPricedValue AlreadyBilled,
    BillingPricedValue CurrentAddition,
    BillingPricedValue AfterAccount,
    BillingPricedValue Remaining,
    BillingPricedValue SubcontractTotal);

public sealed record BillingLiveAmountMissing(string Label, string Reason);

public sealed record BillingLiveAmountSummary(
    decimal PricedStageTotal,
    decimal PricedHoursTotal,
    decimal PricedTotal,
    IReadOnlyList<BillingLiveAmountMissing> Missing)
{
    public bool IsPartial => Missing.Count > 0;
    public bool IsZeroSelection =>
        PricedTotal == 0m && Missing.Count == 0;
}

/// <summary>
/// Live preparation amounts from proven MasterPlan formulas. Never treats unknown as zero.
/// </summary>
public static class BillingPreparationAmountCalculator
{
    public static BillingPricedValue StageAddition(BillingPreparationStageDraft draft, decimal deltaFraction)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (deltaFraction <= 0m)
            return new BillingPricedValue(0m, null);
        if (draft.HasUnpricedIndexation)
            return new BillingPricedValue(null, "הצמדה למדד לא ניתנת לחישוב אוטומטי");
        if (draft.SubContractBillableAmount is not decimal basis)
            return new BillingPricedValue(null, "לא נמצא בסיס תמחור");
        if (draft.DiscountFraction < 0m || draft.DiscountFraction > 1m)
            return new BillingPricedValue(null, "הנחה חריגה");

        var net = basis * (1m - draft.DiscountFraction);
        return new BillingPricedValue(
            decimal.Round(net * draft.StageWeightWithinSubContract * deltaFraction, 4, MidpointRounding.AwayFromZero),
            null);
    }

    /// <summary>
    /// Money for the four SubContract progress buckets. Uses <see cref="StageAddition"/>
    /// (FixedPrices × discount × stage weight × progress). Never invents a percent × FeeSum path.
    /// After = already + addition. Remaining = subcontract total − after.
    /// </summary>
    public static BillingSubContractMoneySummary SubContractProgressAmounts(
        IReadOnlyList<BillingPreparationStageDraft> stages,
        IReadOnlyDictionary<int, decimal> additionFractionsByStageId)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(additionFractionsByStageId);

        var already = SumPriced(stages, additionFractionsByStageId, Bucket.Already);
        var addition = SumPriced(stages, additionFractionsByStageId, Bucket.Addition);
        var total = SumPriced(stages, additionFractionsByStageId, Bucket.Total);
        var after = already.IsPriced && addition.IsPriced
            ? new BillingPricedValue(
                decimal.Round(already.Amount!.Value + addition.Amount!.Value, 4, MidpointRounding.AwayFromZero),
                null)
            : FirstUnpriced(already, addition);
        var remaining = total.IsPriced && after.IsPriced
            ? new BillingPricedValue(
                decimal.Round(total.Amount!.Value - after.Amount!.Value, 4, MidpointRounding.AwayFromZero),
                null)
            : FirstUnpriced(total, after);
        return new BillingSubContractMoneySummary(already, addition, after, remaining, total);
    }

    public static BillingPricedValue Hourly(BillingHourlySubContractDraft draft, decimal hours)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!string.IsNullOrWhiteSpace(draft.AmountUnavailableReason))
            return new BillingPricedValue(null, draft.AmountUnavailableReason);
        if (hours <= 0m)
            return new BillingPricedValue(0m, null);
        if (draft.UniqueHourlyRate is not decimal rate)
            return new BillingPricedValue(null, "תעריף לא ניתן לקביעה");
        if (draft.HourlyDiscountFraction < 0m || draft.HourlyDiscountFraction > 1m)
            return new BillingPricedValue(null, "הנחת שעות חריגה");

        return new BillingPricedValue(
            decimal.Round(hours * rate * (1m - draft.HourlyDiscountFraction), 4, MidpointRounding.AwayFromZero),
            null);
    }

    public static BillingLiveAmountSummary Summarize(
        IEnumerable<(BillingPreparationStageDraft Draft, decimal DeltaFraction)> stages,
        IEnumerable<(BillingHourlySubContractDraft Draft, decimal Hours, string Label)> hours)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(hours);

        decimal stageTotal = 0m;
        decimal hoursTotal = 0m;
        var missing = new List<BillingLiveAmountMissing>();
        foreach (var (draft, delta) in stages)
        {
            if (delta <= 0m)
                continue;
            var priced = StageAddition(draft, delta);
            if (priced.IsPriced)
                stageTotal += priced.Amount!.Value;
            else
                missing.Add(new BillingLiveAmountMissing(draft.StageName, priced.UnavailableReason ?? "לא נמצא בסיס תמחור"));
        }

        foreach (var (draft, hrs, label) in hours)
        {
            if (hrs < 0m)
                continue;
            var priced = Hourly(draft, hrs);
            if (hrs == 0m && priced.IsPriced)
            {
                hoursTotal += 0m;
                continue;
            }

            if (hrs == 0m)
                continue;
            if (priced.IsPriced)
                hoursTotal += priced.Amount!.Value;
            else
                missing.Add(new BillingLiveAmountMissing(label, priced.UnavailableReason ?? "תעריף לא ניתן לקביעה"));
        }

        return new BillingLiveAmountSummary(
            decimal.Round(stageTotal, 4, MidpointRounding.AwayFromZero),
            decimal.Round(hoursTotal, 4, MidpointRounding.AwayFromZero),
            decimal.Round(stageTotal + hoursTotal, 4, MidpointRounding.AwayFromZero),
            missing);
    }

    private enum Bucket
    {
        Already,
        Addition,
        Total
    }

    private static BillingPricedValue SumPriced(
        IReadOnlyList<BillingPreparationStageDraft> stages,
        IReadOnlyDictionary<int, decimal> additionFractionsByStageId,
        Bucket bucket)
    {
        decimal sum = 0m;
        string? unavailable = null;
        foreach (var stage in stages)
        {
            additionFractionsByStageId.TryGetValue(stage.MasterPlanStageId, out var requestedAdd);
            BillingSubContractStageGroupBuilder.ResolveProgressFractions(
                stage.Observed.Value ?? 0m,
                requestedAdd,
                out var addFrac,
                out _,
                out _);
            var fraction = bucket switch
            {
                Bucket.Already => stage.Observed.Value ?? 0m,
                Bucket.Addition => addFrac,
                _ => 1m
            };
            var priced = StageAddition(stage, fraction);
            if (!priced.IsPriced)
            {
                unavailable ??= priced.UnavailableReason ?? "לא נמצא בסיס תמחור";
                continue;
            }

            sum += priced.Amount!.Value;
        }

        if (unavailable is not null)
            return new BillingPricedValue(null, unavailable);
        return new BillingPricedValue(decimal.Round(sum, 4, MidpointRounding.AwayFromZero), null);
    }

    private static BillingPricedValue FirstUnpriced(BillingPricedValue left, BillingPricedValue right) =>
        !left.IsPriced
            ? left
            : !right.IsPriced
                ? right
                : left;
}
