namespace SiNet.Application.Billing;

/// <summary>
/// Derived display math for Billing Preparation. Stage weight is of the SubContract;
/// progress values are of the stage. Contributions are not persisted.
/// </summary>
public static class BillingStageContributionCalculator
{
    public static decimal ToDisplayPercent(decimal fraction) => fraction * 100m;

    /// <summary>
    /// Weighted share of a SubContract: <c>StageWeight × stageProgress</c> as a percent.
    /// Example: weight 0.25, progress 0.10 → 2.5% of the SubContract.
    /// </summary>
    public static decimal SubContractContributionPercent(decimal stageWeight, decimal stageProgressFraction) =>
        stageWeight * stageProgressFraction * 100m;
}
