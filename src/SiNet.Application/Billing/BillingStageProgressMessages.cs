using System.Globalization;

namespace SiNet.Application.Billing;

/// <summary>
/// Manager-facing percentage wording for stage additions. Internal validation stays on 0–1.
/// </summary>
public static class BillingStageProgressMessages
{
    public static decimal MaxAdditionPercent(decimal observedPercent) =>
        Math.Max(0m, 100m - observedPercent);

    public static string FormatMaxAdditionHint(decimal observedPercent) =>
        "ניתן להוסיף עד " + FormatPercent(MaxAdditionPercent(observedPercent));

    public static string? FormatAdditionError(
        BillingStageProgressCalculator.ObservedCumulativeProgress observed,
        decimal additionFraction)
    {
        ArgumentNullException.ThrowIfNull(observed);

        var validation = BillingStageProgressCalculator.ValidateAddition(observed, additionFraction);
        if (validation.IsValid)
            return null;

        if (observed.HasOutliers)
            return "ערכי התקדמות חריגים. אין לחשב תוספת אוטומטית.";

        if (additionFraction < BillingStageProgressCalculator.ScaleMin)
            return "התוספת בחשבון הזה אינה יכולה להיות שלילית.";

        var observedPercent = (observed.Value ?? 0m) * 100m;
        var enteredPercent = additionFraction * 100m;
        var max = MaxAdditionPercent(observedPercent);
        return "לא ניתן להוסיף "
               + FormatPercent(enteredPercent)
               + " — נותרו רק "
               + FormatPercent(max)
               + " בשלב זה.";
    }

    public static bool IsUserFacingScaleLeak(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("0 ל-1", StringComparison.Ordinal)
            || text.Contains("0..1", StringComparison.Ordinal)
            || text.Contains("יעד מצטבר", StringComparison.Ordinal)
            || text.Contains("TargetCumulativeProgress", StringComparison.OrdinalIgnoreCase));

    public static string FormatPercent(decimal percent) =>
        percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
