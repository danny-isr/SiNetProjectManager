using System.Collections.ObjectModel;
using System.Globalization;
using SiNet.Application.Billing;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf.Billing;

public sealed class BillingPreparationContractGroupVm : ObservableObject
{
    private bool _isSearchVisible = true;

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

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        private set => SetField(ref _isSearchVisible, value);
    }

    public void ApplySearch(string? query)
    {
        var active = BillingPreparationStageSearch.IsActive(query);
        var contractMatch = active
            && BillingPreparationStageSearch.MatchesContract(ContractName, ContractNumber, query);
        foreach (var sub in SubContracts)
            sub.ApplySearch(query, contractMatch);
        IsSearchVisible = !active || contractMatch || SubContracts.Any(s => s.IsSearchVisible);
    }
}

public sealed class BillingPreparationSubContractGroupVm : ObservableObject
{
    private readonly BillingSubContractStageGroupDraft _draft;
    private bool _isExpanded;
    private bool _restingExpanded;
    private bool _isSearchVisible = true;
    private bool _filterMatchingStagesOnly;
    private HashSet<int> _matchingStageIds = [];
    private decimal _observedSummaryPercent;
    private decimal _additionSummaryPercent;
    private decimal _afterSummaryPercent;
    private decimal _remainingSummaryPercent;
    private decimal _pricedAdditionAmount;
    private bool _hasUnpricedAddition;

    public BillingPreparationSubContractGroupVm(BillingSubContractStageGroupDraft draft, bool showContract)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _ = showContract;
        EditableStages = [];
        VisibleEditableStages = [];
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
        group.CaptureRestingExpansion();
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
    public IReadOnlyList<BillingPreparationStageEditVm> VisibleEditableStages { get; private set; }
    public string GroupAutomationId =>
        "BillingDashboard.SubContract." + SubContractId.ToString(CultureInfo.InvariantCulture);

    public string HeaderTitle => "תת חוזה: " + SubContractName;
    public string NumberText => BillingSubContractStageGroupBuilder.FormatNumberLabel(SubContractNumber);
    public bool ShowNumber => !string.IsNullOrWhiteSpace(SubContractNumber);
    public string ContractHeaderText =>
        "חוזה: " + (string.IsNullOrWhiteSpace(ContractName)
            ? ContractId.ToString(CultureInfo.InvariantCulture)
            : ContractName);
    public bool ShowContractHeader => false;
    public int PaymentStageCount =>
        _draft.SummaryStages.Count > 0 ? _draft.SummaryStages.Count : EditableStages.Count;
    public string StageCountText => BillingSubContractStageGroupBuilder.FormatStageCount(PaymentStageCount);
    public int AdditionCount => EditableStages.Count(s => s.HasPositiveAddition);
    public string CompactSummaryText =>
        PaymentStageCount.ToString(CultureInfo.InvariantCulture)
        + " שלבים, "
        + AdditionCount.ToString(CultureInfo.InvariantCulture)
        + " עם תוספת בחשבון";

