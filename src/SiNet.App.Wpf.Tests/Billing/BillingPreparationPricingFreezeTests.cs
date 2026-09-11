using System.IO;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingPreparationPricingFreezeTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly MemoryBillingPreparationTaskPort _tasks = new();
    private readonly BillingPreparationService _service;

    public BillingPreparationPricingFreezeTests()
    {
        _service = new BillingPreparationService(
            _store,
            _components,
            new MemoryBillingPreparationProjectMapper(12),
            _tasks,
            new FixedBillingPreparationActor(new BillingActor(7, "manager")),
            new FrozenUtcTimeProvider(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Saved_selection_has_nullable_freeze_before_approval()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var saved = await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, saved.Status);
        Assert.Null(saved.TaskId);
        Assert.Null(saved.ApprovedAtUtc);
        Assert.Null(saved.PricingFrozenAtUtc);
        Assert.Null(saved.PricingTotal);
        Assert.Null(saved.Stages[0].PricingCalculatedAmount);
        Assert.Null(saved.Hours[0].PricingCalculatedAmount);
    }

    [Fact]
    public async Task Complete_fixed_and_hour_selection_freezes_exact_totals()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        var request = approved.Request;
        Assert.Equal(BillingPreparationStatus.TaskOpen, request.Status);
        Assert.Equal(110_000m, request.Stages[0].PricingCalculatedAmount);
        Assert.Equal(2_200_000m, request.Stages[0].PricingBaseAmount);
        Assert.Equal(0m, request.Stages[0].PricingDiscountFraction);
        Assert.Null(request.Stages[0].PricingUnavailableReason);
        Assert.Equal(280m, request.Hours[0].PricingHourlyRate);
        Assert.Equal(280m, request.Hours[0].PricingCalculatedAmount);
        Assert.Equal(1m, request.Hours[0].TotalHours);
        Assert.Equal(57875, request.Hours[0].Reports[0].HoursReportId);
        Assert.Equal(110_000m, request.PricingStageTotal);
        Assert.Equal(280m, request.PricingHoursTotal);
        Assert.Equal(110_280m, request.PricingTotal);
        Assert.False(request.PricingIsPartial);
        Assert.Equal(BillingPreparationPricing.FormulaVersion, request.PricingFormulaVersion);
        Assert.True(BillingPreparationPricingFreeze.HasFreeze(request));
        Assert.False(BillingPreparationPricingFreeze.HasIncompleteFreeze(request));
        Assert.Equal(request.PricingStageTotal + request.PricingHoursTotal, request.PricingTotal);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), request.PricingSourceSnapshotUtc);
        Assert.Equal(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc), request.PricingFrozenAtUtc);
        Assert.Contains("סה\"כ להכנת חשבון: ₪ 110,280", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("שלבי תשלום: ₪ 110,000", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("שעות: ₪ 280", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("משקל השלב בתת החוזה: 25%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("חויב בזמן האישור: 10%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("להוסיף בחשבון הזה: 20%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("לאחר החשבון: 30%", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("תוספת כספית: ₪ 110,000", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("תעריף: ₪ 280", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("סכום שעות: ₪ 280", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("תקופה: 02/06/2026–02/06/2026", _tasks.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stage_freeze_uses_persisted_weight_not_catalog_weight()
    {
        _components.Load = CompleteCatalog() with
        {
            Stages =
            [
                StageDraft(7390, 3853, "פיקוח עליון על הביצוע", billable: 2_200_000m, weight: 0.99m)
            ]
        };
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], []);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(110_000m, approved.Request.PricingTotal);
        Assert.Equal(0.25m, approved.Request.Stages[0].StageWeightWithinSubContract);
    }

    [Fact]
    public async Task Hourly_amount_uses_persisted_total_hours_times_frozen_rate()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var hours = Hours14317() with { TotalHours = 3m };
        await _service.SaveSelectionAsync(ensured.Request.Id, [], [hours]);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(840m, approved.Request.Hours[0].PricingCalculatedAmount);
        Assert.Equal(57875, approved.Request.Hours[0].Reports[0].HoursReportId);
        Assert.Contains("סכום שעות: ₪ 840", _tasks.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_stage_pricing_blocks_approval_before_task()
    {
        _components.Load = CompleteCatalog() with
        {
            Stages = [StageDraft(7390, 3853, "פיקוח עליון על הביצוע", billable: null, weight: 0.25m)]
        };
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], []);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Contains("לא ניתן לאשר", ex.Message, StringComparison.Ordinal);
        Assert.Contains("לא נמצא בסיס תמחור", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.NotNull(stored);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, stored.Status);
        Assert.Null(stored.TaskId);
        Assert.Null(stored.ApprovedAtUtc);
        Assert.True(stored.PricingIsPartial);
        Assert.Null(stored.Stages[0].PricingCalculatedAmount);
        Assert.NotEqual(0m, stored.Stages[0].PricingCalculatedAmount ?? -1m);
    }

    [Fact]
    public async Task Ambiguous_hourly_pricing_blocks_approval()
    {
        _components.Load = CompleteCatalog() with
        {
            HourlySubContracts =
            [
                new BillingHourlySubContractDraft(
                    14317, "פיקוח עליון", MasterPlanSnapshotFeeTypeIds.WorkingHours,
                    AmountUnavailableReason: "תעריף לא ניתן לקביעה")
            ]
        };
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [], [Hours14317()]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Contains("תעריף לא ניתן לקביעה", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Null(stored!.Hours[0].PricingCalculatedAmount);
        Assert.True(stored.PricingIsPartial);
    }

    [Fact]
    public async Task Manual_override_can_freeze_partial_pricing_without_manufacturing_a_total()
    {
        _components.Load = CompleteCatalog() with
        {
            Stages = [StageDraft(7390, 3853, "פיקוח עליון על הביצוע", billable: null, weight: 0.25m)]
        };
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        await _service.ApplyManualOverrideAsync(ensured.Request.Id, "חסר בסיס לשלב");
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(BillingPreparationStatus.TaskOpen, approved.Request.Status);
        Assert.True(approved.Request.PricingIsPartial);
        Assert.Null(approved.Request.Stages[0].PricingCalculatedAmount);
        Assert.Equal(280m, approved.Request.PricingHoursTotal);
        Assert.Equal(280m, approved.Request.PricingTotal);
        Assert.Contains("סכום מחושב חלקית", _tasks.LastBody, StringComparison.Ordinal);
        Assert.Contains("לא נמצא בסיס תמחור", _tasks.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("סה\"כ להכנת חשבון:", _tasks.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_catalog_after_freeze_does_not_change_task_money()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.ThrowOnCreate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        var frozen = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Equal(110_280m, frozen!.PricingTotal);
        _components.Load = CompleteCatalog() with
        {
            Stages = [StageDraft(7390, 3853, "פיקוח עליון על הביצוע", billable: 9_999_999m, weight: 0.25m)],
            HourlySubContracts =
            [
                new BillingHourlySubContractDraft(
                    14317, "פיקוח עליון", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 999m)
            ]
        };
        _tasks.ThrowOnCreate = false;
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(110_280m, approved.Request.PricingTotal);
        Assert.Equal(280m, approved.Request.Hours[0].PricingHourlyRate);
        Assert.Contains("סה\"כ להכנת חשבון: ₪ 110,280", _tasks.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("₪ 999", _tasks.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_create_failure_keeps_ready_for_approval_and_freeze()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.ThrowOnCreate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, stored!.Status);
        Assert.Null(stored.TaskId);
        Assert.Null(stored.ApprovedAtUtc);
        Assert.Null(stored.ApprovedByUserId);
        Assert.Equal(110_280m, stored.PricingTotal);
        Assert.Equal(0, _tasks.CreateCalls);
    }

    [Fact]
    public async Task Save_after_failed_approval_clears_freeze_and_next_approval_recaptures()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.ThrowOnCreate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        var firstFrozenAt = (await _store.GetByIdAsync(ensured.Request.Id))!.PricingFrozenAtUtc;
        Assert.NotNull(firstFrozenAt);

        var saved = await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        Assert.Null(saved.PricingFrozenAtUtc);
        Assert.Null(saved.PricingTotal);
        Assert.Null(saved.Stages[0].PricingCalculatedAmount);
        Assert.Null(saved.Hours[0].PricingCalculatedAmount);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, saved.Status);

        _components.Load = CompleteCatalog() with
        {
            HourlySubContracts =
            [
                new BillingHourlySubContractDraft(
                    14317, "פיקוח עליון", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 300m)
            ]
        };
        _tasks.ThrowOnCreate = false;
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(300m, approved.Request.Hours[0].PricingHourlyRate);
        Assert.Equal(110_300m, approved.Request.PricingTotal);
        Assert.Contains("סה\"כ להכנת חשבון: ₪ 110,300", _tasks.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Freeze_and_task_money_do_not_use_contract_value_or_hour_cost()
    {
        var root = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "SiNet.Application", "Billing", "BillingPreparationAmountCalculator.cs"),
            Path.Combine(root, "src", "SiNet.Application", "Billing", "BillingPreparationPricingFreeze.cs"),
            Path.Combine(root, "src", "SiNet.Application", "Billing", "BillingPreparationTaskInstructions.cs")
        };
        foreach (var file in files)
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotContain("ContractValue", src, StringComparison.Ordinal);
            Assert.DoesNotContain("HourCost", src, StringComparison.Ordinal);
        }

        var draft = StageDraft(7390, 3853, "פיקוח", billable: 2_200_000m, weight: 0.25m);
        var priced = BillingPreparationAmountCalculator.StageAddition(draft, 0.20m);
        Assert.NotEqual(5_000_000m * 0.20m, priced.Amount);
        Assert.Equal(110_000m, priced.Amount);
    }

    [Fact]
    public void Task_instructions_use_frozen_values_even_if_a_later_catalog_is_passed()
    {
        var frozen = EmptyRequest() with
        {
            PricingStageTotal = 110_000m,
            PricingHoursTotal = 280m,
            PricingTotal = 110_280m,
            PricingIsPartial = false,
            PricingFrozenAtUtc = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc),
            PricingSourceSnapshotUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            PricingFormulaVersion = BillingPreparationPricing.FormulaVersion,
            Stages = [Stage7390() with { PricingCalculatedAmount = 110_000m, PricingBaseAmount = 2_200_000m }],
            Hours = [Hours14317() with { PricingHourlyRate = 280m, PricingCalculatedAmount = 280m }]
        };
        var laterCatalog = new[]
        {
            StageDraft(7390, 3853, "פיקוח עליון על הביצוע", billable: 9_999_999m, weight: 0.25m)
        };
        var laterHours = new[]
        {
            new BillingHourlySubContractDraft(
                14317, "פיקוח עליון", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 999m)
        };
        var body = BillingPreparationTaskInstructions.Build(frozen, laterCatalog, laterHours);
        Assert.Contains("סה\"כ להכנת חשבון: ₪ 110,280", body, StringComparison.Ordinal);
        Assert.Contains("תוספת כספית: ₪ 110,000", body, StringComparison.Ordinal);
        Assert.Contains("תעריף: ₪ 280", body, StringComparison.Ordinal);
        Assert.DoesNotContain("₪ 999", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitingForSelection_with_selected_lines_cannot_be_approved()
    {
        _components.Load = CompleteCatalog();
        var planted = await _store.InsertAsync(PlantedRequest(BillingPreparationStatus.WaitingForSelection));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(planted.Id));
        Assert.Equal(BillingPreparationPricingFreeze.NotReadyForApprovalMessage, ex.Message);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(planted.Id);
        Assert.Equal(BillingPreparationStatus.WaitingForSelection, stored!.Status);
        Assert.Null(stored.PricingFrozenAtUtc);
        Assert.Null(stored.TaskId);
        Assert.False(BillingPreparationPricingFreeze.HasFreeze(stored));
    }

    [Theory]
    [InlineData(BillingPreparationStatus.WaitingForSnapshot)]
    [InlineData(BillingPreparationStatus.TaskOpen)]
    [InlineData(BillingPreparationStatus.AwaitingMasterPlanConfirmation)]
    [InlineData(BillingPreparationStatus.Completed)]
    [InlineData(BillingPreparationStatus.Cancelled)]
    public async Task Non_ready_status_cannot_capture_pricing_or_create_task(BillingPreparationStatus status)
    {
        _components.Load = CompleteCatalog();
        var planted = await _store.InsertAsync(PlantedRequest(status));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(planted.Id));
        Assert.Equal(BillingPreparationPricingFreeze.NotReadyForApprovalMessage, ex.Message);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(planted.Id);
        Assert.Equal(status, stored!.Status);
        Assert.Null(stored.PricingFrozenAtUtc);
        Assert.Null(stored.TaskId);
    }

    [Fact]
    public async Task Existing_task_id_is_idempotent_without_recapture()
    {
        _components.Load = CompleteCatalog();
        var planted = await _store.InsertAsync(PlantedRequest(BillingPreparationStatus.TaskOpen) with { TaskId = 77 });
        var approved = await _service.ApproveAndCreateTaskAsync(planted.Id);
        Assert.Equal(77, approved.TaskId);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(planted.Id);
        Assert.Null(stored!.PricingFrozenAtUtc);
        Assert.Equal(77, stored.TaskId);
    }

    [Fact]
    public async Task New_freeze_uses_catalog_latest_backup_utc()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), approved.Request.PricingSourceSnapshotUtc);
        Assert.True(BillingPreparationPricingFreeze.HasFreeze(approved.Request));
    }

    [Fact]
    public async Task New_freeze_falls_back_to_request_snapshot_timestamp()
    {
        _components.Load = CompleteCatalog() with { LatestBackupUtc = null };
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var saved = await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        var stamped = saved with { SnapshotTimestampUtc = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc) };
        await _store.UpdateAsync(stamped);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), approved.Request.PricingSourceSnapshotUtc);
        Assert.Equal(BillingPreparationStatus.TaskOpen, approved.Request.Status);
    }

    [Fact]
    public async Task New_freeze_blocks_when_source_snapshot_is_unknown()
    {
        _components.Load = CompleteCatalog() with { LatestBackupUtc = null };
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var saved = await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        Assert.Null(saved.SnapshotTimestampUtc);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Equal(BillingPreparationPricingFreeze.MissingSourceSnapshotMessage, ex.Message);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, stored!.Status);
        Assert.Null(stored.PricingFrozenAtUtc);
        Assert.Null(stored.PricingSourceSnapshotUtc);
        Assert.Null(stored.TaskId);
        Assert.False(BillingPreparationPricingFreeze.HasFreeze(stored));
    }

    [Fact]
    public async Task Valid_freeze_retry_does_not_require_a_later_catalog_snapshot()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.ThrowOnCreate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        var frozen = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.NotNull(frozen);
        Assert.True(BillingPreparationPricingFreeze.HasFreeze(frozen));
        await _store.UpdateAsync(frozen with { SnapshotTimestampUtc = null, LatestMasterPlanBackupUtc = null });
        _components.Load = CompleteCatalog() with { LatestBackupUtc = null };
        _tasks.ThrowOnCreate = false;
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(110_280m, approved.Request.PricingTotal);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), approved.Request.PricingSourceSnapshotUtc);
        Assert.Equal(1, _tasks.CreateCalls);
    }

    [Fact]
    public async Task Incomplete_freeze_fails_safely_without_repair()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var saved = await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        var corrupt = saved with { PricingFrozenAtUtc = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc) };
        await _store.UpdateAsync(corrupt);
        Assert.True(BillingPreparationPricingFreeze.HasIncompleteFreeze(corrupt));
        Assert.False(BillingPreparationPricingFreeze.HasFreeze(corrupt));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Equal(BillingPreparationPricingFreeze.IncompleteFreezeMessage, ex.Message);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Null(stored!.PricingSourceSnapshotUtc);
        Assert.Null(stored.PricingTotal);
        Assert.Null(stored.TaskId);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, stored.Status);
    }

    [Fact]
    public void Request_identity_marker_is_digit_bounded()
    {
        Assert.True(BillingPreparationTaskInstructions.BodyIdentifiesRequest(
            "מקור החלטה: Billing Preparation Request #2", 2));
        Assert.False(BillingPreparationTaskInstructions.BodyIdentifiesRequest(
            "מקור החלטה: Billing Preparation Request #21", 2));
        Assert.False(BillingPreparationTaskInstructions.BodyIdentifiesRequest(
            "מקור החלטה: Billing Preparation Request #12", 2));
        Assert.True(BillingPreparationTaskInstructions.BodyIdentifiesRequest(
            "מקור החלטה: Billing Preparation Request #2\nאושר על ידי manager", 2));
        Assert.False(BillingPreparationTaskInstructions.BodyIdentifiesRequest(
            "מקור החלטה: Billing Preparation Request #99", 2));
    }

    [Fact]
    public async Task Task_link_persist_failure_leaves_one_task_and_retry_recovers_it()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.NextTaskId = 777;
        _store.ThrowOnTaskLinkPersist = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Equal("FINAL request persistence failed.", ex.Message);

        var afterFailure = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Equal(1, _tasks.CreateCalls);
        Assert.Equal(777, _tasks.LastTaskId);
        Assert.Single(_tasks.Tasks);
        Assert.True(_tasks.Tasks[0].IsOpen);
        Assert.True(BillingPreparationTaskInstructions.BodyIdentifiesRequest(
            _tasks.LastBody, ensured.Request.Id));
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, afterFailure!.Status);
        Assert.Null(afterFailure.TaskId);
        Assert.Null(afterFailure.ApprovedAtUtc);
        Assert.Null(afterFailure.ApprovedByUserId);
        Assert.Equal(110_280m, afterFailure.PricingTotal);
        Assert.True(BillingPreparationPricingFreeze.HasFreeze(afterFailure));

        _store.ThrowOnTaskLinkPersist = false;
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(1, _tasks.CreateCalls);
        Assert.Equal(777, approved.TaskId);
        Assert.Equal(777, approved.Request.TaskId);
        Assert.Equal(BillingPreparationStatus.TaskOpen, approved.Request.Status);
        Assert.Equal(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc), approved.Request.ApprovedAtUtc);
        Assert.Equal(7, approved.Request.ApprovedByUserId);
        Assert.Equal("manager", approved.Request.ApprovedByLogin);
        Assert.Equal(110_280m, approved.Request.PricingTotal);
        Assert.Equal(110_000m, approved.Request.PricingStageTotal);
        Assert.Equal(280m, approved.Request.PricingHoursTotal);
        Assert.Same(_tasks.LastBody, _tasks.Tasks[0].Body);
    }

    [Fact]
    public async Task Unrelated_open_PrepareBill_is_not_adopted_and_still_blocks()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.Seed(
            500,
            12,
            "מקור החלטה: Billing Preparation Request #99\nסה\"כ להכנת חשבון: ₪ 1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Equal("כבר קיימת משימת הכנת חשבון פתוחה לפרויקט.", ex.Message);
        Assert.Equal(0, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Null(stored!.TaskId);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, stored.Status);
        Assert.Null(stored.ApprovedAtUtc);
    }

    [Fact]
    public async Task Matching_marker_for_another_request_is_not_adopted()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.Seed(
            501,
            12,
            "מקור החלטה: " + BillingPreparationTaskInstructions.RequestIdentityMarker(ensured.Request.Id * 10 + 1));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Equal("כבר קיימת משימת הכנת חשבון פתוחה לפרויקט.", ex.Message);
        Assert.Equal(0, _tasks.CreateCalls);
        Assert.Null((await _store.GetByIdAsync(ensured.Request.Id))!.TaskId);
    }

    [Fact]
    public async Task Closed_matching_task_is_not_adopted()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.Seed(
            321,
            12,
            "מקור החלטה: " + BillingPreparationTaskInstructions.RequestIdentityMarker(ensured.Request.Id),
            isOpen: false);
        _tasks.OpenTaskExists = false;
        _tasks.NextTaskId = 778;
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(1, _tasks.CreateCalls);
        Assert.Equal(778, approved.TaskId);
        Assert.Equal(BillingPreparationStatus.TaskOpen, approved.Request.Status);
        Assert.Equal(2, _tasks.Tasks.Count);
    }

    [Fact]
    public async Task Corrupt_freeze_with_matching_open_task_fails_safely()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        _tasks.NextTaskId = 777;
        _store.ThrowOnTaskLinkPersist = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        _store.ThrowOnTaskLinkPersist = false;

        var frozen = await _store.GetByIdAsync(ensured.Request.Id);
        var corrupt = frozen! with { PricingFrozenAtUtc = frozen.PricingFrozenAtUtc, PricingTotal = null };
        await _store.UpdateAsync(corrupt);
        Assert.True(BillingPreparationPricingFreeze.HasIncompleteFreeze(corrupt));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApproveAndCreateTaskAsync(ensured.Request.Id));
        Assert.Equal(BillingPreparationPricingFreeze.IncompleteFreezeMessage, ex.Message);
        Assert.Equal(1, _tasks.CreateCalls);
        var stored = await _store.GetByIdAsync(ensured.Request.Id);
        Assert.Null(stored!.TaskId);
        Assert.Null(stored.ApprovedAtUtc);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, stored.Status);
        Assert.Equal(777, _tasks.LastTaskId);
    }

    [Fact]
    public async Task Ordinary_successful_approval_still_creates_exactly_one_task()
    {
        _components.Load = CompleteCatalog();
        var ensured = await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(ensured.Request.Id, [Stage7390()], [Hours14317()]);
        var approved = await _service.ApproveAndCreateTaskAsync(ensured.Request.Id);
        Assert.Equal(1, _tasks.CreateCalls);
        Assert.Equal(approved.TaskId, approved.Request.TaskId);
        Assert.Equal(BillingPreparationStatus.TaskOpen, approved.Request.Status);
        Assert.Equal(110_280m, approved.Request.PricingTotal);
        Assert.Contains(
            BillingPreparationTaskInstructions.RequestIdentityMarker(ensured.Request.Id),
            _tasks.LastBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Task_link_recovery_uses_existing_body_marker_without_schema_change()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiNet.Application", "Billing", "BillingPreparationService.cs"));
        var port = File.ReadAllText(Path.Combine(root, "src", "SiNet.Infrastructure.Sql", "Services", "Billing", "SqlBillingPreparationTaskPort.cs"));
        Assert.Contains("FindOpenPrepareBillTaskForRequestAsync", service, StringComparison.Ordinal);
        Assert.Contains("FindOpenPrepareBillTaskForRequestAsync", port, StringComparison.Ordinal);
        Assert.Contains("t.Body", port, StringComparison.Ordinal);
        Assert.Contains("AssignmentStatus.IsOpen", port, StringComparison.Ordinal);
        Assert.Contains("TaskTypeCodes.PrepareBill", port, StringComparison.Ordinal);
        Assert.DoesNotContain("Add-Migration", service, StringComparison.Ordinal);
    }

    [Fact]
    public void HasFreeze_requires_coherent_metadata_not_only_frozen_at()
    {
        var frozenAt = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        var source = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(BillingPreparationPricingFreeze.HasFreeze(EmptyRequest() with { PricingFrozenAtUtc = frozenAt }));
        var coherent = EmptyRequest() with
        {
            PricingStageTotal = 110_000m,
            PricingHoursTotal = 280m,
            PricingTotal = 110_280m,
            PricingIsPartial = false,
            PricingFrozenAtUtc = frozenAt,
            PricingSourceSnapshotUtc = source,
            PricingFormulaVersion = BillingPreparationPricing.FormulaVersion,
            Stages = [Stage7390() with { PricingCalculatedAmount = 110_000m }],
            Hours = [Hours14317() with { PricingCalculatedAmount = 280m }]
        };
        Assert.True(BillingPreparationPricingFreeze.HasFreeze(coherent));
        Assert.False(BillingPreparationPricingFreeze.HasFreeze(coherent with { PricingTotal = 1m }));
        Assert.False(BillingPreparationPricingFreeze.HasFreeze(
            coherent with { Stages = [Stage7390() with { PricingCalculatedAmount = 0m, PricingUnavailableReason = "לא נמצא בסיס תמחור" }] }));
    }

    private static BillingPreparationSnapshotLoad CompleteCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [StageDraft(7390, 3853, "פיקוח עליון על הביצוע", billable: 2_200_000m, weight: 0.25m)],
            [
                new BillingHourlySubContractDraft(
                    14317, "פיקוח עליון", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 280m)
            ],
            [
                new BillingHourReportFact(57875, 4608, 14317, 7390, new DateTime(2026, 6, 2), 1, "A", 1m, "x")
            ]);

    private static BillingPreparationStageDraft StageDraft(
        int stageId,
        int subId,
        string name,
        decimal? billable,
        decimal weight) =>
        new(
            stageId,
            subId,
            name,
            "תכנון פיזי",
            weight,
            BillingStageProgressCalculator.Observe([0.10m]),
            0.10m,
            Included: false,
            MasterPlanSnapshotFeeTypeIds.FixedPrice,
            SubContractBillableAmount: billable);

    private static BillingPreparationStageLineSnapshot Stage7390() =>
        new(
            7390, 3853, "פיקוח עליון על הביצוע", "תכנון פיזי", 0.25m,
            0.10m, 0.30m, 0.20m, false,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);

    private static BillingPreparationHoursLineSnapshot Hours14317() =>
        new(
            14317, "פיקוח עליון",
            new DateTime(2026, 6, 2), new DateTime(2026, 6, 2),
            1, 1m,
            [new BillingPreparationHourReportSnapshot(57875, new DateTime(2026, 6, 2), 1, "A", 1m, 14317, 7390, "x")],
            [],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);

    private static BillingPreparationRequestRecord PlantedRequest(BillingPreparationStatus status) =>
        EmptyRequest() with
        {
            Id = 0,
            Status = status,
            SiNetProjectId = 12,
            SnapshotTimestampUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Stages = [Stage7390()],
            Hours = [Hours14317()]
        };

    private static BillingPreparationRequestRecord EmptyRequest() =>
        new(
            2, 4608, 1844, "1844", "פרויקט", "לקוח",
            BillingPreparationStatus.ReadyForApproval,
            new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc),
            7, "manager",
            null, null, null, null, null, null,
            false, null, null, null,
            [],
            []);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
