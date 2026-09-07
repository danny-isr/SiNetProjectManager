using SiNet.Application.Billing;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>Replica-only billing read. Probe first; facts are loaded only after the freshness gate.</summary>
public interface IReplicaBillingDataSource
{
    Task<ReplicaBillingProbe> ProbeAsync(CancellationToken cancellationToken = default);

    Task<ReplicaBillingSnapshot> LoadFactsAsync(
        BillingDashboardRequest request,
        CancellationToken cancellationToken = default);
}
