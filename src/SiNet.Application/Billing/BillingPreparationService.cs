namespace SiNet.Application.Billing;

public sealed record BillingActor(int UserId, string Login);

public interface IBillingPreparationActor
{
    Task<BillingActor> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class BillingPreparationService(
    IBillingPreparationStore store,
    IBillingPreparationComponentSource components,
    IBillingPreparationProjectMapper mapper,
    IBillingPreparationTaskPort tasks,
    IBillingPreparationActor actor,
    TimeProvider? timeProvider = null) : IBillingPreparationService
{
    private readonly IBillingPreparationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IBillingPreparationComponentSource _components =
        components ?? throw new ArgumentNullException(nameof(components));
    private readonly IBillingPreparationProjectMapper _mapper =
        mapper ?? throw new ArgumentNullException(nameof(mapper));
    private readonly IBillingPreparationTaskPort _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
    private readonly IBillingPreparationActor _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<BillingPreparationEnsureResult> EnsureFromPrepareBillAsync(
        int masterPlanProjectId,
        string? projectNumber,
        string? projectName,
        string? customerName,
        CancellationToken cancellationToken = default)
    {
        if (masterPlanProjectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(masterPlanProjectId));

        var existing = await _store.GetActiveByMasterPlanProjectIdAsync(masterPlanProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
            return new BillingPreparationEnsureResult(existing, Created: false);

        var who = await _actor.GetAsync(cancellationToken).ConfigureAwait(false);
        var load = await _components.LoadAsync(masterPlanProjectId, cancellationToken).ConfigureAwait(false);
        var siNetId = await _mapper.TryResolveSiNetProjectIdAsync(
            projectNumber ?? load.ProjectNumber,
            cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;
        var draft = new BillingPreparationRequestRecord(
            Id: 0,
            MasterPlanProjectId: masterPlanProjectId,
            SiNetProjectId: siNetId,
            ProjectNumber: projectNumber ?? load.ProjectNumber,
            ProjectName: projectName ?? load.ProjectName,
            CustomerName: customerName ?? load.CustomerName,
            Status: BillingPreparationWorkflow.InitialStatus(load.SnapshotAvailable),
            CreatedAtUtc: now,
            CreatedByUserId: who.UserId,
            CreatedByLogin: who.Login,
            ApprovedAtUtc: null,
            ApprovedByUserId: null,
            ApprovedByLogin: null,
            SnapshotTimestampUtc: load.LatestBackupUtc,
            LatestMasterPlanBackupUtc: load.LatestBackupUtc,
            TaskId: null,
            ManualOverride: false,
            ManualOverrideReason: null,
            ManualOverrideAtUtc: null,
            ManualOverrideByUserId: null,
            Stages: [],
            Hours: []);
        var saved = await _store.InsertAsync(draft, cancellationToken).ConfigureAwait(false);
        return new BillingPreparationEnsureResult(saved, Created: true);
    }

    public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListForPreparationTabAsync(
        CancellationToken cancellationToken = default) =>
        _store.ListActiveAsync(cancellationToken);

    public async Task<BillingPreparationRequestRecord> RefreshFromSnapshotAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        var load = await _components.LoadAsync(request.MasterPlanProjectId, cancellationToken)
            .ConfigureAwait(false);
        var status = request.Status;
        if (status == BillingPreparationStatus.WaitingForSnapshot && load.SnapshotAvailable)
            status = BillingPreparationStatus.WaitingForSelection;

        var updated = request with
        {
            Status = status,
            SnapshotTimestampUtc = load.LatestBackupUtc ?? request.SnapshotTimestampUtc,
            LatestMasterPlanBackupUtc = load.LatestBackupUtc ?? request.LatestMasterPlanBackupUtc,
            CustomerName = load.CustomerName ?? request.CustomerName,
            ProjectNumber = load.ProjectNumber ?? request.ProjectNumber,
            ProjectName = load.ProjectName ?? request.ProjectName
        };
        updated = await _store.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);

        if (updated.Status == BillingPreparationStatus.AwaitingMasterPlanConfirmation)
            updated = await ReevaluateStageConfirmationAsync(updated.Id, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    public async Task<BillingPreparationRequestRecord> SaveSelectionAsync(
        int requestId,
        IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
        IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(hours);
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (request.Status is BillingPreparationStatus.TaskOpen
            or BillingPreparationStatus.AwaitingMasterPlanConfirmation
            or BillingPreparationStatus.Completed)
        {
            throw new InvalidOperationException("לא ניתן לשנות רכיבים אחרי יצירת משימה.");
        }

        foreach (var stage in stages)
        {
            if (stage.RequestedDelta <= 0m)
                continue;
            var observed = new BillingStageProgressCalculator.ObservedCumulativeProgress(
                stage.ObservedCumulativeProgress,
                stage.HasDataQualityFlag,
                stage.HasDataQualityFlag ? [stage.ObservedCumulativeProgress ?? -1] : []);
            var validation = BillingStageProgressCalculator.ValidateTarget(observed, stage.TargetCumulativeProgress);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Error ?? "בחירת שלב לא תקינה.");
        }

        var persistedStages = stages.Where(s => s.RequestedDelta > 0m).ToList();

        if (hours.Count > 0)
        {
            var load = await _components.LoadAsync(request.MasterPlanProjectId, cancellationToken)
                .ConfigureAwait(false);
            var hourlyIds = load.HourlySubContracts
                .Where(s => s.FeeTypeId == MasterPlanSnapshotFeeTypeIds.WorkingHours)
                .Select(s => s.MasterPlanSubContractId)
                .ToHashSet();
            foreach (var hour in hours)
            {
                if (!hourlyIds.Contains(hour.MasterPlanSubContractId))
                    throw new InvalidOperationException("יש לבחור הסכם משנה שעתי (FeeType=4) מה-snapshot שנטען.");
                if (hour.ToDate.Date < hour.FromDate.Date)
                    throw new InvalidOperationException("עד תאריך חייב להיות באותו יום או אחרי מתאריך.");
                if (hour.Reports.Count == 0)
                    throw new InvalidOperationException("לא ניתן לשמור היקף שעות ללא דיווחים תואמים בטווח שנבחר.");
                if (hour.Reports.GroupBy(r => r.HoursReportId).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("אותו דיווח שעות לא יכול להופיע פעמיים באותו רכיב.");
            }
        }

        var hourIds = hours.SelectMany(h => h.Reports.Select(r => r.HoursReportId)).ToList();
        if (hourIds.Count != hourIds.Distinct().Count())
            throw new InvalidOperationException("אותו דיווח שעות לא יכול להופיע פעמיים באותה בקשת הכנה.");
        var overlapping = hourIds.Count == 0
            ? []
            : await _store.FindHourReportIdsInOtherRequestsAsync(hourIds.Distinct().ToList(), request.Id, cancellationToken)
                .ConfigureAwait(false);
        var overlapSet = overlapping.ToHashSet();
        var hoursWithWarnings = hours
            .Select(h =>
            {
                var hits = h.Reports.Select(r => r.HoursReportId).Where(overlapSet.Contains).Distinct().ToList();
                return hits.Count == 0 ? h : h with { OverlappingHourReportIds = hits };
            })
            .ToList();

        var status = BillingPreparationWorkflow.AfterSelection(persistedStages, hoursWithWarnings, request.ManualOverride);
        var updated = BillingPreparationPricingFreeze.Clear(request with
        {
            Status = status,
            Stages = persistedStages,
            Hours = hoursWithWarnings
        });
        return await _store.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingPreparationRequestRecord> ApplyManualOverrideAsync(
        int requestId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("חובה לציין סיבה לטיפול ידני.", nameof(reason));

        var who = await _actor.GetAsync(cancellationToken).ConfigureAwait(false);
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;
        var updated = request with
        {
            ManualOverride = true,
            ManualOverrideReason = reason.Trim(),
            ManualOverrideAtUtc = now,
            ManualOverrideByUserId = who.UserId,
            Status = BillingPreparationStatus.ReadyForApproval
        };
        return await _store.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingPreparationApproveResult> ApproveAndCreateTaskAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (request.TaskId is int existingTask)
            return new BillingPreparationApproveResult(request, existingTask);

        if (request.Status != BillingPreparationStatus.ReadyForApproval)
            throw new InvalidOperationException(BillingPreparationPricingFreeze.NotReadyForApprovalMessage);

        if (!request.ManualOverride && request.Stages.Count == 0 && request.Hours.Count == 0)
            throw new InvalidOperationException("יש לבחור שלבים, היקף שעות, או לאשר טיפול ידני.");

        if (request.SiNetProjectId is not int siNetProjectId)
            throw new InvalidOperationException("לא נמצא פרויקט SiNet תואם — לא ניתן לפתוח משימה.");

        var matchingOpenTaskId = await _tasks
            .FindOpenPrepareBillTaskForRequestAsync(siNetProjectId, request.Id, cancellationToken)
            .ConfigureAwait(false);
        if (matchingOpenTaskId is int recoveredTaskId)
        {
            if (BillingPreparationPricingFreeze.HasIncompleteFreeze(request)
                || !BillingPreparationPricingFreeze.HasFreeze(request))
            {
                throw new InvalidOperationException(BillingPreparationPricingFreeze.IncompleteFreezeMessage);
            }

            if (request.PricingIsPartial == true && !request.ManualOverride)
                throw new InvalidOperationException(BillingPreparationPricingFreeze.PartialApprovalBlockedMessage(request));

            var recoverWho = await _actor.GetAsync(cancellationToken).ConfigureAwait(false);
            return await FinalizeApprovedRequestAsync(
                    request,
                    recoveredTaskId,
                    recoverWho,
                    request.SnapshotTimestampUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (await _tasks.HasOpenPrepareBillTaskAsync(siNetProjectId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("כבר קיימת משימת הכנת חשבון פתוחה לפרויקט.");

        var now = _time.GetUtcNow().UtcDateTime;
        BillingPreparationRequestRecord frozen;
        if (BillingPreparationPricingFreeze.HasFreeze(request))
        {
            frozen = request;
        }
        else if (BillingPreparationPricingFreeze.HasIncompleteFreeze(request))
        {
            throw new InvalidOperationException(BillingPreparationPricingFreeze.IncompleteFreezeMessage);
        }
        else
        {
            var catalog = await _components.LoadAsync(request.MasterPlanProjectId, cancellationToken)
                .ConfigureAwait(false);
            var sourceSnapshotUtc = BillingPreparationPricingFreeze.ResolveSourceSnapshotUtc(catalog, request);
            if (sourceSnapshotUtc is null)
                throw new InvalidOperationException(BillingPreparationPricingFreeze.MissingSourceSnapshotMessage);

            frozen = BillingPreparationPricingFreeze.Capture(request, catalog, now, sourceSnapshotUtc.Value);
            frozen = await _store.UpdateAsync(frozen, cancellationToken).ConfigureAwait(false);
        }

        if (frozen.PricingIsPartial == true && !frozen.ManualOverride)
            throw new InvalidOperationException(BillingPreparationPricingFreeze.PartialApprovalBlockedMessage(frozen));

        var who = await _actor.GetAsync(cancellationToken).ConfigureAwait(false);
        var title = $"הכנת חשבון — פרויקט {frozen.ProjectNumber ?? frozen.MasterPlanProjectId.ToString()}";
        var body = BillingPreparationTaskInstructions.Build(
            frozen with { ApprovedByLogin = who.Login });
        var taskId = await _tasks.CreatePrepareBillTaskAsync(siNetProjectId, title, body, cancellationToken)
            .ConfigureAwait(false);

        return await FinalizeApprovedRequestAsync(frozen, taskId, who, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<BillingPreparationApproveResult> FinalizeApprovedRequestAsync(
        BillingPreparationRequestRecord frozen,
        int taskId,
        BillingActor who,
        DateTime? snapshotFallbackUtc,
        CancellationToken cancellationToken)
    {
        var approved = frozen with
        {
            Status = BillingPreparationStatus.TaskOpen,
            ApprovedAtUtc = _time.GetUtcNow().UtcDateTime,
            ApprovedByUserId = who.UserId,
            ApprovedByLogin = who.Login,
            SnapshotTimestampUtc = frozen.SnapshotTimestampUtc ?? snapshotFallbackUtc,
            TaskId = taskId
        };
        approved = await _store.UpdateAsync(approved, cancellationToken).ConfigureAwait(false);
        return new BillingPreparationApproveResult(approved, taskId);
    }

    public async Task<BillingPreparationRequestRecord> OnPrepareBillTaskCompletedAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (request.Status == BillingPreparationStatus.Completed)
            return request;

        var updated = request with { Status = BillingPreparationStatus.AwaitingMasterPlanConfirmation };
        updated = await _store.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return await ReevaluateStageConfirmationAsync(updated.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingPreparationRequestRecord> ConfirmHourlyManuallyAsync(
        int requestId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var who = await _actor.GetAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (request.Status != BillingPreparationStatus.AwaitingMasterPlanConfirmation
            && request.Status != BillingPreparationStatus.TaskOpen)
        {
            throw new InvalidOperationException("אישור ידני זמין רק אחרי ביצוע המשימה.");
        }

        var hours = request.Hours
            .Select(h => h with
            {
                ConfirmationMode = BillingConfirmationMode.Manual,
                ConfirmedAtUtc = now,
                ConfirmedByUserId = who.UserId,
                ConfirmationNote = note
            })
            .ToList();

        var updated = request with
        {
            Status = BillingPreparationStatus.AwaitingMasterPlanConfirmation,
            Hours = hours
        };
        if (BillingPreparationWorkflow.AllRequiredComponentsConfirmed(updated))
            updated = updated with { Status = BillingPreparationStatus.Completed };

        return await _store.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingPreparationRequestRecord> ReevaluateStageConfirmationAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (request.Status != BillingPreparationStatus.AwaitingMasterPlanConfirmation)
            return request;
        if (request.ApprovedAtUtc is not DateTime approvedAt)
            return request;

        var load = await _components.LoadAsync(request.MasterPlanProjectId, cancellationToken)
            .ConfigureAwait(false);
        var stages = new List<BillingPreparationStageLineSnapshot>();
        foreach (var stage in request.Stages)
        {
            var current = load.Stages.FirstOrDefault(s => s.MasterPlanStageId == stage.MasterPlanStageId);
            var observed = current?.Observed
                           ?? new BillingStageProgressCalculator.ObservedCumulativeProgress(null, false, []);
            var confirm = BillingStageConfirmationEvaluator.CanAutoConfirm(
                stage,
                observed,
                load.LatestBackupUtc,
                approvedAt);
            stages.Add(confirm
                ? stage with
                {
                    ConfirmationMode = BillingConfirmationMode.NewerSnapshot,
                    ConfirmedAtUtc = _time.GetUtcNow().UtcDateTime
                }
                : stage);
        }

        var updated = request with { Stages = stages };
        if (BillingPreparationWorkflow.AllRequiredComponentsConfirmed(updated))
            updated = updated with { Status = BillingPreparationStatus.Completed };

        return await _store.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task OnPrepareBillTaskCompletedByTaskIdAsync(
        int taskId,
        CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new ArgumentOutOfRangeException(nameof(taskId));

        var request = await _store.GetByTaskIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (request is null)
            return;

        await OnPrepareBillTaskCompletedAsync(request.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BillingPreparationSnapshotLoad> LoadComponentsAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequireAsync(requestId, cancellationToken).ConfigureAwait(false);
        return await _components.LoadAsync(request.MasterPlanProjectId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
        IReadOnlyList<int> hourReportIds,
        int? excludeRequestId,
        CancellationToken cancellationToken = default) =>
        _store.FindHourReportIdsInOtherRequestsAsync(hourReportIds, excludeRequestId, cancellationToken);

    private async Task<BillingPreparationRequestRecord> RequireAsync(int requestId, CancellationToken cancellationToken)
    {
        var request = await _store.GetByIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        return request ?? throw new InvalidOperationException($"בקשת הכנה {requestId} לא נמצאה.");
    }
}
