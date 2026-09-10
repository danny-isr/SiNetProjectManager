using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingStageProgressCalculatorTests
{
    [Fact]
    public void Observed_is_max_not_sum()
    {
        var observed = BillingStageProgressCalculator.Observe([0.40m, 0.40m, 0.70m, 1.00m, 1.00m]);
        Assert.Equal(1.00m, observed.Value);
        Assert.False(observed.HasOutliers);
    }

    [Fact]
    public void Half_and_one_map_to_percent_scale()
    {
        var observed = BillingStageProgressCalculator.Observe([0.50m]);
        Assert.Equal(0.50m, observed.Value);
        Assert.Equal(50m, observed.Value * 100m);
        Assert.True(BillingStageProgressCalculator.IsValidScale(1.00m));
    }

    [Fact]
    public void Outliers_are_flagged_and_excluded_from_max()
    {
        var observed = BillingStageProgressCalculator.Observe([0.40m, 1.068m, -0.000001m]);
        Assert.Equal(0.40m, observed.Value);
        Assert.True(observed.HasOutliers);
        Assert.False(observed.CanUseForAutomaticCalculation);
    }

    [Fact]
    public void Target_cannot_be_below_observed()
    {
        var observed = BillingStageProgressCalculator.Observe([0.40m]);
        var validation = BillingStageProgressCalculator.ValidateTarget(observed, 0.30m);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Target_cannot_exceed_one()
    {
        var observed = BillingStageProgressCalculator.Observe([0.40m]);
        var validation = BillingStageProgressCalculator.ValidateTarget(observed, 1.10m);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Delta_is_target_minus_observed()
    {
        var observed = BillingStageProgressCalculator.Observe([0.40m]);
        var validation = BillingStageProgressCalculator.ValidateTarget(observed, 0.70m);
        Assert.True(validation.IsValid);
        Assert.Equal(0.30m, validation.Delta);
    }

    [Fact]
    public void Addition_20_on_observed_10_yields_target_30()
    {
        var observed = BillingStageProgressCalculator.Observe([0.10m]);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, 0.20m);
        Assert.True(validation.IsValid);
        Assert.Equal(0.30m, validation.Target);
        Assert.Equal(0.20m, validation.Delta);
    }

    [Fact]
    public void Addition_90_on_observed_10_yields_target_100()
    {
        var observed = BillingStageProgressCalculator.Observe([0.10m]);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, 0.90m);
        Assert.True(validation.IsValid);
        Assert.Equal(1.00m, validation.Target);
    }

    [Fact]
    public void Addition_91_on_observed_10_is_rejected()
    {
        var observed = BillingStageProgressCalculator.Observe([0.10m]);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, 0.91m);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Negative_addition_is_rejected()
    {
        var observed = BillingStageProgressCalculator.Observe([0.10m]);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, -0.01m);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Unknown_observed_without_outliers_starts_at_zero()
    {
        var observed = BillingStageProgressCalculator.Observe([]);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, 0.20m);
        Assert.True(validation.IsValid);
        Assert.Equal(0.20m, validation.Target);
        Assert.Equal(0.20m, validation.Delta);
    }

    [Fact]
    public void Outlier_observed_is_not_treated_as_zero_for_addition()
    {
        var observed = BillingStageProgressCalculator.Observe([1.06m]);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, 0.20m);
        Assert.False(validation.IsValid);
        Assert.True(validation.HasDataQualityFlag);
    }

    [Fact]
    public void Outlier_row_does_not_silently_validate()
    {
        var observed = BillingStageProgressCalculator.Observe([1.06m]);
        var validation = BillingStageProgressCalculator.ValidateTarget(observed, 1.00m);
        Assert.False(validation.IsValid);
        Assert.True(validation.HasDataQualityFlag);
    }
}

public sealed class BillingHourlyScopeResolverTests
{
    [Fact]
    public void Date_range_resolves_concrete_hour_ids()
    {
        var reports = new BillingHourReportFact[]
        {
            new(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x"),
            new(12, 5905, 100, 1, new DateTime(2026, 8, 15), 1, "A", 3m, "y"),
            new(13, 5905, 100, 1, new DateTime(2026, 9, 1), 1, "A", 1m, "z"),
            new(14, 5905, 200, 1, new DateTime(2026, 8, 10), 1, "B", 4m, "other")
        };

        var resolved = BillingHourlyScopeResolver.Resolve(
            reports, 100, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));

        Assert.Equal(2, resolved.Count);
        Assert.Equal([11, 12], resolved.Select(r => r.HoursReportId).ToArray());
        Assert.Equal(5.5m, resolved.Sum(r => r.Hours));
    }
}

public sealed class BillingPreparationWorkflowServiceTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly MemoryBillingPreparationTaskPort _tasks = new();
    private readonly BillingPreparationService _service;

    public BillingPreparationWorkflowServiceTests()
    {
        _service = new BillingPreparationService(
            _store,
            _components,
            new MemoryBillingPreparationProjectMapper(12),
            _tasks,
            new FixedBillingPreparationActor(new BillingActor(7, "manager")),
            new FrozenUtcTimeProvider(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task PrepareBill_creates_request_but_never_task()
    {
        var result = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        Assert.True(result.Created);
        Assert.Null(result.Request.TaskId);
        Assert.Equal(0, _tasks.CreateCalls);
        Assert.Equal(BillingPreparationStatus.WaitingForSnapshot, result.Request.Status);
    }

    [Fact]
    public async Task Repeated_PrepareBill_is_idempotent()
    {
        var first = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var second = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        Assert.False(second.Created);
        Assert.Equal(first.Request.Id, second.Request.Id);
        Assert.Equal(0, _tasks.CreateCalls);
    }

    [Fact]
    public async Task Newer_snapshot_promotes_waiting_for_snapshot()
    {
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        _components.Load = SnapshotAvailable();
        var refreshed = await _service.RefreshFromSnapshotAsync(1);
        Assert.Equal(BillingPreparationStatus.WaitingForSelection, refreshed.Status);
    }

    [Fact]
    public async Task Mixed_stage_and_hour_request_can_be_saved()
    {
        _components.Load = SnapshotAvailable();
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var saved = await _service.SaveSelectionAsync(
            ensured.Request.Id,
            [StageLine(0.40m, 0.70m)],
            [HoursLine()]);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, saved.Status);
        Assert.Single(saved.Stages);
        Assert.Single(saved.Hours);
        Assert.Equal(0.30m, saved.Stages[0].RequestedDelta);
    }

    [Fact]
    public async Task Manual_override_is_audited()
    {
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var updated = await _service.ApplyManualOverrideAsync(ensured.Request.Id, "אין שלבים בגיבוי");
        Assert.True(updated.ManualOverride);
        Assert.Equal("אין שלבים בגיבוי", updated.ManualOverrideReason);
        Assert.Equal(7, updated.ManualOverrideByUserId);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, updated.Status);
    }

    [Fact]
    public async Task Approval_creates_one_PrepareBill_task_with_cumulative_instructions()
    {
        _components.Load = SnapshotAvailable();
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [StageLine(0.25m, 0.50m)], []);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(1, _tasks.CreateCalls);
        Assert.Equal(approved.TaskId, approved.Request.TaskId);
        Assert.Equal(BillingPreparationStatus.TaskOpen, approved.Request.Status);
        Assert.Contains("להוסיף בחשבון הזה: 25%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("לאחר החשבון: 50%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("מצב ב-MasterPlan בזמן האישור: 25%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("להגיש עד 50% מצטבר", _tasks.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("לא מחויבות", _tasks.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("already billed", _tasks.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_open_PrepareBill_is_prevented()
    {
        _components.Load = SnapshotAvailable();
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [StageLine(0.25m, 0.50m)], []);
        _tasks.OpenTaskExists = true;
        _tasks.CreateCalls = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
    }

    [Fact]
    public async Task Task_completion_moves_to_awaiting_confirmation_not_completed()
    {
        _components.Load = SnapshotAvailable();
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [StageLine(0.25m, 0.50m)], [HoursLine()]);
        await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        var after = await _service.OnPrepareBillTaskCompletedAsync(ensured.Request.Id);
        Assert.Equal(BillingPreparationStatus.AwaitingMasterPlanConfirmation, after.Status);
        Assert.All(after.Hours, h => Assert.Equal(BillingConfirmationMode.None, h.ConfirmationMode));
    }

    [Fact]
    public async Task Hourly_completion_requires_explicit_manager_confirmation()
    {
        _components.Load = SnapshotAvailable();
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [], [HoursLine()]);
        await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        await _service.OnPrepareBillTaskCompletedAsync(ensured.Request.Id);
        var confirmed = await _service.ConfirmHourlyManuallyAsync(ensured.Request.Id, "בוצע ב-MP");
        Assert.Equal(BillingConfirmationMode.Manual, confirmed.Hours[0].ConfirmationMode);
        Assert.Equal(BillingPreparationStatus.Completed, confirmed.Status);
    }

    [Fact]
    public async Task Overlapping_hour_ids_are_detectable_as_SiNet_preparation_warning()
    {
        _components.Load = SnapshotAvailable();
        var first = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(first.Request.Id, [], [HoursLine()]);
        var hits = await _store.FindHourReportIdsInOtherRequestsAsync([11, 99], excludeRequestId: 2);
        Assert.Contains(11, hits);
        Assert.DoesNotContain(99, hits);
    }

    [Fact]
    public async Task Old_snapshot_does_not_auto_confirm_stage()
    {
        _components.Load = SnapshotAvailable(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var ensured = await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [StageLine(0.25m, 0.50m)], []);
        await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        var after = await _service.OnPrepareBillTaskCompletedAsync(ensured.Request.Id);
        Assert.Equal(BillingPreparationStatus.AwaitingMasterPlanConfirmation, after.Status);
        Assert.Equal(BillingConfirmationMode.None, after.Stages[0].ConfirmationMode);
    }

    private static BillingPreparationSnapshotLoad SnapshotAvailable(DateTime? backup = null) =>
        new(
            true,
            backup ?? new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "2608",
            "פרויקט",
            [new BillingPreparationStageDraft(
                12791, 9794, "תכנון מפורט", "כבישים", 0.20m,
                BillingStageProgressCalculator.Observe([0.40m]),
                0.40m,
                Included: false)],
            [new BillingHourlySubContractDraft(100, "תנועה", 4)],
            []);

    private static BillingPreparationStageLineSnapshot StageLine(decimal observed, decimal target) =>
        new(
            12791, 9794, "תכנון מפורט", "כבישים", 0.20m,
            observed, target, target - observed,
            HasDataQualityFlag: false,
            SnapshotTimestampUtc: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);

    private static BillingPreparationHoursLineSnapshot HoursLine() =>
        new(
            100, "תנועה",
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31),
            1, 2.5m,
            [new BillingPreparationHourReportSnapshot(11, new DateTime(2026, 8, 1), 1, "A", 2.5m, 100, 1, "x")],
            [],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);
}

internal sealed class FrozenUtcTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
