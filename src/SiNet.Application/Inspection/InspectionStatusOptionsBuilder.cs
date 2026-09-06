using SiNet.Application.Settings;

namespace SiNet.Application.Inspection;

/// <summary>
/// Builds Inspection status ComboBox options from system settings labels while keeping
/// stable internal status codes (<see cref="InspectionQuestionnaireRules"/>).
/// </summary>
public static class InspectionStatusOptionsBuilder
{
    public const string ManagerReviewDefaultLabel = "הערה לבדיקת המנהל";

    /// <summary>
    /// Creates display options. DbKey values are fixed English codes used in persistence/validation.
    /// </summary>
    public static IReadOnlyList<(string DbKey, string Label)> FromLabels(
        InspectionStatusLabelsDto? labels,
        string? managerReviewLabel = null)
    {
        labels ??= new InspectionStatusLabelsDto(
            SystemSettingsDefaults.StatusLabelPassed,
            SystemSettingsDefaults.StatusLabelFailed,
            SystemSettingsDefaults.StatusLabelRecurringFailed,
            SystemSettingsDefaults.StatusLabelNotApplicable);

        return
        [
            (InspectionQuestionnaireRules.Failed, NonEmpty(labels.Failed, SystemSettingsDefaults.StatusLabelFailed)),
            ("Passed", NonEmpty(labels.Passed, SystemSettingsDefaults.StatusLabelPassed)),
            ("RecurringFailed", NonEmpty(labels.RecurringFailed, SystemSettingsDefaults.StatusLabelRecurringFailed)),
            (InspectionQuestionnaireRules.NotApplicable, NonEmpty(labels.NotApplicable, SystemSettingsDefaults.StatusLabelNotApplicable)),
            (InspectionQuestionnaireRules.ManagerReview, NonEmpty(managerReviewLabel, ManagerReviewDefaultLabel)),
        ];
    }

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
