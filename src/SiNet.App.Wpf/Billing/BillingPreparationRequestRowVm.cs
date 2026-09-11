using System.Globalization;
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
        : this(source, catalog: null, feeTypeId: 0)
    {
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
            draft,
            draft.FeeTypeId);
    }

    public BillingPreparationStageEditVm(
        BillingPreparationStageLineSnapshot source,
        BillingPreparationStageDraft? catalog)
        : this(source, catalog, catalog?.FeeTypeId ?? 0)
    {
    }

    private BillingPreparationStageEditVm(
        BillingPreparationStageLineSnapshot source,
        BillingPreparationStageDraft? catalog,
        int feeTypeId)
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
        FeeTypeId = feeTypeId;
        MasterPlanContractId = catalog?.MasterPlanContractId ?? 0;
        ContractName = catalog?.ContractName;
        ContractNumber = catalog?.ContractNumber;
        SubContractNumber = catalog?.SubContractNumber;
        OrderNum = catalog?.OrderNum ?? 0;
        SubContractBillableAmount = catalog?.SubContractBillableAmount;
        DiscountFraction = catalog?.DiscountFraction ?? 0m;
        HasUnpricedIndexation = catalog?.HasUnpricedIndexation ?? false;
        _additionPercent = source.RequestedDelta * 100m;
    }

    public int MasterPlanStageId { get; }
    public int MasterPlanSubContractId { get; }
    public int MasterPlanContractId { get; }
    public string StageName { get; }
    public string SubContractName { get; }
    public string? SubContractNumber { get; }
    public string? ContractName { get; }
    public string? ContractNumber { get; }
    public int OrderNum { get; }
    public decimal StageWeightWithinSubContract { get; }
    public decimal? ObservedCumulativeProgress { get; }
    public bool HasDataQualityFlag { get; }
    public DateTime SnapshotTimestampUtc { get; }
    public BillingConfirmationMode ConfirmationMode { get; }
    public DateTime? ConfirmedAtUtc { get; }
    public int? ConfirmedByUserId { get; }
    public string? ConfirmationNote { get; }
    public int FeeTypeId { get; }
    public decimal? SubContractBillableAmount { get; }
    public decimal DiscountFraction { get; }
    public bool HasUnpricedIndexation { get; }
    public string AdditionAutomationId =>
        "BillingDashboard.StageAddition." + MasterPlanStageId.ToString(CultureInfo.InvariantCulture);
    public string AmountAutomationId =>
        "BillingDashboard.StageAmount." + MasterPlanStageId.ToString(CultureInfo.InvariantCulture);

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
    public decimal WeightPercent => StageWeightWithinSubContract * 100m;
    public decimal AfterBillPercent => ObservedPercent + AdditionPercent;
    public decimal TargetPercent => AfterBillPercent;
    public decimal RemainingPercent => 100m - AfterBillPercent;
    public decimal RequestedDelta => AdditionPercent / 100m;
    public bool HasPositiveAddition => AdditionPercent > 0m;
    public decimal RelativeSharePercent => StageWeightWithinSubContract * AdditionPercent;
    public bool ShowRelativeShare => AdditionPercent > 0m && StageWeightWithinSubContract > 0m;
    public decimal ObservedContributionPercent =>
        BillingStageContributionCalculator.SubContractContributionPercent(
            StageWeightWithinSubContract, ObservedCumulativeProgress ?? 0m);
    public decimal AdditionContributionPercent =>
        BillingStageContributionCalculator.SubContractContributionPercent(
            StageWeightWithinSubContract, RequestedDelta);
    public decimal AfterContributionPercent =>
        BillingStageContributionCalculator.SubContractContributionPercent(
            StageWeightWithinSubContract, (ObservedCumulativeProgress ?? 0m) + RequestedDelta);
    public decimal RemainingContributionPercent =>
        BillingStageContributionCalculator.SubContractContributionPercent(
            StageWeightWithinSubContract, Math.Max(0m, 1m - ((ObservedCumulativeProgress ?? 0m) + RequestedDelta)));
    public decimal MaxAdditionPercent => BillingStageProgressMessages.MaxAdditionPercent(ObservedPercent);
    public string MaxAdditionHintText => BillingStageProgressMessages.FormatMaxAdditionHint(ObservedPercent);
    public bool IsAfterBillValid => !HasAdditionValidation;

    public decimal ObservedBarShare => ObservedPercent;
    public decimal AdditionBarShare => IsAfterBillValid ? Math.Max(0m, AdditionPercent) : 0m;
    public decimal RemainingBarShare
    {
        get
        {
            var remaining = IsAfterBillValid ? Math.Max(0m, RemainingPercent) : Math.Max(0m, 100m - ObservedPercent);
            if (ObservedBarShare <= 0m && AdditionBarShare <= 0m && remaining <= 0m)
                return 100m;
            return remaining;
        }
    }

    public string WeightText => "משקל השלב בתת החוזה: " + FormatPercent(WeightPercent);
    public string CompactWeightText => "משקל בתת חוזה: " + FormatPercent(WeightPercent);
    public string ObservedCompactText =>
        FormatPercent(ObservedPercent) + " / " + FormatPercent(ObservedContributionPercent);
    public string AdditionContributionCompactText => FormatPercent(AdditionContributionPercent);
    public string AfterCompactText =>
        IsAfterBillValid
            ? FormatPercent(AfterBillPercent) + " / " + FormatPercent(AfterContributionPercent)
            : "לא תקין";
    public string RemainingCompactText =>
        IsAfterBillValid
            ? FormatPercent(RemainingPercent) + " / " + FormatPercent(RemainingContributionPercent)
            : "לא תקין";
    public string ObservedText => "כבר חויב: " + FormatPercent(ObservedPercent) + " מהשלב";
    public string ObservedStageText => FormatPercent(ObservedPercent) + " מהשלב";
    public string ObservedContributionText => FormatPercent(ObservedContributionPercent) + " מתת החוזה";
    public string AdditionStageText => FormatPercent(AdditionPercent) + " מהשלב";
    public string AdditionContributionText => FormatPercent(AdditionContributionPercent) + " מתת החוזה";
    public string AfterBillText =>
        IsAfterBillValid
            ? "לאחר החשבון: " + FormatPercent(AfterBillPercent)
            : "לאחר החשבון: לא תקין";
    public string AfterStageText =>
        IsAfterBillValid ? FormatPercent(AfterBillPercent) + " מהשלב" : "לא תקין";
    public string AfterContributionText =>
        IsAfterBillValid
            ? FormatPercent(AfterContributionPercent) + " מתת החוזה"
            : "לא תקין";
    public string RemainingText =>
        IsAfterBillValid
            ? "נותר: " + FormatPercent(RemainingPercent)
            : "נותר: לא תקין";
    public string RemainingStageText =>
        IsAfterBillValid ? FormatPercent(RemainingPercent) + " מהשלב" : "לא תקין";
    public string RemainingContributionText =>
        IsAfterBillValid
            ? FormatPercent(RemainingContributionPercent) + " מתת החוזה"
            : "לא תקין";
    public string RelativeShareText =>
        "תרומת התוספת לתת החוזה: " + FormatPercent(RelativeSharePercent);

    public BillingPricedValue AdditionAmount =>
        BillingPreparationAmountCalculator.StageAddition(ToPricingDraft(), RequestedDelta);

    public bool ShowAdditionAmount => HasPositiveAddition && !HasAdditionValidation;
    public bool AdditionAmountIsPriced => AdditionAmount.IsPriced;
    public string AdditionAmountText
    {
        get
        {
            if (!ShowAdditionAmount)
                return string.Empty;
            if (AdditionAmount.IsPriced)
                return "תוספת כספית בחשבון: " + BillingMoneyFormatter.FormatShekels(AdditionAmount.Amount!.Value);
            return "תוספת כספית בחשבון: לא ניתן לחשב — " + (AdditionAmount.UnavailableReason ?? "לא נמצא בסיס תמחור");
        }
    }

    public BillingPreparationStageDraft ToPricingDraft() =>
        new(
            MasterPlanStageId,
            MasterPlanSubContractId,
            StageName,
            SubContractName,
            StageWeightWithinSubContract,
            new BillingStageProgressCalculator.ObservedCumulativeProgress(
                ObservedCumulativeProgress,
                HasDataQualityFlag,
                HasDataQualityFlag ? [ObservedCumulativeProgress ?? -1] : []),
            ObservedCumulativeProgress,
            HasPositiveAddition,
            FeeTypeId,
            MasterPlanContractId,
            ContractName,
            ContractNumber,
            SubContractNumber,
            OrderNum,
            SubContractBillableAmount,
            DiscountFraction,
            HasUnpricedIndexation);

    public string? AdditionValidationMessage
    {
        get
        {
            var observed = new BillingStageProgressCalculator.ObservedCumulativeProgress(
                ObservedCumulativeProgress,
                HasDataQualityFlag,
                HasDataQualityFlag ? [ObservedCumulativeProgress ?? -1] : []);
            return BillingStageProgressMessages.FormatAdditionError(observed, RequestedDelta);
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
        OnPropertyChanged(nameof(ObservedContributionPercent));
        OnPropertyChanged(nameof(AdditionContributionPercent));
        OnPropertyChanged(nameof(AfterContributionPercent));
        OnPropertyChanged(nameof(RemainingContributionPercent));
        OnPropertyChanged(nameof(MaxAdditionPercent));
        OnPropertyChanged(nameof(MaxAdditionHintText));
        OnPropertyChanged(nameof(IsAfterBillValid));
        OnPropertyChanged(nameof(ObservedBarShare));
        OnPropertyChanged(nameof(AdditionBarShare));
        OnPropertyChanged(nameof(RemainingBarShare));
        OnPropertyChanged(nameof(ObservedText));
        OnPropertyChanged(nameof(ObservedStageText));
        OnPropertyChanged(nameof(ObservedContributionText));
        OnPropertyChanged(nameof(AdditionStageText));
        OnPropertyChanged(nameof(AdditionContributionText));
        OnPropertyChanged(nameof(AfterBillText));
        OnPropertyChanged(nameof(AfterStageText));
        OnPropertyChanged(nameof(AfterContributionText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(RemainingStageText));
        OnPropertyChanged(nameof(RemainingContributionText));
        OnPropertyChanged(nameof(RelativeShareText));
        OnPropertyChanged(nameof(ObservedCompactText));
        OnPropertyChanged(nameof(AdditionContributionCompactText));
        OnPropertyChanged(nameof(AfterCompactText));
        OnPropertyChanged(nameof(RemainingCompactText));
        OnPropertyChanged(nameof(AdditionValidationMessage));
        OnPropertyChanged(nameof(HasAdditionValidation));
        OnPropertyChanged(nameof(AdditionAmount));
        OnPropertyChanged(nameof(ShowAdditionAmount));
        OnPropertyChanged(nameof(AdditionAmountIsPriced));
        OnPropertyChanged(nameof(AdditionAmountText));
    }

    private static string FormatPercent(decimal percent) =>
        BillingStageProgressMessages.FormatPercent(percent);
}
