using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.MasterPlan;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class MasterPlanHoursNormalizerBillingTests
{
    [Fact]
    public void When_extended_duration_is_valid_then_it_wins_over_total_hours()
    {
        var hours = MasterPlanHoursNormalizer.ConvertExtendedHours(
            duration: 3.5m,
            totalHours: TimeSpan.FromHours(8),
            startTime: TimeSpan.FromHours(9),
            endTime: TimeSpan.FromHours(17));

        Assert.Equal(3.5m, hours);
    }

    [Fact]
    public void When_extended_duration_is_out_of_range_then_total_hours_is_used()
    {
        var hours = MasterPlanHoursNormalizer.ConvertExtendedHours(
            duration: 25m,
            totalHours: TimeSpan.FromHours(2),
            startTime: null,
            endTime: null);

        Assert.Equal(2.00m, hours);
    }

    [Fact]
    public void Hebrew_state_labels_match_the_product_contract()
    {
        Assert.Equal("לבדוק עכשיו", BillingCandidateStateDisplay.ToHebrew(BillingCandidateState.ReviewNow));
        Assert.Equal("עבודה שהצטברה", BillingCandidateStateDisplay.ToHebrew(BillingCandidateState.AccumulatedWork));
        Assert.Equal("אין עבודה חדשה מאז החשבון", BillingCandidateStateDisplay.ToHebrew(BillingCandidateState.CoveredByLatestBill));
        Assert.Equal("כבר יש חשבון ביצירה", BillingCandidateStateDisplay.ToHebrew(BillingCandidateState.BillInPreparation));
        Assert.Equal("לא דחוף", BillingCandidateStateDisplay.ToHebrew(BillingCandidateState.NotUrgent));
    }
}
