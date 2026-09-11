namespace SiNetSQL.Models;

/// <summary>
/// SiNet-local billing preparation cycle. External MasterPlan project identity is not a SiNet Project FK.
/// </summary>
public sealed class BillingPreparationRequest
{
    public int Id { get; set; }
    public int MasterPlanProjectId { get; set; }
    public int? SiNetProjectId { get; set; }
    public string? ProjectNumber { get; set; }
    public string? ProjectName { get; set; }
    public string? CustomerName { get; set; }
    public int Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int CreatedByUserId { get; set; }
    public string? CreatedByLogin { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? ApprovedByLogin { get; set; }
    public DateTime? SnapshotTimestampUtc { get; set; }
    public DateTime? LatestMasterPlanBackupUtc { get; set; }
    public int? TaskId { get; set; }

    public bool ManualOverride { get; set; }
    public string? ManualOverrideReason { get; set; }
    public DateTime? ManualOverrideAtUtc { get; set; }
    public int? ManualOverrideByUserId { get; set; }

    public decimal? PricingStageTotal { get; set; }
    public decimal? PricingHoursTotal { get; set; }
    public decimal? PricingTotal { get; set; }
    public bool? PricingIsPartial { get; set; }
    public DateTime? PricingFrozenAtUtc { get; set; }
    public DateTime? PricingSourceSnapshotUtc { get; set; }
    public string? PricingFormulaVersion { get; set; }

    public ICollection<BillingPreparationStageLine> Stages { get; set; } = new List<BillingPreparationStageLine>();
    public ICollection<BillingPreparationHoursLine> Hours { get; set; } = new List<BillingPreparationHoursLine>();
}

public sealed class BillingPreparationStageLine
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int MasterPlanStageId { get; set; }
    public int MasterPlanSubContractId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string SubContractName { get; set; } = string.Empty;
    public decimal StageWeightWithinSubContract { get; set; }
    public decimal? ObservedCumulativeProgress { get; set; }
    public decimal TargetCumulativeProgress { get; set; }
    public decimal RequestedDelta { get; set; }
    public bool HasDataQualityFlag { get; set; }
    public DateTime SnapshotTimestampUtc { get; set; }
    public int ConfirmationMode { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public string? ConfirmationNote { get; set; }
    public decimal? PricingBaseAmount { get; set; }
    public decimal? PricingDiscountFraction { get; set; }
    public decimal? PricingCalculatedAmount { get; set; }
    public string? PricingUnavailableReason { get; set; }

    public BillingPreparationRequest Request { get; set; } = null!;
}

public sealed class BillingPreparationHoursLine
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int MasterPlanSubContractId { get; set; }
    public string SubContractName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int ReportCount { get; set; }
    public decimal TotalHours { get; set; }
    public string? OverlappingHourReportIds { get; set; }
    public DateTime SnapshotTimestampUtc { get; set; }
    public int ConfirmationMode { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public string? ConfirmationNote { get; set; }
    public decimal? PricingHourlyRate { get; set; }
    public decimal? PricingDiscountFraction { get; set; }
    public decimal? PricingCalculatedAmount { get; set; }
    public string? PricingUnavailableReason { get; set; }

    public BillingPreparationRequest Request { get; set; } = null!;
    public ICollection<BillingPreparationHoursReport> Reports { get; set; } = new List<BillingPreparationHoursReport>();
}

public sealed class BillingPreparationHoursReport
{
    public int Id { get; set; }
    public int HoursLineId { get; set; }
    public int HoursReportId { get; set; }
    public DateTime Date { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public decimal Hours { get; set; }
    public int? SubContractId { get; set; }
    public int? SubContractStepId { get; set; }
    public string? Description { get; set; }

    public BillingPreparationHoursLine HoursLine { get; set; } = null!;
}
