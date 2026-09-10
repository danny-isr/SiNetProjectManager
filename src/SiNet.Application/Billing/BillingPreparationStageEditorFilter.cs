namespace SiNet.Application.Billing;

public enum BillingPreparationStageExclusionKind
{
    Completed = 1,
    NonPositiveWeight = 2,
    HourlyFeeType = 3,
    DataQuality = 4
}

public sealed record BillingPreparationStageEditorDecision(
    bool IsEditable,
    BillingPreparationStageExclusionKind? Exclusion,
    string? Reason)
{
    public bool ShowInDataQualityWarning =>
        Exclusion is BillingPreparationStageExclusionKind.DataQuality
            or BillingPreparationStageExclusionKind.NonPositiveWeight;
}

/// <summary>
/// Which MasterPlan stages belong in the manager-facing «שלבים» editor.
/// Hourly FeeType=4 belongs only in «רכיבי שעות». Completed 100% stages are omitted.
/// Data-quality / non-positive-weight rows are excluded and may be listed as a warning.
/// </summary>
public static class BillingPreparationStageEditorFilter
{
    public static BillingPreparationStageEditorDecision Classify(BillingPreparationStageDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.FeeTypeId == MasterPlanSnapshotFeeTypeIds.WorkingHours)
        {
            return new BillingPreparationStageEditorDecision(
                false,
                BillingPreparationStageExclusionKind.HourlyFeeType,
                "הסכם משנה שעתי (FeeType=4) — שייך לרכיבי שעות בלבד.");
        }

        if (draft.StageWeightWithinSubContract <= 0m)
        {
            return new BillingPreparationStageEditorDecision(
                false,
                BillingPreparationStageExclusionKind.NonPositiveWeight,
                draft.StageName + " / " + draft.SubContractName + " — משקל שלב אפסי או שלילי.");
        }

        if (draft.Observed.HasOutliers)
        {
            return new BillingPreparationStageEditorDecision(
                false,
                BillingPreparationStageExclusionKind.DataQuality,
                draft.StageName + " / " + draft.SubContractName + " — ערכי התקדמות חריגים; אין לחשב יעד אוטומטית.");
        }

        if (draft.Observed.Value is decimal value)
        {
            if (!BillingStageProgressCalculator.IsValidScale(value))
            {
                return new BillingPreparationStageEditorDecision(
                    false,
                    BillingPreparationStageExclusionKind.DataQuality,
                    draft.StageName + " / " + draft.SubContractName + " — ערך התקדמות מחוץ ל-0..1.");
            }

            if (value >= BillingStageProgressCalculator.ScaleMax)
            {
                return new BillingPreparationStageEditorDecision(
                    false,
                    BillingPreparationStageExclusionKind.Completed,
                    draft.StageName + " / " + draft.SubContractName + " — חויב/נצפה 100%.");
            }
        }

        return new BillingPreparationStageEditorDecision(true, null, null);
    }

    public static bool IsEditable(BillingPreparationStageDraft draft) =>
        Classify(draft).IsEditable;
}
