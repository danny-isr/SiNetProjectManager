namespace SiNet.Application.Billing;

/// <summary>
/// Replica-first billing candidate read model. Does not write MasterPlan or SiNet financial facts.
/// Current dashboards return no candidates when Replica freshness is stale/fatal.
/// </summary>
public interface IBillingDashboardReadService
{
    Task<BillingDashboardResult> GetAsync(
        BillingDashboardRequest request,
        CancellationToken cancellationToken = default);
}
