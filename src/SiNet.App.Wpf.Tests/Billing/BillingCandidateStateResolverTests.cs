using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingCandidateStateResolverTests
{
    [Fact]
    public void When_bill_in_creation_and_many_hours_then_BillInPreparation()
    {
        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation: 1,
            hoursSinceLastBill: 200m,
            hours30: 40m,
            hasRealLastBill: true);

        Assert.Equal(BillingCandidateState.BillInPreparation, state);
    }

    [Fact]
    public void When_no_hours_since_real_last_bill_then_CoveredByLatestBill()
    {
        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation: 0,
            hoursSinceLastBill: 0m,
            hours30: 104.75m,
            hasRealLastBill: true);

        Assert.Equal(BillingCandidateState.CoveredByLatestBill, state);
    }

    [Fact]
    public void When_hours30_and_hours_since_bill_then_ReviewNow()
    {
        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation: 0,
            hoursSinceLastBill: 201.25m,
            hours30: 38.67m,
            hasRealLastBill: true);

        Assert.Equal(BillingCandidateState.ReviewNow, state);
    }

    [Fact]
    public void When_no_hours30_but_hours_since_bill_then_AccumulatedWork()
    {
        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation: 0,
            hoursSinceLastBill: 80m,
            hours30: 0m,
            hasRealLastBill: false);

        Assert.Equal(BillingCandidateState.AccumulatedWork, state);
    }

    [Fact]
    public void When_no_work_and_no_real_bill_then_NotUrgent()
    {
        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation: 0,
            hoursSinceLastBill: 0m,
            hours30: 0m,
            hasRealLastBill: false);

        Assert.Equal(BillingCandidateState.NotUrgent, state);
    }

    [Fact]
    public void When_submitted_bills_exist_then_still_ReviewNow_if_recent_work()
    {
        // BillsSubmitted is not a hard stop — resolver does not take that count.
        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation: 0,
            hoursSinceLastBill: 10m,
            hours30: 10m,
            hasRealLastBill: true);

        Assert.Equal(BillingCandidateState.ReviewNow, state);
    }
}
