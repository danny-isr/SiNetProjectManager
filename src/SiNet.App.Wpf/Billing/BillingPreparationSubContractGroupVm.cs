using System.Collections.ObjectModel;
using System.Globalization;
using SiNet.Application.Billing;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf.Billing;

public sealed class BillingPreparationContractGroupVm
{
    public BillingPreparationContractGroupVm(
        int contractId,
        string? contractName,
        string? contractNumber,
        IEnumerable<BillingPreparationSubContractGroupVm> subContracts)
    {
        ContractId = contractId;
        ContractName = contractName;
        ContractNumber = contractNumber;
        SubContracts = new ObservableCollection<BillingPreparationSubContractGroupVm>(
            subContracts ?? throw new ArgumentNullException(nameof(subContracts)));
    }

    public int ContractId { get; }
    public string? ContractName { get; }
    public string? ContractNumber { get; }
    public ObservableCollection<BillingPreparationSubContractGroupVm> SubContracts { get; }
    public string GroupAutomationId => "BillingDashboard.Contract." + ContractId.ToString(CultureInfo.InvariantCulture);
    public string HeaderTitle =>
        "חוזה: " + (string.IsNullOrWhiteSpace(ContractName) ? ContractId.ToString(CultureInfo.InvariantCulture) : ContractName);
    public string NumberText => BillingSubContractStageGroupBuilder.FormatNumberLabel(ContractNumber);
    public bool ShowNumber => !string.IsNullOrWhiteSpace(ContractNumber);
}

public sealed class BillingPreparationSubContractGroupVm : ObservableObject
{
    private readonly BillingSubContractStageGroupDraft _draft;
    private readonly bool _showContract;
    private bool _isExpanded;
    private decimal _observedSummaryPercent;
    private decimal _additionSummaryPercent;
    private decimal _afterSummaryPercent;
    private decimal _remainingSummaryPercent;