    public string CompactHeaderTitle
    {
        get
        {
            var parts = new List<string> { HeaderTitle };
            var compactNumber = BillingSubContractStageGroupBuilder.FormatCompactNumberLabel(SubContractNumber);
            if (!string.IsNullOrWhiteSpace(compactNumber))
                parts.Add(compactNumber);
            parts.Add(PaymentStageCount.ToString(CultureInfo.InvariantCulture) + " שלבים");
            if (AdditionCount > 0)
                parts.Add(AdditionCount.ToString(CultureInfo.InvariantCulture) + " עם תוספת");
            return string.Join(" | ", parts);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(HeaderWeightWarning));
                OnPropertyChanged(nameof(HeaderPartialWarning));
            }
        }
    }

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        private set => SetField(ref _isSearchVisible, value);
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

    public decimal PricedAdditionAmount
    {
        get => _pricedAdditionAmount;
        private set => SetField(ref _pricedAdditionAmount, value);
    }

    public bool HasUnpricedAddition
    {
        get => _hasUnpricedAddition;
        private set => SetField(ref _hasUnpricedAddition, value);
    }

    public bool ShowAdditionMoney => HasPositiveAddition;
    public string AdditionMoneyText
    {
        get
        {
            if (!HasPositiveAddition)
                return string.Empty;
            if (HasUnpricedAddition)
                return "סכום חלקי " + BillingMoneyFormatter.FormatShekels(PricedAdditionAmount);
            return BillingMoneyFormatter.FormatShekels(PricedAdditionAmount);
        }
    }

    public string HeaderAdditionMoneyText
    {
        get
        {
            if (!HasPositiveAddition)
                return string.Empty;
            var percent = BillingStageProgressMessages.FormatPercent(AdditionSummaryPercent);
            var money = HasUnpricedAddition
                ? "סכום חלקי " + BillingMoneyFormatter.FormatShekels(PricedAdditionAmount)
                : BillingMoneyFormatter.FormatShekels(PricedAdditionAmount);
            return "🔵 תוספת בחשבון: " + percent + "   |   " + money;
        }
    }

    public bool HasPositiveAddition => AdditionCount > 0;
    public bool HasInvalidAddition => EditableStages.Any(s => s.HasAdditionValidation);
    public bool WeightsSumApproximatelyToOne => _draft.WeightsSumApproximatelyToOne;
    public bool ShowWeightSumWarning => !_draft.WeightsSumApproximatelyToOne && PaymentStageCount > 0;
    public string WeightSumWarning =>
        ShowWeightSumWarning
            ? BillingSubContractStageGroupBuilder.FormatWeightSumWarning(_draft.ValidWeightSum * 100m)
            : string.Empty;
    public string CompactWeightSumWarning =>
        ShowWeightSumWarning
            ? BillingSubContractStageGroupBuilder.FormatCompactWeightSumWarning(_draft.ValidWeightSum * 100m)
            : string.Empty;
    public string HeaderWeightWarning => IsExpanded ? WeightSumWarning : CompactWeightSumWarning;
    public bool ShowPartialSummaryWarning => _draft.IsPartialBecauseOfDataQuality;
    public string PartialSummaryWarning => BillingSubContractStageGroupBuilder.PartialSummaryWarning;
    public string CompactPartialSummaryWarning =>
        BillingSubContractStageGroupBuilder.CompactPartialSummaryWarning;
    public string HeaderPartialWarning =>
        IsExpanded ? PartialSummaryWarning : CompactPartialSummaryWarning;
    public bool ShowDefinedWeightSummary => ShowWeightSumWarning;
    public string DefinedWeightSummaryText =>
        ShowDefinedWeightSummary
            ? BillingSubContractStageGroupBuilder.FormatDefinedWeightSummary(_draft.ValidWeightSum * 100m)
            : string.Empty;
    public bool ShowAfterBillAsValid => !HasInvalidAddition;

    public string ObservedSummaryText =>
        BillingSubContractStageGroupBuilder.FormatObservedSummary(
            ObservedSummaryPercent, _draft.WeightsSumApproximatelyToOne);
    public string AdditionSummaryText =>
        BillingSubContractStageGroupBuilder.FormatAdditionSummary(
            AdditionSummaryPercent, _draft.WeightsSumApproximatelyToOne);
    public string AfterSummaryText =>
        BillingSubContractStageGroupBuilder.FormatAfterSummary(
            AfterSummaryPercent, ShowAfterBillAsValid, _draft.WeightsSumApproximatelyToOne);
    public string RemainingSummaryText =>
        BillingSubContractStageGroupBuilder.FormatRemainingSummary(
            RemainingSummaryPercent, ShowAfterBillAsValid, _draft.WeightsSumApproximatelyToOne);

    public void CaptureRestingExpansion() => _restingExpanded = IsExpanded;

    public void ApplySearch(string? query, bool contractMatched)
    {
        var active = BillingPreparationStageSearch.IsActive(query);
        if (!active)
        {
            IsSearchVisible = true;
            _filterMatchingStagesOnly = false;
            _matchingStageIds = [];
            IsExpanded = _restingExpanded;
            PublishVisibleStages();
            return;
        }

        var subMatch = BillingPreparationStageSearch.MatchesSubContract(
            SubContractName, SubContractNumber, query);
        var matchingEditable = EditableStages
            .Where(s => BillingPreparationStageSearch.MatchesStageName(s.StageName, query))
            .Select(s => s.MasterPlanStageId)
            .ToHashSet();
        var hiddenOrSummaryMatch = SummaryStages.Any(s =>
            BillingPreparationStageSearch.MatchesStageName(s.StageName, query));
        var visible = contractMatched || subMatch || matchingEditable.Count > 0 || hiddenOrSummaryMatch;
        IsSearchVisible = visible;
        var showWholeGroup = contractMatched || subMatch || (hiddenOrSummaryMatch && matchingEditable.Count == 0);
        _filterMatchingStagesOnly = visible && !showWholeGroup;
        _matchingStageIds = matchingEditable;
        if (visible)
            IsExpanded = true;
        PublishVisibleStages();
    }

    public void AddEditable(BillingPreparationStageEditVm edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        EditableStages.Add(edit);
        PublishVisibleStages();
        OnPropertyChanged(nameof(PaymentStageCount));
        OnPropertyChanged(nameof(StageCountText));
        OnPropertyChanged(nameof(CompactSummaryText));
        OnPropertyChanged(nameof(CompactHeaderTitle));
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

        decimal priced = 0m;
        var unpriced = false;
        foreach (var stage in EditableStages.Where(s => s.HasPositiveAddition && !s.HasAdditionValidation))
        {
            var amount = stage.AdditionAmount;
            if (amount.IsPriced)
                priced += amount.Amount!.Value;
            else
                unpriced = true;
        }

        PricedAdditionAmount = priced;
        HasUnpricedAddition = unpriced;

        OnPropertyChanged(nameof(AdditionCount));
        OnPropertyChanged(nameof(CompactSummaryText));
        OnPropertyChanged(nameof(CompactHeaderTitle));
        OnPropertyChanged(nameof(HasPositiveAddition));
        OnPropertyChanged(nameof(HasInvalidAddition));
        OnPropertyChanged(nameof(ShowAfterBillAsValid));
        OnPropertyChanged(nameof(ObservedSummaryText));
        OnPropertyChanged(nameof(AdditionSummaryText));
        OnPropertyChanged(nameof(AfterSummaryText));
        OnPropertyChanged(nameof(RemainingSummaryText));
        OnPropertyChanged(nameof(DefinedWeightSummaryText));
        OnPropertyChanged(nameof(ShowDefinedWeightSummary));
        OnPropertyChanged(nameof(HeaderWeightWarning));
        OnPropertyChanged(nameof(HeaderPartialWarning));
        OnPropertyChanged(nameof(ShowAdditionMoney));
        OnPropertyChanged(nameof(AdditionMoneyText));
        OnPropertyChanged(nameof(HeaderAdditionMoneyText));
    }

    private void PublishVisibleStages()
    {
        VisibleEditableStages = _filterMatchingStagesOnly
            ? EditableStages.Where(s => _matchingStageIds.Contains(s.MasterPlanStageId)).ToList()
            : EditableStages.ToList();
        OnPropertyChanged(nameof(VisibleEditableStages));
    }
}
