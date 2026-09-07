using SiNet.Application.Billing;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>Monthly <c>Db_Mp_SiEng</c> enrichment payload. Missing data never removes Replica candidates.</summary>
public sealed record MonthlyBillingEnrichmentLoad(
    bool DatabaseConfigured,
    IReadOnlyList<string> MissingTables,
    IReadOnlyDictionary<int, BillingSnapshotProjectEnrichment> Projects)
{
    public static MonthlyBillingEnrichmentLoad Unavailable { get; } = new(
        false,
        Array.Empty<string>(),
        new Dictionary<int, BillingSnapshotProjectEnrichment>());
}

/// <summary>
/// Reads monthly snapshot enrichment only. Never used as current billing facts
/// and never as a fallback when Replica is stale.
/// </summary>
public interface IMonthlyBillingEnrichmentDataSource
{
    Task<MonthlyBillingEnrichmentLoad> LoadAsync(
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken = default);
}
