namespace SiNet.Application.Billing;

/// <summary>
/// SiNet-owned billing review writes. Does not touch MasterPlan, Replica, or financial facts.
/// </summary>
public interface IBillingReviewDecisionService
{
    Task SaveAsync(
        BillingReviewDecisionWriteRequest request,
        CancellationToken cancellationToken = default);

    Task ClearAsync(int projectId, CancellationToken cancellationToken = default);
}
