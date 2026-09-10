namespace SiNet.Application.Billing;

public sealed record BillingPreparationStageDraft(
    int MasterPlanStageId,
    int MasterPlanSubContractId,
    string StageName,
    string SubContractName,
    decimal StageWeightWithinSubContract,
    BillingStageProgressCalculator.ObservedCumulativeProgress Observed,
    decimal? ObservedApprovedProgress,
    bool Included,
    int FeeTypeId = 0);

public sealed record BillingPreparationHoursDraft(
    int MasterPlanSubContractId,
    string SubContractName,
    DateTime FromDate,
    DateTime ToDate,
    IReadOnlyList<BillingPreparationHourReportSnapshot> Reports,
    IReadOnlyList<int> OverlappingHourReportIds);

public sealed record BillingPreparationHourReportSnapshot(
    int HoursReportId,
    DateTime Date,
    int? EmployeeId,
    string? EmployeeName,
    decimal Hours,
    int? SubContractId,
    int? SubContractStepId,
    string? Description);

public sealed record BillingPreparationStageLineSnapshot(
    int MasterPlanStageId,
    int MasterPlanSubContractId,
    string StageName,
    string SubContractName,
    decimal StageWeightWithinSubContract,
    decimal? ObservedCumulativeProgress,
    decimal TargetCumulativeProgress,
    decimal RequestedDelta,
    bool HasDataQualityFlag,
    DateTime SnapshotTimestampUtc,
    BillingConfirmationMode ConfirmationMode,
    DateTime? ConfirmedAtUtc,
    int? ConfirmedByUserId,
    string? ConfirmationNote);

public sealed record BillingPreparationHoursLineSnapshot(
    int MasterPlanSubContractId,
    string SubContractName,
    DateTime FromDate,
    DateTime ToDate,
    int ReportCount,
    decimal TotalHours,
    IReadOnlyList<BillingPreparationHourReportSnapshot> Reports,
    IReadOnlyList<int> OverlappingHourReportIds,
    DateTime SnapshotTimestampUtc,
    BillingConfirmationMode ConfirmationMode,
    DateTime? ConfirmedAtUtc,
    int? ConfirmedByUserId,
    string? ConfirmationNote);

public sealed record BillingPreparationRequestRecord(
    int Id,
    int MasterPlanProjectId,
    int? SiNetProjectId,
    string? ProjectNumber,
    string? ProjectName,
    string? CustomerName,
    BillingPreparationStatus Status,
    DateTime CreatedAtUtc,
    int CreatedByUserId,
    string? CreatedByLogin,
    DateTime? ApprovedAtUtc,
    int? ApprovedByUserId,
    string? ApprovedByLogin,
    DateTime? SnapshotTimestampUtc,
    DateTime? LatestMasterPlanBackupUtc,
    int? TaskId,
    bool ManualOverride,
    string? ManualOverrideReason,
    DateTime? ManualOverrideAtUtc,
    int? ManualOverrideByUserId,
    IReadOnlyList<BillingPreparationStageLineSnapshot> Stages,
    IReadOnlyList<BillingPreparationHoursLineSnapshot> Hours);

public sealed record BillingPreparationEnsureResult(
    BillingPreparationRequestRecord Request,
    bool Created);

public sealed record BillingPreparationApproveResult(
    BillingPreparationRequestRecord Request,
    int TaskId);

public sealed record BillingHourReportFact(
    int HoursReportId,
    int ProjectId,
    int? SubContractId,
    int? SubContractStepId,
    DateTime Date,
    int? EmployeeId,
    string? EmployeeName,
    decimal Hours,
    string? Description);
