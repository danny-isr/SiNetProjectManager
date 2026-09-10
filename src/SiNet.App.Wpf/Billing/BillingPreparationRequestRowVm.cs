using SiNet.Application.Billing;
using SiNet.App.Wpf.Inspection;

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

public sealed class BillingPreparationStageEditVm : ObservableObject
{
    private decimal _additionPercent;

    public BillingPreparationStageEditVm(BillingPreparationStageLineSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        MasterPlanStageId = source.MasterPlanStageId;
        MasterPlanSubContractId = source.MasterPlanSubContractId;
        StageName = source.StageName;
        SubContractName = source.SubContractName;
        StageWeightWithinSubContract = source.StageWeightWithinSubContract;
        ObservedCumulativeProgress = source.ObservedCumulativeProgress;
        HasDataQualityFlag = source.HasDataQualityFlag;
        SnapshotTimestampUtc = source.SnapshotTimestampUtc;
        ConfirmationMode = source.ConfirmationMode;
        ConfirmedAtUtc = source.ConfirmedAtUtc;
        ConfirmedByUserId = source.ConfirmedByUserId;
        ConfirmationNote = source.ConfirmationNote;
        FeeTypeId = 0;
        _additionPercent = source.RequestedDelta * 100m;
    }

    public static BillingPreparationStageEditVm FromCatalog(
        BillingPreparationStageDraft draft,
        DateTime snapshotTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var start = draft.Observed.HasOutliers ? 0m : (draft.Observed.Value ?? 0m);
        return new BillingPreparationStageEditVm(
            new BillingPreparationStageLineSnapshot(
                draft.MasterPlanStageId,
                draft.MasterPlanSubContractId,
                draft.StageName,
                draft.SubContractName,
                draft.StageWeightWithinSubContract,
                draft.Observed.Value,
                start,
                0m,
                draft.Observed.HasOutliers,
                snapshotTimestampUtc,
                BillingConfirmationMode.None,
                null,
                null,
                null),
            draft.FeeTypeId);
    }

    private BillingPreparationStageEditVm(BillingPreparationStageLineSnapshot source, int feeTypeId)
        : this(source)
    {
        FeeTypeId = feeTypeId;
    }

    public int MasterPlanStageId { get; }
    public int MasterPlanSubContractId { get; }
    public string StageName { get; }
    public string SubContractName { get; }
    public decimal StageWeightWithinSubContract { get; }
    public decimal? ObservedCumulativeProgress { get; }
    public bool HasDataQualityFlag { get; }
    public DateTime SnapshotTimestampUtc { get; }
    public BillingConfirmationMode ConfirmationMode { get; }
    public DateTime? ConfirmedAtUtc { get; }
    public int? ConfirmedByUserId { get; }
    public string? ConfirmationNote { get; }
    public int FeeTypeId { get; }

    public decimal AdditionPercent
    {
        get => _additionPercent;
        set
        {
            if (SetField(ref _additionPercent, value))
                RaiseCalculated();
        }
    }

    public decimal ObservedPercent => (ObservedCumulativeProgress ?? 0m) * 100m;
    public decimal AfterBillPercent => ObservedPercent + AdditionPercent;
    public decimal TargetPercent => AfterBillPercent;
    public decimal RemainingPercent => 100m - AfterBillPercent;
    public decimal RequestedDelta => AdditionPercent / 100m;
    public bool HasPositiveAddition => AdditionPercent > 0m;
    public decimal RelativeSharePercent => StageWeightWithinSubContract * AdditionPercent;
    public bool ShowRelativeShare => AdditionPercent > 0m && StageWeightWithinSubContract > 0m;

    public string WeightText => "משקל השלב בהסכם המשנה: " + FormatPercent(StageWeightWithinSubContract * 100m);
    public string ObservedText =>
        "חויב/נצפה עד כה ב-MasterPlan: " + FormatPercent(ObservedPercent);
    public string AfterBillText => "לאחר החשבון: " + FormatPercent(AfterBillPercent);
    public string RemainingText => "נותר בשלב לאחר החשבון: " + FormatPercent(RemainingPercent);
    public string RelativeShareText =>
        "חלק יחסי נוסף בהסכם המשנה: " + FormatPercent(RelativeSharePercent);

    public string? AdditionValidationMessage
    {
        get
        {
            var observed = new BillingStageProgressCalculator.ObservedCumulativeProgress(
                ObservedCumulativeProgress,
                HasDataQualityFlag,
                HasDataQualityFlag ? [ObservedCumulativeProgress ?? -1] : []);
            var validation = BillingStageProgressCalculator.ValidateAddition(observed, RequestedDelta);
            return validation.IsValid ? null : validation.Error;
        }
    }

    public bool HasAdditionValidation => !string.IsNullOrWhiteSpace(AdditionValidationMessage);

    public BillingPreparationStageLineSnapshot ToSnapshot()
    {
        var observed = new BillingStageProgressCalculator.ObservedCumulativeProgress(
            ObservedCumulativeProgress,
            HasDataQualityFlag,
            HasDataQualityFlag ? [ObservedCumulativeProgress ?? -1] : []);
        var validation = BillingStageProgressCalculator.ValidateAddition(observed, RequestedDelta);
        var target = validation.IsValid
            ? validation.Target
            : (ObservedCumulativeProgress ?? 0m) + RequestedDelta;
        var delta = validation.IsValid ? validation.Delta : RequestedDelta;
        return new BillingPreparationStageLineSnapshot(
            MasterPlanStageId,
            MasterPlanSubContractId,
            StageName,
            SubContractName,
            StageWeightWithinSubContract,
            ObservedCumulativeProgress,
            target,
            delta,
            HasDataQualityFlag,
            SnapshotTimestampUtc,
            ConfirmationMode,
            ConfirmedAtUtc,
            ConfirmedByUserId,
            ConfirmationNote);
    }

    private void RaiseCalculated()
    {
        OnPropertyChanged(nameof(AfterBillPercent));
        OnPropertyChanged(nameof(TargetPercent));
        OnPropertyChanged(nameof(RemainingPercent));
        OnPropertyChanged(nameof(RequestedDelta));
        OnPropertyChanged(nameof(HasPositiveAddition));
        OnPropertyChanged(nameof(RelativeSharePercent));
        OnPropertyChanged(nameof(ShowRelativeShare));
        OnPropertyChanged(nameof(AfterBillText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(RelativeShareText));
        OnPropertyChanged(nameof(AdditionValidationMessage));
        OnPropertyChanged(nameof(HasAdditionValidation));
    }

    private static string FormatPercent(decimal percent) => percent.ToString("0.##") + "%";
}
