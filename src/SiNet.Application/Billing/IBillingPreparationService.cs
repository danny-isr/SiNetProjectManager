namespace SiNet.Application.Billing;

public interface IBillingPreparationStore
{
    Task<BillingPreparationRequestRecord?> GetActiveByMasterPlanProjectIdAsync(
        int masterPlanProjectId,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord?> GetByTaskIdAsync(
        int taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingPreparationRequestRecord>> ListActiveAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingPreparationRequestRecord>> ListAwaitingConfirmationAsync(
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> InsertAsync(
        BillingPreparationRequestRecord request,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> UpdateAsync(
        BillingPreparationRequestRecord request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
        IReadOnlyList<int> hourReportIds,
        int? excludeRequestId,
        CancellationToken cancellationToken = default);
}

public interface IBillingPreparationComponentSource
{
    Task<BillingPreparationSnapshotLoad> LoadAsync(
        int masterPlanProjectId,
        CancellationToken cancellationToken = default);
}

public sealed record BillingPreparationSnapshotLoad(
    bool SnapshotAvailable,
    DateTime? LatestBackupUtc,
    string? CustomerName,
    string? ProjectNumber,
    string? ProjectName,
    IReadOnlyList<BillingPreparationStageDraft> Stages,
    IReadOnlyList<BillingHourlySubContractDraft> HourlySubContracts,
    IReadOnlyList<BillingHourReportFact> HourReports);

public sealed record BillingHourlySubContractDraft(
    int MasterPlanSubContractId,
    string Name,
    int FeeTypeId,
    decimal? UniqueHourlyRate = null,
    decimal HourlyDiscountFraction = 0m,
    string? AmountUnavailableReason = null);

public interface IBillingPreparationProjectMapper
{
    Task<int?> TryResolveSiNetProjectIdAsync(
        string? projectNumber,
        CancellationToken cancellationToken = default);
}

public interface IBillingPreparationTaskPort
{
    Task<int> CreatePrepareBillTaskAsync(
        int siNetProjectId,
        string title,
        string body,
        CancellationToken cancellationToken = default);

    Task<bool> HasOpenPrepareBillTaskAsync(
        int siNetProjectId,
        CancellationToken cancellationToken = default);

    Task<int?> FindOpenPrepareBillTaskForRequestAsync(
        int siNetProjectId,
        int billingPreparationRequestId,
        CancellationToken cancellationToken = default);
}

public interface IBillingPreparationService
{
    Task<BillingPreparationEnsureResult> EnsureFromPrepareBillAsync(
        int masterPlanProjectId,
        string? projectNumber,
        string? projectName,
        string? customerName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingPreparationRequestRecord>> ListForPreparationTabAsync(
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> RefreshFromSnapshotAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> SaveSelectionAsync(
        int requestId,
        IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
        IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> ApplyManualOverrideAsync(
        int requestId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationApproveResult> ApproveAndCreateTaskAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> OnPrepareBillTaskCompletedAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> ConfirmHourlyManuallyAsync(
        int requestId,
        string? note,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationRequestRecord> ReevaluateStageConfirmationAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task OnPrepareBillTaskCompletedByTaskIdAsync(
        int taskId,
        CancellationToken cancellationToken = default);

    Task<BillingPreparationSnapshotLoad> LoadComponentsAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
        IReadOnlyList<int> hourReportIds,
        int? excludeRequestId,
        CancellationToken cancellationToken = default);
}
