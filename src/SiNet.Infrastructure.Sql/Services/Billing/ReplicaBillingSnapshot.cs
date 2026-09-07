using SiNet.Application.Billing;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>Replica-only snapshot for the billing candidate engine (B1; no monthly enrichment).</summary>
public sealed record ReplicaBillingSnapshot(
    IReadOnlyList<BillingProjectFact> Projects,
    IReadOnlyList<BillingBillFact> Bills,
    IReadOnlyList<BillingHourFact> Hours,
    decimal? ReceivedThisMonth,
    int HoursOnlyInBasicCount);
