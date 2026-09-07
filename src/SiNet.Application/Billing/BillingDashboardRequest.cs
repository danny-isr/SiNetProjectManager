namespace SiNet.Application.Billing;

/// <summary>Filter for the billing candidate read model.</summary>
public sealed record BillingDashboardRequest(
    bool ActiveOnly = true,
    IReadOnlyList<int>? ProjectIds = null,
    IReadOnlyList<int>? CustomerIds = null,
    IReadOnlyList<BillingCandidateState>? CandidateStates = null,
    DateTime? AsOfDate = null);
