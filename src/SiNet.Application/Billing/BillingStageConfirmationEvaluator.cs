namespace SiNet.Application.Billing;

/// <summary>
/// Stage auto-confirm only when a restored snapshot is newer than approval and
/// observed cumulative progress meets the approved target.
/// </summary>
public static class BillingStageConfirmationEvaluator
{
    public static bool CanAutoConfirm(
        BillingPreparationStageLineSnapshot approved,
        BillingStageProgressCalculator.ObservedCumulativeProgress currentObserved,
        DateTime? evidenceSnapshotUtc,
        DateTime approvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(currentObserved);

        if (approved.HasDataQualityFlag || currentObserved.HasOutliers)
            return false;
        if (evidenceSnapshotUtc is not DateTime evidence)
            return false;
        if (evidence <= approvedAtUtc)
            return false;
        if (!currentObserved.CanUseForAutomaticCalculation)
            return false;

        return currentObserved.Value >= approved.TargetCumulativeProgress;
    }
}
