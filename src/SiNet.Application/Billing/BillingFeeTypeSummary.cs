namespace SiNet.Application.Billing;

/// <summary>MasterPlan monthly <c>FeeTypes.ID</c> values. Not a billing-amount formula.</summary>
public static class MasterPlanSnapshotFeeTypeIds
{
    public const int PercentOfCost = 1;
    public const int Units = 2;
    public const int FixedPrice = 3;
    public const int WorkingHours = 4;

    public static string DisplayName(int feeTypeId) => feeTypeId switch
    {
        PercentOfCost => "אחוז מעלות",
        Units => "יחידות",
        FixedPrice => "מחיר קבוע",
        WorkingHours => "שעות עבודה",
        _ => "אחר"
    };
}

/// <summary>Coarse fee mix from snapshot SubContracts. Does not drive <see cref="BillingCandidateState"/>.</summary>
public enum BillingSnapshotFeeMix
{
    FixedPrice = 1,
    Hourly = 2,
    Mixed = 3,
    Other = 4
}

public sealed record BillingFeeTypeCount(int FeeTypeId, string DisplayName, int Count);

/// <summary>Per-project snapshot fee-type summary (SubContracts via Contracts).</summary>
public sealed record BillingFeeTypeSummary(
    IReadOnlyList<int> DistinctFeeTypeIds,
    IReadOnlyList<BillingFeeTypeCount> Counts,
    BillingSnapshotFeeMix Mix);
