namespace SiNet.Application.Billing;

/// <summary>
/// Stable business identifier for the proven MasterPlan billing formulas.
/// Not an implementation class name.
/// </summary>
public static class BillingPreparationPricing
{
    public const string FormulaVersion = "MP-BILLING-1";
}

/// <summary>
/// Approval-time pricing evidence captured from the already-saved selection
/// and the MasterPlan catalog loaded for that attempt. Live UI preview stays unpersisted.
/// </summary>
public static class BillingPreparationPricingFreeze
{
    public static bool HasFreeze(BillingPreparationRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.PricingFrozenAtUtc is not null;
    }

    public static BillingPreparationRequestRecord Capture(
        BillingPreparationRequestRecord request,
        BillingPreparationSnapshotLoad catalog,
        DateTime frozenAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);

        var stages = request.Stages.Select(s => FreezeStage(s, catalog.Stages)).ToList();
        var hours = request.Hours.Select(h => FreezeHours(h, catalog.HourlySubContracts)).ToList();

        decimal stageTotal = 0m;
        decimal hoursTotal = 0m;
        var partial = false;
        foreach (var stage in stages)
        {
            if (stage.PricingCalculatedAmount is decimal amount)
                stageTotal += amount;
            else
                partial = true;
        }

        foreach (var hour in hours)
        {
            if (hour.PricingCalculatedAmount is decimal amount)
                hoursTotal += amount;
            else
                partial = true;
        }

        stageTotal = decimal.Round(stageTotal, 4, MidpointRounding.AwayFromZero);
        hoursTotal = decimal.Round(hoursTotal, 4, MidpointRounding.AwayFromZero);
        return request with
        {
            Stages = stages,
            Hours = hours,
            PricingStageTotal = stageTotal,
            PricingHoursTotal = hoursTotal,
            PricingTotal = decimal.Round(stageTotal + hoursTotal, 4, MidpointRounding.AwayFromZero),
            PricingIsPartial = partial,
            PricingFrozenAtUtc = frozenAtUtc,
            PricingSourceSnapshotUtc = catalog.LatestBackupUtc ?? request.SnapshotTimestampUtc,
            PricingFormulaVersion = BillingPreparationPricing.FormulaVersion
        };
    }

    public static BillingPreparationRequestRecord Clear(BillingPreparationRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request with
        {
            PricingStageTotal = null,
            PricingHoursTotal = null,
            PricingTotal = null,
            PricingIsPartial = null,
            PricingFrozenAtUtc = null,
            PricingSourceSnapshotUtc = null,
            PricingFormulaVersion = null,
            Stages = request.Stages
                .Select(s => s with
                {
                    PricingBaseAmount = null,
                    PricingDiscountFraction = null,
                    PricingCalculatedAmount = null,
                    PricingUnavailableReason = null
                })
                .ToList(),
            Hours = request.Hours
                .Select(h => h with
                {
                    PricingHourlyRate = null,
                    PricingDiscountFraction = null,
                    PricingCalculatedAmount = null,
                    PricingUnavailableReason = null
                })
                .ToList()
        };
    }

    public static string PartialApprovalBlockedMessage(BillingPreparationRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var missing = ListMissing(request);
        var details = missing.Count == 0
            ? "יש רכיבים ללא תמחור."
            : string.Join("; ", missing);
        return "לא ניתן לאשר — יש רכיבים שנבחרו ללא בסיס תמחור. " + details;
    }

    public static IReadOnlyList<string> ListMissing(BillingPreparationRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var missing = new List<string>();
        foreach (var stage in request.Stages)
        {
            if (stage.PricingCalculatedAmount is not null)
                continue;
            missing.Add(stage.StageName + " — " + (stage.PricingUnavailableReason ?? "לא נמצא בסיס תמחור"));
        }

        foreach (var hours in request.Hours)
        {
            if (hours.PricingCalculatedAmount is not null)
                continue;
            missing.Add(hours.SubContractName + " — " + (hours.PricingUnavailableReason ?? "תעריף לא ניתן לקביעה"));
        }

        return missing;
    }

    private static BillingPreparationStageLineSnapshot FreezeStage(
        BillingPreparationStageLineSnapshot line,
        IReadOnlyList<BillingPreparationStageDraft> catalog)
    {
        var match = catalog.FirstOrDefault(d => d.MasterPlanStageId == line.MasterPlanStageId);
        var draft = match is null
            ? new BillingPreparationStageDraft(
                line.MasterPlanStageId,
                line.MasterPlanSubContractId,
                line.StageName,
                line.SubContractName,
                line.StageWeightWithinSubContract,
                new BillingStageProgressCalculator.ObservedCumulativeProgress(
                    line.ObservedCumulativeProgress, line.HasDataQualityFlag, []),
                line.ObservedCumulativeProgress,
                Included: true)
            : match with { StageWeightWithinSubContract = line.StageWeightWithinSubContract };

        var priced = BillingPreparationAmountCalculator.StageAddition(draft, line.RequestedDelta);
        return line with
        {
            PricingBaseAmount = draft.SubContractBillableAmount,
            PricingDiscountFraction = match is null ? null : draft.DiscountFraction,
            PricingCalculatedAmount = priced.IsPriced ? priced.Amount : null,
            PricingUnavailableReason = priced.IsPriced
                ? null
                : priced.UnavailableReason ?? "לא נמצא בסיס תמחור"
        };
    }

    private static BillingPreparationHoursLineSnapshot FreezeHours(
        BillingPreparationHoursLineSnapshot line,
        IReadOnlyList<BillingHourlySubContractDraft> catalog)
    {
        var match = catalog.FirstOrDefault(c => c.MasterPlanSubContractId == line.MasterPlanSubContractId);
        var draft = match ?? new BillingHourlySubContractDraft(
            line.MasterPlanSubContractId,
            line.SubContractName,
            MasterPlanSnapshotFeeTypeIds.WorkingHours,
            AmountUnavailableReason: "לא נמצא בסיס תמחור שעתי");

        var priced = BillingPreparationAmountCalculator.Hourly(draft, line.TotalHours);
        return line with
        {
            PricingHourlyRate = match?.UniqueHourlyRate,
            PricingDiscountFraction = match is null ? null : draft.HourlyDiscountFraction,
            PricingCalculatedAmount = priced.IsPriced ? priced.Amount : null,
            PricingUnavailableReason = priced.IsPriced
                ? null
                : priced.UnavailableReason ?? "תעריף לא ניתן לקביעה"
        };
    }

    public static bool HasLineEvidence(BillingPreparationStageLineSnapshot stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.PricingCalculatedAmount is not null
               || !string.IsNullOrWhiteSpace(stage.PricingUnavailableReason);
    }

    public static bool HasLineEvidence(BillingPreparationHoursLineSnapshot hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        return hours.PricingCalculatedAmount is not null
               || hours.PricingHourlyRate is not null
               || !string.IsNullOrWhiteSpace(hours.PricingUnavailableReason);
    }
}
