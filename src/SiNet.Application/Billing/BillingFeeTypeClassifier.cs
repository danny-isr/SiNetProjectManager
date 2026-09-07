namespace SiNet.Application.Billing;

/// <summary>Classifies snapshot SubContract fee types. Never produces a suggested bill amount.</summary>
public static class BillingFeeTypeClassifier
{
    public static BillingFeeTypeSummary? Classify(IReadOnlyList<int>? feeTypeIds)
    {
        if (feeTypeIds is not { Count: > 0 })
            return null;

        var counts = feeTypeIds
            .GroupBy(id => id)
            .OrderBy(g => g.Key)
            .Select(g => new BillingFeeTypeCount(
                g.Key,
                MasterPlanSnapshotFeeTypeIds.DisplayName(g.Key),
                g.Count()))
            .ToList();

        var distinct = counts.Select(c => c.FeeTypeId).ToList();
        var mix = distinct.Count > 1
            ? BillingSnapshotFeeMix.Mixed
            : distinct[0] switch
            {
                MasterPlanSnapshotFeeTypeIds.FixedPrice => BillingSnapshotFeeMix.FixedPrice,
                MasterPlanSnapshotFeeTypeIds.WorkingHours => BillingSnapshotFeeMix.Hourly,
                _ => BillingSnapshotFeeMix.Other
            };

        return new BillingFeeTypeSummary(distinct, counts, mix);
    }
}
