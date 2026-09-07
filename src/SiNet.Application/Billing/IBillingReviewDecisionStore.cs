namespace SiNet.Application.Billing;

/// <summary>Read port for current local billing decisions (including cleared rows).</summary>
public interface IBillingReviewDecisionStore
{
    Task<IReadOnlyList<BillingReviewDecisionRecord>> GetByProjectIdsAsync(
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken = default);
}
