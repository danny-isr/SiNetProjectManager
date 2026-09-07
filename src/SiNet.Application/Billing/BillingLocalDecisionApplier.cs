namespace SiNet.Application.Billing;

/// <summary>
/// Attaches an active local-decision overlay. Never changes hours, bills, balances,
/// or <see cref="BillingCandidateState"/>.
/// </summary>
public static class BillingLocalDecisionApplier
{
    public static IReadOnlyList<BillingCandidateRow> Apply(
        IReadOnlyList<BillingCandidateRow> rows,
        IReadOnlyList<BillingReviewDecisionRecord>? decisions,
        DateTime asOfDate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0 || decisions is not { Count: > 0 })
            return rows;

        var byProject = decisions
            .GroupBy(d => d.ProjectId)
            .ToDictionary(g => g.Key, g => g.Last());

        var result = new List<BillingCandidateRow>(rows.Count);
        var asOf = asOfDate.Date;
        foreach (var row in rows)
        {
            if (!byProject.TryGetValue(row.ProjectId, out var stored))
            {
                result.Add(row);
                continue;
            }

            var effect = Evaluate(row, stored, asOf);
            if (effect != BillingLocalDecisionEffect.Active)
            {
                result.Add(row);
                continue;
            }

            result.Add(row with
            {
                LocalDecision = new BillingLocalDecisionOverlay(
                    stored.DecisionType,
                    stored.Reason,
                    stored.ReviewAgainDate,
                    stored.CreatedAtUtc,
                    stored.CreatedByUserId,
                    stored.CreatedByLogin,
                    stored.UpdatedAtUtc,
                    stored.UpdatedByUserId,
                    stored.UpdatedByLogin,
                    effect)
            });
        }

        return result;
    }

    public static BillingLocalDecisionEffect Evaluate(
        BillingCandidateRow row,
        BillingReviewDecisionRecord stored,
        DateTime asOfDate)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.ClearedAtUtc is not null)
            return BillingLocalDecisionEffect.Cleared;

        if (HasNewerBill(row.LatestBillId, stored.ObservedLatestBillId))
            return BillingLocalDecisionEffect.Superseded;

        if (stored.DecisionType == BillingLocalDecisionType.NotNow
            && stored.ReviewAgainDate is DateTime review
            && review.Date <= asOfDate.Date)
        {
            return BillingLocalDecisionEffect.Expired;
        }

        return BillingLocalDecisionEffect.Active;
    }

    /// <summary>
    /// A new or different MasterPlan latest bill after the decision. Null current latest bill
    /// is not treated as advancement.
    /// </summary>
    internal static bool HasNewerBill(int? currentLatestBillId, int? observedLatestBillId)
    {
        if (currentLatestBillId is null)
            return false;
        if (observedLatestBillId is null)
            return true;
        return currentLatestBillId.Value != observedLatestBillId.Value;
    }

    public static bool SuppressFromDefaultActionableView(BillingCandidateRow row, DateTime asOfDate)
    {
        ArgumentNullException.ThrowIfNull(row);
        var local = row.LocalDecision;
        if (local is null || local.Effect != BillingLocalDecisionEffect.Active)
            return false;
        if (local.DecisionType != BillingLocalDecisionType.NotNow)
            return false;
        return local.ReviewAgainDate is DateTime review && review.Date > asOfDate.Date;
    }

    public static bool IncludeInDefaultActionableView(
        BillingCandidateRow row,
        IReadOnlyCollection<BillingCandidateState> computedActionable,
        DateTime asOfDate)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(computedActionable);

        if (SuppressFromDefaultActionableView(row, asOfDate))
            return false;

        if (row.LocalDecision is { Effect: BillingLocalDecisionEffect.Active, DecisionType: BillingLocalDecisionType.PrepareBill })
            return true;

        return computedActionable.Contains(row.CandidateState);
    }
}
