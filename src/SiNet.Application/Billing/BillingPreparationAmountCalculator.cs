namespace SiNet.Application.Billing;

public sealed record BillingPricedValue(decimal? Amount, string? UnavailableReason)
{
    public bool IsPriced => Amount is not null && string.IsNullOrWhiteSpace(UnavailableReason);
}

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
}
