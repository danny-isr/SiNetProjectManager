using SiNet.Application.Billing;

namespace SiNet.App.Wpf.Billing;

public sealed class BillingPreparationRequestRowVm
{
    public BillingPreparationRequestRowVm(BillingPreparationRequestRecord source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public BillingPreparationRequestRecord Source { get; }
    public int Id => Source.Id;
    public int MasterPlanProjectId => Source.MasterPlanProjectId;
    public string? ProjectNumber => Source.ProjectNumber;
    public string? ProjectName => Source.ProjectName;
    public string? CustomerName => Source.CustomerName;
    public BillingPreparationStatus Status => Source.Status;
    public string StatusText => Source.Status.ToHebrew();
    public string TaskIdText => Source.TaskId?.ToString() ?? "—";
    public bool ManualOverride => Source.ManualOverride;
}

public sealed class BillingPreparationStageEditVm
{
    public BillingPreparationStageEditVm(BillingPreparationStageLineSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        MasterPlanStageId = source.MasterPlanStageId;
        MasterPlanSubContractId = source.MasterPlanSubContractId;
        StageName = source.StageName;
        SubContractName = source.SubContractName;
        StageWeightWithinSubContract = source.StageWeightWithinSubContract;
        ObservedCumulativeProgress = source.ObservedCumulativeProgress;
        TargetPercent = source.TargetCumulativeProgress * 100m;
        HasDataQualityFlag = source.HasDataQualityFlag;
        SnapshotTimestampUtc = source.SnapshotTimestampUtc;
        ConfirmationMode = source.ConfirmationMode;
        ConfirmedAtUtc = source.ConfirmedAtUtc;
        ConfirmedByUserId = source.ConfirmedByUserId;
        ConfirmationNote = source.ConfirmationNote;
    }

    public int MasterPlanStageId { get; }
    public int MasterPlanSubContractId { get; }
    public string StageName { get; }
    public string SubContractName { get; }
    public decimal StageWeightWithinSubContract { get; }
    public decimal? ObservedCumulativeProgress { get; }
    public decimal TargetPercent { get; set; }
    public bool HasDataQualityFlag { get; }
    public DateTime SnapshotTimestampUtc { get; }
    public BillingConfirmationMode ConfirmationMode { get; }
    public DateTime? ConfirmedAtUtc { get; }
    public int? ConfirmedByUserId { get; }
    public string? ConfirmationNote { get; }

    public string WeightText => (StageWeightWithinSubContract * 100m).ToString("0.##") + "%";
    public string ObservedText => ObservedCumulativeProgress is decimal o
        ? (o * 100m).ToString("0.##") + "%"
        : "לא ידוע";
    public string DeltaText
    {
        get
        {
            if (ObservedCumulativeProgress is not decimal o)
                return (TargetPercent).ToString("0.##") + "%";
            return (TargetPercent - (o * 100m)).ToString("0.##") + "%";
        }
    }

    public BillingPreparationStageLineSnapshot ToSnapshot()
    {
        var target = TargetPercent / 100m;
        var observed = ObservedCumulativeProgress;
        var delta = observed is decimal o ? target - o : target;
        return new BillingPreparationStageLineSnapshot(
            MasterPlanStageId,
            MasterPlanSubContractId,
            StageName,
            SubContractName,
            StageWeightWithinSubContract,
            observed,
            target,
            delta,
            HasDataQualityFlag,
            SnapshotTimestampUtc,
            ConfirmationMode,
            ConfirmedAtUtc,
            ConfirmedByUserId,
            ConfirmationNote);
    }
}
