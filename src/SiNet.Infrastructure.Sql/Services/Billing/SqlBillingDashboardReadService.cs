using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Billing;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Replica-first billing dashboard with optional monthly snapshot enrichment (B2)
/// and SiNet local-decision overlay (B5). Freshness vs now gates current requests;
/// stale Replica still blocks before local decisions are loaded.
/// Snapshot fields and local decisions never override Replica facts or <see cref="BillingCandidateState"/>.
/// </summary>
public sealed class SqlBillingDashboardReadService(
    IReplicaBillingDataSource replica,
    IMonthlyBillingEnrichmentDataSource? monthly = null,
    TimeProvider? timeProvider = null,
    BillingReplicaFreshnessOptions? freshnessOptions = null,
    IAppLogger? logger = null,
    IBillingReviewDecisionStore? localDecisions = null) : IBillingDashboardReadService
{
    private readonly IReplicaBillingDataSource _replica =
        replica ?? throw new ArgumentNullException(nameof(replica));
    private readonly IMonthlyBillingEnrichmentDataSource? _monthly = monthly;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly BillingReplicaFreshnessOptions _freshnessOptions =
        freshnessOptions ?? BillingReplicaFreshnessOptions.Default;

    public async Task<BillingDashboardResult> GetAsync(
        BillingDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var localToday = _timeProvider.GetLocalNow().Date;
        var probe = await _replica.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            probe.MissingRequiredTables,
            probe.SyncStateTablePresent,
            probe.SyncTimesByEntity,
            utcNow,
            localToday,
            request.AsOfDate,
            _freshnessOptions);

        var freshness = probe.Diagnostics.ToSourceFreshness();
        var warnings = new List<BillingDataQualityWarning>();
        if (!string.IsNullOrWhiteSpace(decision.Code))
        {
            warnings.Add(new BillingDataQualityWarning(decision.Code, decision.Message));
            logger?.Warn($"[Billing] replica-freshness {decision.Code}: {decision.Message}");
        }

        if (decision.CandidatesBlocked)
        {
            return new BillingDashboardResult(
                new BillingDashboardSummary(
                    ReviewNowCount: 0,
                    BillInPreparationCount: 0,
                    SubmittedOpenAmount: null,
                    ReceivedThisMonth: null,
                    ReplicaLastSyncTime: freshness.ReplicaLastSyncTime,
                    MonthlySnapshotDate: null,
                    AccumulatedWorkCount: 0,
                    CoveredByLatestBillCount: 0,
                    NotUrgentCount: 0),
                Candidates: Array.Empty<BillingCandidateRow>(),
                Freshness: freshness,
                Warnings: warnings,
                Diagnostics: probe.Diagnostics,
                FreshnessStatus: decision.Status,
                CandidatesBlocked: true);
        }

        var snapshot = await _replica.LoadFactsAsync(request, cancellationToken).ConfigureAwait(false);
        var asOf = (request.AsOfDate ?? localToday).Date;
        var allRows = BillingCandidateEngine.BuildRows(
            snapshot.Projects,
            snapshot.Bills,
            snapshot.Hours,
            asOf);

        DateTime? monthlyDate = null;
        if (probe.SyncTimesByEntity.TryGetValue(
                BillingReplicaRequirements.MonthlyRestoreSyncStateEntity, out var restoreStamp))
        {
            monthlyDate = restoreStamp;
        }

        if (_monthly is not null)
        {
            var projectIds = allRows.Select(r => r.ProjectId).ToList();
            var monthlyLoad = await _monthly.LoadAsync(projectIds, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(BillingSnapshotWarningBuilder.Build(
                monthlyLoad.DatabaseConfigured,
                monthlyLoad.MissingTables,
                monthlyDate,
                logger));
            allRows = BillingSnapshotEnrichmentApplier.Apply(allRows, monthlyLoad.Projects, monthlyDate);
            freshness = freshness with { MonthlySnapshotDate = monthlyDate };
        }

        if (localDecisions is not null && allRows.Count > 0)
        {
            var stored = await localDecisions
                .GetByProjectIdsAsync(allRows.Select(r => r.ProjectId).ToList(), cancellationToken)
                .ConfigureAwait(false);
            allRows = BillingLocalDecisionApplier.Apply(allRows, stored, asOf);
        }

        IReadOnlyList<BillingCandidateRow> rows = allRows;
        if (request.CandidateStates is { Count: > 0 })
        {
            var allowed = request.CandidateStates.ToHashSet();
            rows = allRows.Where(r => allowed.Contains(r.CandidateState)).ToList();
        }

        warnings.AddRange(BillingDashboardWarningBuilder.Build(
            probe.SyncStateTablePresent,
            snapshot.HoursOnlyInBasicCount,
            BillingBillTimeline.CountRealBillsUsingLastUpdatedFallback(snapshot.Bills),
            logger));

        var summary = new BillingDashboardSummary(
            ReviewNowCount: allRows.Count(r => r.CandidateState == BillingCandidateState.ReviewNow),
            BillInPreparationCount: allRows.Count(r => r.CandidateState == BillingCandidateState.BillInPreparation),
            SubmittedOpenAmount: null,
            ReceivedThisMonth: snapshot.ReceivedThisMonth,
            ReplicaLastSyncTime: freshness.ReplicaLastSyncTime,
            MonthlySnapshotDate: freshness.MonthlySnapshotDate,
            AccumulatedWorkCount: allRows.Count(r => r.CandidateState == BillingCandidateState.AccumulatedWork),
            CoveredByLatestBillCount: allRows.Count(r => r.CandidateState == BillingCandidateState.CoveredByLatestBill),
            NotUrgentCount: allRows.Count(r => r.CandidateState == BillingCandidateState.NotUrgent));

        return new BillingDashboardResult(
            summary,
            rows,
            freshness,
            warnings,
            probe.Diagnostics,
            decision.Status,
            CandidatesBlocked: false);
    }
}
