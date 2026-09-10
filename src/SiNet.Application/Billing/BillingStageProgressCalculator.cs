namespace SiNet.Application.Billing;

/// <summary>
/// Cumulative stage progress from MasterPlan <c>BillLines.StepProgress</c> (0.00–1.00).
/// Never sums historical values. Outliers outside [0,1] are flagged and excluded from MAX.
/// </summary>
public static class BillingStageProgressCalculator
{
    public const decimal ScaleMin = 0m;
    public const decimal ScaleMax = 1m;

    public sealed record ObservedCumulativeProgress(
        decimal? Value,
        bool HasOutliers,
        IReadOnlyList<decimal> OutlierValues)
    {
        public bool CanUseForAutomaticCalculation =>
            Value is decimal v && !HasOutliers && v >= ScaleMin && v <= ScaleMax;
    }

    public sealed record TargetValidation(
        bool IsValid,
        decimal? Observed,
        decimal Target,
        decimal Delta,
        string? Error)
    {
        public bool HasDataQualityFlag { get; init; }
    }

    public static bool IsValidScale(decimal value) =>
        value >= ScaleMin && value <= ScaleMax;

    /// <summary>
    /// Observed cumulative progress = MAX of in-range StepProgress values from accepted bills.
    /// </summary>
    public static ObservedCumulativeProgress Observe(IEnumerable<decimal> stepProgressValues)
    {
        ArgumentNullException.ThrowIfNull(stepProgressValues);

        var outliers = new List<decimal>();
        decimal? max = null;
        foreach (var value in stepProgressValues)
        {
            if (!IsValidScale(value))
            {
                outliers.Add(value);
                continue;
            }

            if (max is null || value > max.Value)
                max = value;
        }

        return new ObservedCumulativeProgress(max, outliers.Count > 0, outliers);
    }

    public static TargetValidation ValidateTarget(
        ObservedCumulativeProgress observed,
        decimal target)
    {
        ArgumentNullException.ThrowIfNull(observed);

        if (observed.HasOutliers)
        {
            return new TargetValidation(
                false,
                observed.Value,
                target,
                0m,
                "ערכי StepProgress חריגים (מחוץ ל-0..1). אין לחשב יעד אוטומטית.")
            {
                HasDataQualityFlag = true
            };
        }

        if (!IsValidScale(target))
        {
            return new TargetValidation(
                false,
                observed.Value,
                target,
                0m,
                "יעד מצטבר חייב להיות בין 0 ל-1.");
        }

        if (observed.Value is not decimal current)
        {
            var deltaWhenUnknown = target;
            return new TargetValidation(true, null, target, deltaWhenUnknown, null);
        }

        if (target < current)
        {
            return new TargetValidation(
                false,
                current,
                target,
                target - current,
                "היעד המצטבר אינו יכול להיות נמוך מהמצב שנצפה.");
        }

        return new TargetValidation(true, current, target, target - current, null);
    }

    /// <summary>
    /// Manager-facing addition on the 0–1 scale. Target = observed-or-zero + addition.
    /// Unknown observed without outliers starts at 0. Outliers are never treated as 0.
    /// </summary>
    public static TargetValidation ValidateAddition(
        ObservedCumulativeProgress observed,
        decimal addition)
    {
        ArgumentNullException.ThrowIfNull(observed);

        if (observed.HasOutliers)
        {
            return new TargetValidation(
                false,
                observed.Value,
                addition,
                0m,
                "ערכי StepProgress חריגים (מחוץ ל-0..1). אין לחשב יעד אוטומטית.")
            {
                HasDataQualityFlag = true
            };
        }

        if (addition < ScaleMin)
        {
            return new TargetValidation(
                false,
                observed.Value,
                observed.Value ?? ScaleMin,
                addition,
                "התוספת בחשבון הזה אינה יכולה להיות שלילית.");
        }

        var start = observed.Value ?? ScaleMin;
        var target = start + addition;
        return ValidateTarget(observed, target);
    }
}