    public BillingPreparationSubContractGroupVm(BillingSubContractStageGroupDraft draft, bool showContract)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _showContract = showContract;
        EditableStages = [];
    }

    public static BillingPreparationSubContractGroupVm FromOrphan(
        BillingPreparationStageEditVm edit,
        bool showContract)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var draft = new BillingSubContractStageGroupDraft(
            edit.MasterPlanSubContractId,
            edit.SubContractName,
            edit.SubContractNumber,
            edit.MasterPlanContractId,
            edit.ContractName,
            edit.ContractNumber,
            [],
            [],
            [],
            edit.StageWeightWithinSubContract,
            Math.Abs(edit.StageWeightWithinSubContract - 1m) <= BillingSubContractStageGroupBuilder.WeightSumTolerance,
            false);
        var group = new BillingPreparationSubContractGroupVm(draft, showContract);
        group.AddEditable(edit);
        group.IsExpanded = true;
        group.RefreshSummary();
        return group;
    }

    public int SubContractId => _draft.SubContractId;
    public int ContractId => _draft.ContractId;
    public string SubContractName => _draft.SubContractName;
    public string? SubContractNumber => _draft.SubContractNumber;
    public string? ContractName => _draft.ContractName;
    public string? ContractNumber => _draft.ContractNumber;
    public IReadOnlyList<BillingPreparationStageDraft> SummaryStages => _draft.SummaryStages;
    public ObservableCollection<BillingPreparationStageEditVm> EditableStages { get; }
    public string GroupAutomationId =>
        "BillingDashboard.SubContract." + SubContractId.ToString(CultureInfo.InvariantCulture);

    public string HeaderTitle => "תת חוזה: " + SubContractName;
    public string NumberText => BillingSubContractStageGroupBuilder.FormatNumberLabel(SubContractNumber);
    public bool ShowNumber => !string.IsNullOrWhiteSpace(SubContractNumber);
    public string ContractHeaderText =>
        "חוזה: " + (string.IsNullOrWhiteSpace(ContractName)
            ? ContractId.ToString(CultureInfo.InvariantCulture)
            : ContractName);
    public bool ShowContractHeader => _showContract;
    public int PaymentStageCount =>
        _draft.SummaryStages.Count > 0 ? _draft.SummaryStages.Count : EditableStages.Count;
    public string StageCountText => BillingSubContractStageGroupBuilder.FormatStageCount(PaymentStageCount);
    public int AdditionCount => EditableStages.Count(s => s.HasPositiveAddition);
    public string CompactSummaryText =>
        PaymentStageCount.ToString(CultureInfo.InvariantCulture)
        + " שלבים, "
        + AdditionCount.ToString(CultureInfo.InvariantCulture)
        + " עם תוספת בחשבון";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public decimal ObservedSummaryPercent
    {
        get => _observedSummaryPercent;
        private set => SetField(ref _observedSummaryPercent, value);
    }

    public decimal AdditionSummaryPercent
    {
        get => _additionSummaryPercent;
        private set => SetField(ref _additionSummaryPercent, value);
    }

    public decimal AfterSummaryPercent
    {
        get => _afterSummaryPercent;
        private set => SetField(ref _afterSummaryPercent, value);
    }

    public decimal RemainingSummaryPercent
    {
        get => _remainingSummaryPercent;
        private set => SetField(ref _remainingSummaryPercent, value);
    }

    public bool HasPositiveAddition => AdditionCount > 0;
    public bool HasInvalidAddition => EditableStages.Any(s => s.HasAdditionValidation);
    public bool ShowWeightSumWarning => !_draft.WeightsSumApproximatelyToOne && PaymentStageCount > 0;
    public string WeightSumWarning =>
        ShowWeightSumWarning
            ? BillingSubContractStageGroupBuilder.FormatWeightSumWarning(_draft.ValidWeightSum * 100m)
            : string.Empty;
    public bool ShowPartialSummaryWarning => _draft.IsPartialBecauseOfDataQuality;
    public string PartialSummaryWarning => BillingSubContractStageGroupBuilder.PartialSummaryWarning;
    public bool ShowAfterBillAsValid => !HasInvalidAddition;

    public string ObservedSummaryText =>
        "כבר חויב " + BillingStageProgressMessages.FormatPercent(ObservedSummaryPercent);
    public string AdditionSummaryText =>
        "תוספת בחשבון הזה " + BillingStageProgressMessages.FormatPercent(AdditionSummaryPercent);
    public string AfterSummaryText =>
        ShowAfterBillAsValid
            ? "לאחר החשבון " + BillingStageProgressMessages.FormatPercent(AfterSummaryPercent)
            : "לאחר החשבון: לא תקין";
    public string RemainingSummaryText =>
        ShowAfterBillAsValid
            ? "נותר " + BillingStageProgressMessages.FormatPercent(RemainingSummaryPercent)
            : "נותר: לא תקין";

    public void AddEditable(BillingPreparationStageEditVm edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        EditableStages.Add(edit);
        OnPropertyChanged(nameof(PaymentStageCount));
        OnPropertyChanged(nameof(StageCountText));
        OnPropertyChanged(nameof(CompactSummaryText));
    }

    public void RefreshSummary()
    {
        var additions = EditableStages
            .Where(s => !s.HasAdditionValidation)
            .ToDictionary(s => s.MasterPlanStageId, s => s.RequestedDelta);
        if (_draft.SummaryStages.Count > 0)
        {
            var summary = BillingSubContractStageGroupBuilder.Summarize(_draft, additions);
            ObservedSummaryPercent = summary.ObservedPercent;
            AdditionSummaryPercent = summary.AdditionPercent;
            AfterSummaryPercent = summary.AfterPercent;
            RemainingSummaryPercent = summary.RemainingPercent;
        }
        else
        {
            ObservedSummaryPercent = EditableStages.Sum(s => s.ObservedContributionPercent);
            AdditionSummaryPercent = EditableStages
                .Where(s => !s.HasAdditionValidation)
                .Sum(s => s.AdditionContributionPercent);
            AfterSummaryPercent = EditableStages
                .Where(s => !s.HasAdditionValidation)
                .Sum(s => s.AfterContributionPercent);
            RemainingSummaryPercent = EditableStages
                .Where(s => !s.HasAdditionValidation)
                .Sum(s => s.RemainingContributionPercent);
        }

        OnPropertyChanged(nameof(AdditionCount));
        OnPropertyChanged(nameof(CompactSummaryText));
        OnPropertyChanged(nameof(HasPositiveAddition));
        OnPropertyChanged(nameof(HasInvalidAddition));
        OnPropertyChanged(nameof(ShowAfterBillAsValid));
        OnPropertyChanged(nameof(ObservedSummaryText));
        OnPropertyChanged(nameof(AdditionSummaryText));
        OnPropertyChanged(nameof(AfterSummaryText));
        OnPropertyChanged(nameof(RemainingSummaryText));
    }
}
