using SiNet.Application.Inspection;
using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class InspectionStatusOptionsBuilderTests
{
    [Fact]
    public void FromLabels_keeps_stable_db_keys()
    {
        var labels = new InspectionStatusLabelsDto("OK", "BAD", "AGAIN", "N/A");
        var options = InspectionStatusOptionsBuilder.FromLabels(labels);

        Assert.Equal(
            [
                InspectionQuestionnaireRules.Failed,
                "Passed",
                "RecurringFailed",
                InspectionQuestionnaireRules.NotApplicable,
                InspectionQuestionnaireRules.ManagerReview,
            ],
            options.Select(o => o.DbKey).ToArray());
    }

    [Fact]
    public void FromLabels_uses_settings_display_text()
    {
        var labels = new InspectionStatusLabelsDto("תוקן", "ליקוי", "ליקוי חוזר", "לא חל");
        var options = InspectionStatusOptionsBuilder.FromLabels(labels, managerReviewLabel: "לבדיקה");

        Assert.Equal("ליקוי", options.First(o => o.DbKey == InspectionQuestionnaireRules.Failed).Label);
        Assert.Equal("תוקן", options.First(o => o.DbKey == "Passed").Label);
        Assert.Equal("ליקוי חוזר", options.First(o => o.DbKey == "RecurringFailed").Label);
        Assert.Equal("לא חל", options.First(o => o.DbKey == InspectionQuestionnaireRules.NotApplicable).Label);
        Assert.Equal("לבדיקה", options.First(o => o.DbKey == InspectionQuestionnaireRules.ManagerReview).Label);
    }

    [Fact]
    public void FromLabels_falls_back_to_defaults_when_blank()
    {
        var labels = new InspectionStatusLabelsDto(" ", "", "  ", null!);
        var options = InspectionStatusOptionsBuilder.FromLabels(labels);

        Assert.Equal(SystemSettingsDefaults.StatusLabelFailed,
            options.First(o => o.DbKey == InspectionQuestionnaireRules.Failed).Label);
        Assert.Equal(SystemSettingsDefaults.StatusLabelPassed,
            options.First(o => o.DbKey == "Passed").Label);
        Assert.Equal(InspectionStatusOptionsBuilder.ManagerReviewDefaultLabel,
            options.First(o => o.DbKey == InspectionQuestionnaireRules.ManagerReview).Label);
    }
}
