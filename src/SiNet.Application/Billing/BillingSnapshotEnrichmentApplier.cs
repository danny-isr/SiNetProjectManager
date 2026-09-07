namespace SiNet.Application.Billing;

/// <summary>
/// Overlays monthly snapshot enrichment onto Replica candidate rows.
/// Replica identity, status, hours, bills, and <see cref="BillingCandidateState"/> always win.
/// </summary>
public static class BillingSnapshotEnrichmentApplier
{
    public static IReadOnlyList<BillingCandidateRow> Apply(
        IReadOnlyList<BillingCandidateRow> rows,
        IReadOnlyDictionary<int, BillingSnapshotProjectEnrichment>? enrichmentByProject,
        DateTime? snapshotDate)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return rows;

        enrichmentByProject ??= new Dictionary<int, BillingSnapshotProjectEnrichment>();

        var result = new List<BillingCandidateRow>(rows.Count);
        foreach (var row in rows)
        {
            enrichmentByProject.TryGetValue(row.ProjectId, out var extra);
            result.Add(row with
            {
                CustomerName = MergeCustomer(row.CustomerName, extra?.CustomerName),
                SnapshotBalance = extra?.Balance,
                SnapshotOpenBillSum = extra?.OpenBillSum,
                SnapshotApprovedBillSum = extra?.ApprovedBillSum,
                SnapshotBilledPercent = extra?.ProgressPercentage,
                SnapshotDate = snapshotDate,
                SnapshotFeeTypes = BillingFeeTypeClassifier.Classify(extra?.SubContractFeeTypeIds)
            });
        }

        return result;
    }

    internal static string? MergeCustomer(string? replicaCustomerName, string? snapshotCustomerName)
    {
        if (!string.IsNullOrWhiteSpace(replicaCustomerName))
            return replicaCustomerName.Trim();

        if (string.IsNullOrWhiteSpace(snapshotCustomerName))
            return null;

        return snapshotCustomerName.Trim();
    }
}
