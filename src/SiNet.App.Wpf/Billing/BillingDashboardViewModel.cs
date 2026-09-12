using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Billing;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Billing;

/// <summary>
/// Billing Control Center. Reads candidates from <see cref="IBillingDashboardReadService"/>
/// and records SiNet-local review decisions without writing MasterPlan.
/// </summary>
public sealed class BillingDashboardViewModel : ObservableObject
{
    internal static readonly BillingCandidateState[] DefaultActionableStates =
    [
        BillingCandidateState.ReviewNow,
        BillingCandidateState.AccumulatedWork,
        BillingCandidateState.BillInPreparation
    ];

    private readonly IBillingDashboardReadService _service;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger? _logger;
    private readonly IBillingReviewDecisionService? _reviewDecisions;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly IBillingReviewPrompts? _prompts;
    private readonly IBillingPreparationService? _preparation;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _prepareBillCommand;
    private readonly AsyncRelayCommand _continuePrepareBillCommand;
    private readonly AsyncRelayCommand _notNowCommand;
    private readonly AsyncRelayCommand _clearDecisionCommand;
    private readonly RelayCommand _addHourlyScopeCommand;
    private readonly RelayCommand _removeHourlyScopeCommand;

    private CancellationTokenSource? _loadCts;
    private IReadOnlyList<BillingDashboardRowVm> _allRows = [];
    private BillingDashboardResult? _lastResult;
    private BillingDashboardUiState _uiState = BillingDashboardUiState.Idle;
    private bool _isBusy;
    private bool _activeOnly = true;
    private bool _actionableOnly = true;
    private bool _canWriteBillingDecisions;
    private string _filterText = string.Empty;
    private BillingDashboardStateFilterOption _stateFilter = BillingDashboardStateFilterOption.All;
    private BillingDashboardRowVm? _selected;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private string _operationErrorMessage = string.Empty;
    private string _asOfDateText = BillingDashboardFormatters.EmDash;
    private DateTime _asOfDate = DateTime.Today;
    private string _freshnessStatusText = BillingDashboardFormatters.EmDash;
    private string _replicaLastSyncText = BillingDashboardFormatters.EmDash;
    private string _monthlySnapshotDateText = BillingDashboardFormatters.UnknownSnapshotDate;
    private string _reviewNowCountText = BillingDashboardFormatters.EmDash;
    private string _accumulatedWorkCountText = BillingDashboardFormatters.EmDash;
    private string _billInPreparationCountText = BillingDashboardFormatters.EmDash;
    private string _coveredCountText = BillingDashboardFormatters.EmDash;
    private string _receivedThisMonthText = BillingDashboardFormatters.EmDash;
    private string _warningBannerText = string.Empty;
    private string _diagnosticsSummary = string.Empty;
    private int _serviceCallCount;
    private int _selectedWorkspaceTab;
    private BillingPreparationRequestRowVm? _selectedPreparation;
    private string _manualOverrideReason = string.Empty;
    private string _hourlyConfirmNote = string.Empty;
    private IReadOnlyList<BillingHourlySubContractDraft> _hourlySubContracts = [];
    private IReadOnlyList<BillingHourReportFact> _hourReports = [];
    private IReadOnlyList<BillingPreparationStageDraft> _stageCatalog = [];
    private string _stageExclusionWarningHeader = string.Empty;
    private string _stageExclusionDetails = string.Empty;
    private bool _showContractLevel;
    private int _componentLoadGeneration;
    private HashSet<int> _activePreparationProjectIds = [];
    private string _stageSearchText = string.Empty;
    private bool _isDirty;
    private int _suspendDirty;
    private bool _suppressSelectionGuard;
    private BillingLiveAmountSummary _liveAmount = new(0m, 0m, 0m, []);
    private BillingProjectFinancialSummary _projectFinancial = BillingProjectFinancialSummaryCalculator.Empty;

    public BillingDashboardViewModel(
        IBillingDashboardReadService service,
        TimeProvider? timeProvider = null,
        IAppLogger? logger = null,
        IBillingReviewDecisionService? reviewDecisions = null,
        IAuthorizationQueryService? authorization = null,
        IBillingReviewPrompts? prompts = null,
        IBillingPreparationService? preparation = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _reviewDecisions = reviewDecisions;
        _authorization = authorization;
        _prompts = prompts;
        _preparation = preparation;
        _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        _prepareBillCommand = new AsyncRelayCommand(PrepareBillAsync, CanWriteNewDecision);
        _continuePrepareBillCommand = new AsyncRelayCommand(ContinuePrepareBillAsync, CanContinuePrepareBill);
        _notNowCommand = new AsyncRelayCommand(NotNowAsync, CanWriteNewDecision);
        _clearDecisionCommand = new AsyncRelayCommand(ClearDecisionAsync, CanClearDecision);
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        RefreshPreparationCommand = new AsyncRelayCommand(RefreshPreparationAsync, () => !IsBusy && _preparation is not null);
        SavePreparationCommand = new AsyncRelayCommand(SavePreparationAsync, CanSavePreparation);
        ManualOverrideCommand = new AsyncRelayCommand(ApplyManualOverrideAsync, CanEditPreparation);
        ApprovePreparationCommand = new AsyncRelayCommand(ApprovePreparationAsync, CanApprovePreparation);
        ConfirmHourlyCommand = new AsyncRelayCommand(ConfirmHourlyAsync, CanConfirmHourly);
        _addHourlyScopeCommand = new RelayCommand(_ => AddHourlyScope(), _ => CanEditPreparation());
        _removeHourlyScopeCommand = new RelayCommand(
            p => RemoveHourlyScope(p as BillingPreparationHoursScopeEditVm),
            p => CanEditPreparation() && p is BillingPreparationHoursScopeEditVm);
        ExpandAllStageGroupsCommand = new RelayCommand(_ => ExpandAllStageGroups());
        CollapseAllStageGroupsCommand = new RelayCommand(_ => CollapseAllStageGroups());
        Rows = new ObservableCollection<BillingDashboardRowVm>();
        PreparationRows = new ObservableCollection<BillingPreparationRequestRowVm>();
        StageEdits = new ObservableCollection<BillingPreparationStageEditVm>();
        StageGroups = new ObservableCollection<BillingPreparationSubContractGroupVm>();
        StageContractGroups = new ObservableCollection<BillingPreparationContractGroupVm>();
        HourlyScopeEdits = new ObservableCollection<BillingPreparationHoursScopeEditVm>();
        StateFilterOptions =
        [
            BillingDashboardStateFilterOption.All,
            new("לבדוק עכשיו", BillingCandidateState.ReviewNow),
            new("עבודה שהצטברה", BillingCandidateState.AccumulatedWork),
            new("חשבון בהכנה", BillingCandidateState.BillInPreparation),
            new("מכוסה בחשבון האחרון", BillingCandidateState.CoveredByLatestBill),
            new("לא דחוף", BillingCandidateState.NotUrgent),
            BillingDashboardStateFilterOption.MarkedPrepareBill,
            BillingDashboardStateFilterOption.Held
        ];
    }

    public ObservableCollection<BillingDashboardRowVm> Rows { get; }
    public ObservableCollection<BillingPreparationRequestRowVm> PreparationRows { get; }
    public ObservableCollection<BillingPreparationStageEditVm> StageEdits { get; }
    public ObservableCollection<BillingPreparationSubContractGroupVm> StageGroups { get; }
    public ObservableCollection<BillingPreparationContractGroupVm> StageContractGroups { get; }
    public ObservableCollection<BillingPreparationHoursScopeEditVm> HourlyScopeEdits { get; }
    public IReadOnlyList<BillingDashboardStateFilterOption> StateFilterOptions { get; }
    public ICommand RefreshCommand => _refreshCommand;
    public ICommand ClearFiltersCommand { get; }
    public ICommand PrepareBillCommand => _prepareBillCommand;
    public ICommand ContinuePrepareBillCommand => _continuePrepareBillCommand;
    public ICommand NotNowCommand => _notNowCommand;
    public ICommand ClearDecisionCommand => _clearDecisionCommand;
    public ICommand RefreshPreparationCommand { get; }
    public ICommand SavePreparationCommand { get; }
    public ICommand ManualOverrideCommand { get; }
    public ICommand ApprovePreparationCommand { get; }
    public ICommand ConfirmHourlyCommand { get; }
    public ICommand AddHourlyScopeCommand => _addHourlyScopeCommand;
    public ICommand RemoveHourlyScopeCommand => _removeHourlyScopeCommand;
    public ICommand ExpandAllStageGroupsCommand { get; }
    public ICommand CollapseAllStageGroupsCommand { get; }
    public string HourlyScopeCaption => BillingPreparationHoursScopeComposer.ManagerSelectedScopeCaption;
    public string StageExclusionWarningHeader => _stageExclusionWarningHeader;
    public string StageExclusionDetails => _stageExclusionDetails;
    public bool ShowStageExclusionWarning => !string.IsNullOrWhiteSpace(_stageExclusionWarningHeader);
    public bool ShowContractLevel => _showContractLevel;
    public bool ShowFlatSubContractGroups => !_showContractLevel;
    public bool ShowStageLegend => StageGroups.Count > 0;

    public int SelectedWorkspaceTab
    {
        get => _selectedWorkspaceTab;
        set
        {
            if (_selectedWorkspaceTab == value)
                return;
            if (_selectedWorkspaceTab == 1 && value != 1 && !TryLeaveUnsavedPreparationEdits())
            {
                OnPropertyChanged(nameof(SelectedWorkspaceTab));
                return;
            }

            if (SetField(ref _selectedWorkspaceTab, value) && value == 1)
                _ = RefreshPreparationAsync();
        }
    }

    public BillingPreparationRequestRowVm? SelectedPreparation
    {
        get => _selectedPreparation;
        set
        {
            if (ReferenceEquals(_selectedPreparation, value))
                return;
            if (!_suppressSelectionGuard && !TryLeaveUnsavedPreparationEdits())
            {
                OnPropertyChanged(nameof(SelectedPreparation));
                return;
            }

            if (!SetField(ref _selectedPreparation, value))
                return;

            _suspendDirty++;
            try
            {
                ReloadStageEdits();
                ReloadHourlyScopeEdits();
            }
            finally
            {
                _suspendDirty--;
            }

            OnPropertyChanged(nameof(HasSelectedPreparation));
            OnPropertyChanged(nameof(ShowPreparationAmountCard));
            OnPropertyChanged(nameof(SelectedPreparationStatusText));
            OnPropertyChanged(nameof(SelectedPreparationInstructions));
            OnPropertyChanged(nameof(ShowWaitingForSnapshot));
            OnPropertyChanged(nameof(CanConfirmHourlyVisible));
            RefreshPreparationAmount();
            RaisePreparationCommands();
            if (value is not null)
                _ = LoadPreparationComponentsAsync();
        }
    }

    public string StageSearchText
    {
        get => _stageSearchText;
        set
        {
            if (SetField(ref _stageSearchText, value ?? string.Empty))
                ApplyStageSearch();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(ShowDirtyBanner));
                OnPropertyChanged(nameof(DirtyBannerText));
            }
        }
    }

    public bool ShowDirtyBanner => IsDirty;
    public string DirtyBannerText => IsDirty ? "יש שינויים שלא נשמרו" : string.Empty;

    public bool HasSelectedPreparation => SelectedPreparation is not null;
    public string SelectedPreparationStatusText =>
        SelectedPreparation is null
            ? string.Empty
            : SelectedPreparation.Status.ToHebrew();
    public bool ShowWaitingForSnapshot =>
        SelectedPreparation?.Status == BillingPreparationStatus.WaitingForSnapshot;
    public bool CanConfirmHourlyVisible =>
        SelectedPreparation?.Status == BillingPreparationStatus.AwaitingMasterPlanConfirmation
        && SelectedPreparation.Source.Hours.Count > 0;
    public string SelectedPreparationInstructions =>
        SelectedPreparation is null
            ? string.Empty
            : BillingPreparationTaskInstructions.Build(
                SelectedPreparation.Source, _stageCatalog, _hourlySubContracts);

    public bool ShowPreparationAmountCard => HasSelectedPreparation;
    internal BillingLiveAmountSummary LiveAmount => _liveAmount;
    public string PreparationAmountTitle =>
        _liveAmount.IsPartial ? "סכום מחושב חלקית" : "סכום החשבון להכנה";
    public string PreparationAmountMainText =>
        BillingMoneyFormatter.FormatShekels(_liveAmount.PricedTotal);
    public string PreparationStageAmountText =>
        BillingMoneyFormatter.FormatShekels(_liveAmount.PricedStageTotal);
    public string PreparationHoursAmountText =>
        BillingMoneyFormatter.FormatShekels(_liveAmount.PricedHoursTotal);
    public string PreparationTotalCaption =>
        _liveAmount.IsPartial ? "סכום מחושב חלקית" : "סה\"כ";
    public string PreparationTotalText => PreparationAmountMainText;
    public bool ShowPreparationAmountMissing => _liveAmount.IsPartial;
    public string PreparationAmountMissingCountText =>
        _liveAmount.Missing.Count == 0
            ? string.Empty
            : _liveAmount.Missing.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
              + " רכיבים אינם כלולים בסכום";
    public string PreparationAmountMissingDetails =>
        string.Join(
            Environment.NewLine,
            _liveAmount.Missing.Select(m => "⚠ " + m.Label + " — " + m.Reason));

    public bool ShowProjectFinancialCard => HasSelectedPreparation;
    internal BillingProjectFinancialSummary ProjectFinancial => _projectFinancial;
    public string ProjectTotalFeeText => _projectFinancial.TotalFeeText;
    public string ProjectBalanceBeforeText => _projectFinancial.BalanceBeforeText;
    public string ProjectAlreadyBilledText => _projectFinancial.AlreadyBilledText;
    public string ProjectCurrentBillText => _projectFinancial.CurrentBillText;
    public string ProjectBalanceAfterText => _projectFinancial.BalanceAfterText;
    public decimal ProjectObservedBarShare => _projectFinancial.ObservedBarShare;
    public decimal ProjectAdditionBarShare => _projectFinancial.AdditionBarShare;
    public decimal ProjectRemainingBarShare => _projectFinancial.RemainingBarShare;
    public bool ShowProjectFinancialBar => _projectFinancial.ShowBar;
    public bool ShowProjectFinancialBarUnavailable => _projectFinancial.ShowBarUnavailable;
    public string ProjectFinancialBarUnavailableText => _projectFinancial.BarUnavailableReason ?? string.Empty;
    public string ProjectFinancialSourceText => _projectFinancial.SourceLine;
    public string ProjectFinancialExceedsText => _projectFinancial.ExceedsBalanceWarning ?? string.Empty;
    public bool ShowProjectFinancialExceeds => _projectFinancial.ShowExceedsWarning;
    public string ProjectFinancialHourlyNote => _projectFinancial.HourlyNote ?? string.Empty;
    public bool ShowProjectFinancialHourlyNote => _projectFinancial.ShowHourlyNote;
    public string ProjectFinancialInCreationNote => _projectFinancial.InCreationNote ?? string.Empty;
    public bool ShowProjectFinancialInCreationNote => _projectFinancial.ShowInCreationNote;

    public string ManualOverrideReason
    {
        get => _manualOverrideReason;
        set
        {
            if (SetField(ref _manualOverrideReason, value))
                MarkPreparationDirty();
        }
    }

    public string HourlyConfirmNote
    {
        get => _hourlyConfirmNote;
        set
        {
            if (SetField(ref _hourlyConfirmNote, value))
                MarkPreparationDirty();
        }
    }

    public string Title => "מרכז חיובים";
    public string ReceivedThisMonthLabel => BillingDashboardFormatters.ReceivedThisMonthLabel;
    public string BlockedHeadline => BillingDashboardFormatters.BlockedHeadline;
    public int ServiceCallCount => _serviceCallCount;

    public BillingDashboardUiState UiState
    {
        get => _uiState;
        private set
        {
            if (!SetField(ref _uiState, value))
                return;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(ShowWarningBanner));
            OnPropertyChanged(nameof(ShowBlockedPanel));
            OnPropertyChanged(nameof(ShowErrorBanner));
            OnPropertyChanged(nameof(ShowOperationErrorBanner));
            OnPropertyChanged(nameof(ShowCandidatesArea));
            OnPropertyChanged(nameof(ShowOperationalChrome));
            OnPropertyChanged(nameof(ShowHeaderStatusMessage));
            OnPropertyChanged(nameof(ShowEmptyFilterState));
            OnPropertyChanged(nameof(ShowEmptyServiceState));
            OnPropertyChanged(nameof(EmptyListMessage));
            OnPropertyChanged(nameof(ShowDecisionWriteButtons));
            OnPropertyChanged(nameof(ShowClearDecisionButton));
            OnPropertyChanged(nameof(ShowContinuePrepareBillButton));
            RaiseDecisionCommands();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
                RaiseDecisionCommands();
            }
        }
    }

    public bool IsLoading => UiState == BillingDashboardUiState.Loading;
    public bool ShowWarningBanner =>
        UiState == BillingDashboardUiState.Loaded
        && _lastResult?.FreshnessStatus == BillingReplicaFreshnessStatus.Warning;
    public bool ShowBlockedPanel => UiState == BillingDashboardUiState.FreshnessBlocked;
    public bool ShowErrorBanner =>
        UiState is BillingDashboardUiState.RecoverableError or BillingDashboardUiState.FatalError;
    public bool ShowOperationErrorBanner => !string.IsNullOrWhiteSpace(OperationErrorMessage);

    internal bool HasPreparationService => _preparation is not null;
    public bool ShowCandidatesArea =>
        UiState == BillingDashboardUiState.Loaded && _lastResult is { CandidatesBlocked: false };
    public bool ShowOperationalChrome => ShowCandidatesArea;
    public bool ShowHeaderStatusMessage =>
        !ShowBlockedPanel && !ShowErrorBanner && !ShowOperationErrorBanner && !string.IsNullOrWhiteSpace(StatusMessage);
    public bool ShowEmptyFilterState => ShowCandidatesArea && Rows.Count == 0 && _allRows.Count > 0;
    public bool ShowEmptyServiceState => ShowCandidatesArea && _allRows.Count == 0;
    public string EmptyListMessage => _allRows.Count == 0
        ? "השירות לא החזיר מועמדים."
        : "אין פרויקטים התואמים לסינון הנוכחי.";

    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (!SetField(ref _activeOnly, value))
                return;
            _ = RefreshAsync();
        }
    }

    public bool ActionableOnly
    {
        get => _actionableOnly;
        set
        {
            if (!SetField(ref _actionableOnly, value))
                return;
            ApplyFilters();
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value ?? string.Empty))
                return;
            ApplyFilters();
        }
    }

    public BillingDashboardStateFilterOption StateFilter
    {
        get => _stateFilter;
        set
        {
            if (!SetField(ref _stateFilter, value ?? BillingDashboardStateFilterOption.All))
                return;
            ApplyFilters();
        }
    }

    public BillingDashboardRowVm? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value))
                return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasActiveLocalDecision));
            OnPropertyChanged(nameof(HasActivePrepareBill));
            OnPropertyChanged(nameof(HasActiveNotNow));
            OnPropertyChanged(nameof(ShowDecisionWriteButtons));
            OnPropertyChanged(nameof(ShowClearDecisionButton));
            OnPropertyChanged(nameof(ShowContinuePrepareBillButton));
            RaiseDecisionCommands();
        }
    }

    public bool HasSelection => Selected is not null;
    public bool CanWriteBillingDecisions
    {
        get => _canWriteBillingDecisions;
        private set
        {
            if (!SetField(ref _canWriteBillingDecisions, value))
                return;
            OnPropertyChanged(nameof(ShowDecisionWriteButtons));
            OnPropertyChanged(nameof(ShowClearDecisionButton));
            OnPropertyChanged(nameof(ShowContinuePrepareBillButton));
            RaiseDecisionCommands();
        }
    }

    public bool HasActiveLocalDecision => Selected?.HasActiveLocalDecision == true;
    public bool HasActivePrepareBill => Selected?.HasActivePrepareBill == true;
    public bool HasActiveNotNow => Selected?.HasActiveNotNow == true;
    public bool ShowDecisionWriteButtons =>
        CanWriteBillingDecisions && HasSelection && !HasActiveLocalDecision && ShowCandidatesArea;
    public bool ShowClearDecisionButton =>
        CanWriteBillingDecisions && HasSelection && HasActiveLocalDecision && ShowCandidatesArea;
    public bool ShowContinuePrepareBillButton =>
        CanWriteBillingDecisions
        && HasActivePrepareBill
        && _preparation is not null
        && !HasActivePreparationRequestForSelected
        && ShowCandidatesArea;
    private bool HasActivePreparationRequestForSelected =>
        Selected is not null && _activePreparationProjectIds.Contains(Selected.ProjectId);
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
                OnPropertyChanged(nameof(ShowHeaderStatusMessage));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(ShowErrorBanner));
        }
    }

    public string OperationErrorMessage
    {
        get => _operationErrorMessage;
        private set
        {
            if (SetField(ref _operationErrorMessage, value))
            {
                OnPropertyChanged(nameof(ShowOperationErrorBanner));
                OnPropertyChanged(nameof(ShowHeaderStatusMessage));
            }
        }
    }

    public string AsOfDateText
    {
        get => _asOfDateText;
        private set => SetField(ref _asOfDateText, value);
    }

    public string FreshnessStatusText
    {
        get => _freshnessStatusText;
        private set => SetField(ref _freshnessStatusText, value);
    }

    public string ReplicaLastSyncText
    {
        get => _replicaLastSyncText;
        private set => SetField(ref _replicaLastSyncText, value);
    }

    public string MonthlySnapshotDateText
    {
        get => _monthlySnapshotDateText;
        private set => SetField(ref _monthlySnapshotDateText, value);
    }

    public string ReviewNowCountText
    {
        get => _reviewNowCountText;
        private set => SetField(ref _reviewNowCountText, value);
    }

    public string AccumulatedWorkCountText
    {
        get => _accumulatedWorkCountText;
        private set => SetField(ref _accumulatedWorkCountText, value);
    }

    public string BillInPreparationCountText
    {
        get => _billInPreparationCountText;
        private set => SetField(ref _billInPreparationCountText, value);
    }

    public string CoveredCountText
    {
        get => _coveredCountText;
        private set => SetField(ref _coveredCountText, value);
    }

    public string ReceivedThisMonthText
    {
        get => _receivedThisMonthText;
        private set => SetField(ref _receivedThisMonthText, value);
    }

    public string WarningBannerText
    {
        get => _warningBannerText;
        private set => SetField(ref _warningBannerText, value);
    }

    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetField(ref _diagnosticsSummary, value);
    }

    public Task LoadAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsBusy = true;
        UiState = BillingDashboardUiState.Loading;
        ErrorMessage = string.Empty;
        OperationErrorMessage = string.Empty;
        StatusMessage = "טוען מועמדים לחיוב…";
        try
        {
            _serviceCallCount++;
            var asOf = _timeProvider.GetLocalNow().Date;
            _asOfDate = asOf;
            AsOfDateText = asOf.ToString("dd/MM/yyyy");
            CanWriteBillingDecisions = await ResolveCanWriteAsync(ct).ConfigureAwait(true);
            var result = await _service.GetAsync(
                    new BillingDashboardRequest(ActiveOnly: ActiveOnly),
                    ct)
                .ConfigureAwait(true);
            ApplyResult(result);
            if (!result.CandidatesBlocked)
                await LoadActivePreparationIndexAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "הרענון בוטל.";
        }
        catch (InvalidOperationException ex)
        {
            _logger?.Error("[Billing] dashboard fatal source error", ex);
            UiState = BillingDashboardUiState.FatalError;
            ErrorMessage = ex.Message;
            StatusMessage = "שגיאת מקור נתונים.";
            ClearKpis();
            Rows.Clear();
            _allRows = [];
            Selected = null;
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] dashboard recoverable error", ex);
            UiState = BillingDashboardUiState.RecoverableError;
            ErrorMessage = ex.Message;
            StatusMessage = "הרענון נכשל. ניתן לנסות שוב.";
            ClearKpis();
            Rows.Clear();
            _allRows = [];
            Selected = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ClearFilters()
    {
        FilterText = string.Empty;
        StateFilter = BillingDashboardStateFilterOption.All;
        ActionableOnly = false;
    }

    private void ApplyResult(BillingDashboardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _lastResult = result;
        FreshnessStatusText = BillingDashboardFormatters.Freshness(result.FreshnessStatus);
        ReplicaLastSyncText = BillingDashboardFormatters.DateTimeStamp(result.Summary.ReplicaLastSyncTime);
        MonthlySnapshotDateText = BillingDashboardFormatters.SnapshotDateText(result.Summary.MonthlySnapshotDate);
        DiagnosticsSummary = BuildDiagnostics(result);
        WarningBannerText = result.Warnings.Count > 0
            ? string.Join(" · ", result.Warnings.Select(w => w.Message))
            : "נתוני Replica בחריגה — המועמדים מוצגים, אך יש לבדוק את עדכניות הנתונים.";

        if (result.CandidatesBlocked)
        {
            UiState = BillingDashboardUiState.FreshnessBlocked;
            StatusMessage = string.Empty;
            ClearKpis();
            Rows.Clear();
            _allRows = [];
            Selected = null;
            OnPropertyChanged(nameof(ShowOperationalChrome));
            OnPropertyChanged(nameof(ShowEmptyFilterState));
            OnPropertyChanged(nameof(ShowEmptyServiceState));
            RefreshPreparationAmount();
            return;
        }

        ReviewNowCountText = result.Summary.ReviewNowCount.ToString();
        AccumulatedWorkCountText = result.Summary.AccumulatedWorkCount.ToString();
        BillInPreparationCountText = result.Summary.BillInPreparationCount.ToString();
        CoveredCountText = result.Summary.CoveredByLatestBillCount.ToString();
        ReceivedThisMonthText = BillingDashboardFormatters.Money(result.Summary.ReceivedThisMonth);

        _allRows = result.Candidates.Select(r => new BillingDashboardRowVm(r)).ToList();
        UiState = BillingDashboardUiState.Loaded;
        ApplyFilters();
        StatusMessage = $"נטענו {result.Candidates.Count} מועמדים מהשירות.";
        RefreshPreparationAmount();
    }

    private void ApplyFilters()
    {
        if (_lastResult is { CandidatesBlocked: true })
            return;

        IEnumerable<BillingDashboardRowVm> query = _allRows;
        if (StateFilter.LocalDecision is BillingLocalDecisionType localType)
        {
            query = query.Where(r =>
                r.HasActiveLocalDecision && r.Source.LocalDecision!.DecisionType == localType);
        }
        else if (StateFilter.State is BillingCandidateState specific)
        {
            query = query.Where(r => r.CandidateState == specific);
        }
        else if (ActionableOnly)
        {
            query = query.Where(r => BillingLocalDecisionApplier.IncludeInDefaultActionableView(
                r.Source,
                DefaultActionableStates,
                _asOfDate));
        }

        var text = FilterText.Trim();
        if (text.Length > 0)
        {
            query = query.Where(r => Matches(r, text));
        }

        var selectedId = Selected?.ProjectId;
        Rows.Clear();
        foreach (var row in query)
            Rows.Add(row);

        Selected = selectedId is int id
            ? Rows.FirstOrDefault(r => r.ProjectId == id)
            : Rows.Count > 0 ? Rows[0] : null;
        OnPropertyChanged(nameof(ShowEmptyFilterState));
        OnPropertyChanged(nameof(ShowEmptyServiceState));
        OnPropertyChanged(nameof(EmptyListMessage));
        OnPropertyChanged(nameof(HasActiveLocalDecision));
        OnPropertyChanged(nameof(HasActivePrepareBill));
        OnPropertyChanged(nameof(HasActiveNotNow));
        OnPropertyChanged(nameof(ShowDecisionWriteButtons));
        OnPropertyChanged(nameof(ShowClearDecisionButton));
        OnPropertyChanged(nameof(ShowContinuePrepareBillButton));
        RaiseDecisionCommands();
    }

    public async Task PrepareBillAsync()
    {
        var row = Selected;
        if (row is null || _reviewDecisions is null || !CanWriteNewDecision())
            return;

        var label = ProjectLabel(row);
        if (_prompts is not null && !_prompts.ConfirmPrepareBill(label))
            return;

        await WriteDecisionAsync(
            row,
            BillingLocalDecisionType.PrepareBill,
            reason: null,
            reviewAgain: null).ConfigureAwait(true);

        row = Selected;
        if (row is null || _preparation is null)
            return;

        await EnsurePreparationAndShowAsync(row).ConfigureAwait(true);
    }

    public async Task ContinuePrepareBillAsync()
    {
        var row = Selected;
        if (row is null || !CanContinuePrepareBill())
            return;

        await EnsurePreparationAndShowAsync(row).ConfigureAwait(true);
    }

    public async Task NotNowAsync()
    {
        var row = Selected;
        if (row is null || _reviewDecisions is null || !CanWriteNewDecision())
            return;

        var label = ProjectLabel(row);
        string? reason = null;
        DateTime? reviewAgain = null;
        if (_prompts is not null)
        {
            var prompt = _prompts.PromptNotNow(label);
            if (prompt is null)
                return;
            reason = prompt.Reason;
            reviewAgain = prompt.ReviewAgainDate;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            OperationErrorMessage = "חובה לציין סיבה להשהיה.";
            return;
        }

        await WriteDecisionAsync(
            row,
            BillingLocalDecisionType.NotNow,
            reason,
            reviewAgain).ConfigureAwait(true);
    }

    public async Task ClearDecisionAsync()
    {
        var row = Selected;
        if (row is null || _reviewDecisions is null || !CanClearDecision())
            return;

        if (_prompts is not null && !_prompts.ConfirmClearDecision(ProjectLabel(row)))
            return;

        try
        {
            await _reviewDecisions.ClearAsync(row.ProjectId).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            OperationErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] clear local decision failed", ex);
            OperationErrorMessage = ex.Message;
        }
    }

    private async Task WriteDecisionAsync(
        BillingDashboardRowVm row,
        BillingLocalDecisionType type,
        string? reason,
        DateTime? reviewAgain)
    {
        if (_reviewDecisions is null)
            return;

        var source = row.Source;
        var request = new BillingReviewDecisionWriteRequest(
            source.ProjectId,
            type,
            reason,
            reviewAgain,
            source.LatestBillId,
            source.LatestBillStatusId,
            source.LastBillDate,
            source.HoursSinceLastBill,
            source.LastWorkDate);
        try
        {
            await _reviewDecisions.SaveAsync(request).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            OperationErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] save local decision failed", ex);
            OperationErrorMessage = ex.Message;
        }
    }

    private async Task<bool> ResolveCanWriteAsync(CancellationToken cancellationToken)
    {
        if (_reviewDecisions is null)
            return false;
        if (_authorization is null)
            return true;
        return await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.BillingRecordReviewDecision, cancellationToken)
            .ConfigureAwait(true);
    }

    private bool CanWriteNewDecision() =>
        !IsBusy
        && CanWriteBillingDecisions
        && Selected is not null
        && !HasActiveLocalDecision
        && ShowCandidatesArea;

    private bool CanClearDecision() =>
        !IsBusy
        && CanWriteBillingDecisions
        && Selected is not null
        && HasActiveLocalDecision
        && ShowCandidatesArea;

    private bool CanContinuePrepareBill() =>
        !IsBusy
        && CanWriteBillingDecisions
        && HasActivePrepareBill
        && _preparation is not null
        && !HasActivePreparationRequestForSelected
        && ShowCandidatesArea;

    private void RaiseDecisionCommands()
    {
        _prepareBillCommand.RaiseCanExecuteChanged();
        _continuePrepareBillCommand.RaiseCanExecuteChanged();
        _notNowCommand.RaiseCanExecuteChanged();
        _clearDecisionCommand.RaiseCanExecuteChanged();
    }

    private async Task EnsurePreparationAndShowAsync(BillingDashboardRowVm row)
    {
        if (_preparation is null)
            return;

        try
        {
            OperationErrorMessage = string.Empty;
            await _preparation.EnsureFromPrepareBillAsync(
                    row.ProjectId,
                    row.ProjectNumber,
                    row.ProjectName,
                    row.CustomerName)
                .ConfigureAwait(true);
            await LoadActivePreparationIndexAsync().ConfigureAwait(true);
            SelectedWorkspaceTab = 1;
            await RefreshPreparationAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] ensure preparation request failed", ex);
            OperationErrorMessage = "לא ניתן היה לפתוח בקשת הכנת חשבון: " + ex.Message;
        }
    }

    private async Task LoadActivePreparationIndexAsync(CancellationToken cancellationToken = default)
    {
        if (_preparation is null)
        {
            _activePreparationProjectIds = [];
            OnPropertyChanged(nameof(ShowContinuePrepareBillButton));
            _continuePrepareBillCommand.RaiseCanExecuteChanged();
            return;
        }

        try
        {
            var rows = await _preparation.ListForPreparationTabAsync(cancellationToken).ConfigureAwait(true);
            _activePreparationProjectIds = rows.Select(r => r.MasterPlanProjectId).ToHashSet();
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] list preparation requests failed", ex);
        }

        OnPropertyChanged(nameof(ShowContinuePrepareBillButton));
        _continuePrepareBillCommand.RaiseCanExecuteChanged();
    }

    private static string ProjectLabel(BillingDashboardRowVm row) =>
        string.IsNullOrWhiteSpace(row.ProjectNumber)
            ? row.ProjectName ?? $"פרויקט {row.ProjectId}"
            : $"{row.ProjectNumber} — {row.ProjectName}";

    private static bool Matches(BillingDashboardRowVm row, string text) =>
        Contains(row.ProjectNumber, text)
        || Contains(row.ProjectName, text)
        || Contains(row.CustomerName, text);

    private static bool Contains(string? value, string text) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    private void ClearKpis()
    {
        ReviewNowCountText = BillingDashboardFormatters.EmDash;
        AccumulatedWorkCountText = BillingDashboardFormatters.EmDash;
        BillInPreparationCountText = BillingDashboardFormatters.EmDash;
        CoveredCountText = BillingDashboardFormatters.EmDash;
        ReceivedThisMonthText = BillingDashboardFormatters.EmDash;
    }

    internal async Task RefreshPreparationAsync()
    {
        if (_preparation is null)
            return;
        if (!TryLeaveUnsavedPreparationEdits())
            return;

        var selectedId = SelectedPreparation?.Id;
        var rows = await _preparation.ListForPreparationTabAsync().ConfigureAwait(true);
        PreparationRows.Clear();
        foreach (var row in rows)
            PreparationRows.Add(new BillingPreparationRequestRowVm(row));
        _suppressSelectionGuard = true;
        _suspendDirty++;
        try
        {
            SelectedPreparation = selectedId is int id
                ? PreparationRows.FirstOrDefault(r => r.Id == id)
                : PreparationRows.Count > 0 ? PreparationRows[0] : null;
        }
        finally
        {
            _suspendDirty--;
            _suppressSelectionGuard = false;
        }

        if (SelectedPreparation is not null)
            await LoadPreparationComponentsAsync().ConfigureAwait(true);
        ClearDirty();
        RaisePreparationCommands();
    }

    internal bool TryLeaveUnsavedPreparationEdits()
    {
        if (!IsDirty)
            return true;
        var decision = _prompts?.ConfirmDiscardUnsavedPreparationEdits()
                       ?? BillingUnsavedEditsDecision.Stay;
        if (decision == BillingUnsavedEditsDecision.Stay)
            return false;
        ClearDirty();
        return true;
    }

    private void MarkPreparationDirty()
    {
        if (_suspendDirty > 0)
            return;
        IsDirty = true;
    }

    private void ClearDirty() => IsDirty = false;

    private void ReloadStageEdits()
    {
        RebuildStageEdits();
    }

    private void RebuildStageEdits()
    {
        foreach (var existing in StageEdits)
            existing.PropertyChanged -= OnStageEditPropertyChanged;
        StageEdits.Clear();
        StageGroups.Clear();
        StageContractGroups.Clear();
        var saved = SelectedPreparation?.Source.Stages ?? [];
        var savedById = saved.ToDictionary(s => s.MasterPlanStageId);
        var stamp = SelectedPreparation?.Source.SnapshotTimestampUtc
                    ?? _timeProvider.GetUtcNow().UtcDateTime;
        var groupDrafts = BillingSubContractStageGroupBuilder.Build(_stageCatalog);
        _showContractLevel = BillingSubContractStageGroupBuilder.ShouldShowContractLevel(groupDrafts);
        var shown = new HashSet<int>();
        foreach (var draftGroup in groupDrafts)
        {
            var groupVm = new BillingPreparationSubContractGroupVm(draftGroup, _showContractLevel);
            foreach (var stageDraft in draftGroup.EditableStages)
            {
                var edit = savedById.TryGetValue(stageDraft.MasterPlanStageId, out var line)
                    ? new BillingPreparationStageEditVm(line, stageDraft)
                    : BillingPreparationStageEditVm.FromCatalog(stageDraft, stamp);
                AttachStage(edit);
                groupVm.AddEditable(edit);
                shown.Add(stageDraft.MasterPlanStageId);
            }

            groupVm.IsExpanded = groupVm.HasPositiveAddition || groupDrafts.Count == 1;
            groupVm.CaptureRestingExpansion();
            groupVm.RefreshSummary();
            StageGroups.Add(groupVm);
        }

        foreach (var line in saved)
        {
            if (shown.Contains(line.MasterPlanStageId))
                continue;
            var catalog = _stageCatalog.FirstOrDefault(s => s.MasterPlanStageId == line.MasterPlanStageId);
            if (catalog is not null && !BillingPreparationStageEditorFilter.IsEditable(catalog))
                continue;
            var edit = catalog is null
                ? new BillingPreparationStageEditVm(line)
                : new BillingPreparationStageEditVm(line, catalog);
            AttachStage(edit);
            var group = StageGroups.FirstOrDefault(g => g.SubContractId == edit.MasterPlanSubContractId);
            if (group is null)
            {
                group = BillingPreparationSubContractGroupVm.FromOrphan(edit, _showContractLevel);
                StageGroups.Add(group);
            }
            else
            {
                group.AddEditable(edit);
                group.RefreshSummary();
            }

            shown.Add(line.MasterPlanStageId);
        }

        StageContractGroups.Clear();
        if (_showContractLevel)
        {
            foreach (var contract in StageGroups.GroupBy(g => g.ContractId))
            {
                var first = contract.First();
                StageContractGroups.Add(new BillingPreparationContractGroupVm(
                    first.ContractId,
                    first.ContractName,
                    first.ContractNumber,
                    contract));
            }
        }

        var warnings = _stageCatalog
            .Select(d => (Draft: d, Decision: BillingPreparationStageEditorFilter.Classify(d)))
            .Where(x => x.Decision.ShowInDataQualityWarning)
            .ToList();
        _stageExclusionWarningHeader = warnings.Count == 0
            ? string.Empty
            : warnings.Count + " שלבים אינם זמינים לחיוב אוטומטי ודורשים בדיקה";
        _stageExclusionDetails = string.Join(
            Environment.NewLine + Environment.NewLine,
            warnings.Select(x => BillingPreparationStageEditorFilter.FormatExpandedWarning(
                x.Draft, x.Decision, _showContractLevel)));
        OnPropertyChanged(nameof(StageExclusionWarningHeader));
        OnPropertyChanged(nameof(StageExclusionDetails));
        OnPropertyChanged(nameof(ShowStageExclusionWarning));
        OnPropertyChanged(nameof(ShowContractLevel));
        OnPropertyChanged(nameof(ShowFlatSubContractGroups));
        OnPropertyChanged(nameof(ShowStageLegend));
        ApplyStageSearch();
        RefreshPreparationAmount();
        RaisePreparationCommands();
    }

    private void ApplyStageSearch()
    {
        if (_showContractLevel)
        {
            foreach (var contract in StageContractGroups)
                contract.ApplySearch(_stageSearchText);
            return;
        }

        foreach (var group in StageGroups)
            group.ApplySearch(_stageSearchText, contractMatched: false);
    }

    private void AttachStage(BillingPreparationStageEditVm edit)
    {
        edit.PropertyChanged += OnStageEditPropertyChanged;
        StageEdits.Add(edit);
    }

    private void OnStageEditPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BillingPreparationStageEditVm.AdditionPercent)
            or nameof(BillingPreparationStageEditVm.HasPositiveAddition)
            or nameof(BillingPreparationStageEditVm.HasAdditionValidation))
        {
            if (sender is BillingPreparationStageEditVm stage)
            {
                var group = StageGroups.FirstOrDefault(g => g.SubContractId == stage.MasterPlanSubContractId);
                group?.RefreshSummary();
            }

            if (e.PropertyName == nameof(BillingPreparationStageEditVm.AdditionPercent))
                MarkPreparationDirty();

            RefreshPreparationAmount();
            RaisePreparationCommands();
        }
    }

    private void ExpandAllStageGroups()
    {
        foreach (var group in StageGroups)
            group.IsExpanded = true;
    }

    private void CollapseAllStageGroups()
    {
        foreach (var group in StageGroups)
            group.IsExpanded = false;
    }

    private void ReloadHourlyScopeEdits()
    {
        foreach (var existing in HourlyScopeEdits)
            existing.PropertyChanged -= OnHourlyScopePropertyChanged;
        HourlyScopeEdits.Clear();
        if (SelectedPreparation is null)
            return;
        var groups = BillingPreparationHoursScopeComposer.GroupCompatibleScopes(SelectedPreparation.Source.Hours);
        foreach (var group in groups)
            AttachHourlyScope(BillingPreparationHoursScopeEditVm.FromSavedLines(group));
        BindHourlyCatalog();
    }

    private void AttachHourlyScope(BillingPreparationHoursScopeEditVm edit)
    {
        edit.PropertyChanged += OnHourlyScopePropertyChanged;
        HourlyScopeEdits.Add(edit);
        edit.BindCatalog(_hourlySubContracts, _hourReports);
    }

    private void BindHourlyCatalog()
    {
        foreach (var edit in HourlyScopeEdits)
            edit.BindCatalog(_hourlySubContracts, _hourReports);
    }

    private void AddHourlyScope()
    {
        if (!CanEditPreparation())
            return;
        AttachHourlyScope(new BillingPreparationHoursScopeEditVm());
        MarkPreparationDirty();
        RefreshPreparationAmount();
        RaisePreparationCommands();
        _ = RefreshHourlyOverlapAsync();
    }

    private void RemoveHourlyScope(BillingPreparationHoursScopeEditVm? edit)
    {
        if (edit is null || !CanEditPreparation())
            return;
        edit.PropertyChanged -= OnHourlyScopePropertyChanged;
        HourlyScopeEdits.Remove(edit);
        MarkPreparationDirty();
        RefreshPreparationAmount();
        RaisePreparationCommands();
        _ = RefreshHourlyOverlapAsync();
    }

    private void OnHourlyScopePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BillingPreparationHoursScopeEditVm.Included)
            or nameof(BillingPreparationHoursScopeEditVm.MasterPlanSubContractId)
            or nameof(BillingPreparationHoursScopeEditVm.SelectedSubContractCount)
            or nameof(BillingPreparationHoursScopeEditVm.FromDate)
            or nameof(BillingPreparationHoursScopeEditVm.ToDate)
            or nameof(BillingPreparationHoursScopeEditVm.ReportCount)
            or nameof(BillingPreparationHoursScopeEditVm.TotalHours)
            or nameof(BillingPreparationHoursScopeEditVm.PricedHoursAmount))
        {
            if (e.PropertyName is nameof(BillingPreparationHoursScopeEditVm.Included)
                or nameof(BillingPreparationHoursScopeEditVm.MasterPlanSubContractId)
                or nameof(BillingPreparationHoursScopeEditVm.SelectedSubContractCount)
                or nameof(BillingPreparationHoursScopeEditVm.FromDate)
                or nameof(BillingPreparationHoursScopeEditVm.ToDate))
            {
                MarkPreparationDirty();
            }

            RefreshPreparationAmount();
            RaisePreparationCommands();
            _ = RefreshHourlyOverlapAsync();
        }
    }

    internal async Task LoadPreparationComponentsAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        var generation = ++_componentLoadGeneration;
        _suspendDirty++;
        try
        {
            var load = await _preparation.LoadComponentsAsync(SelectedPreparation.Id).ConfigureAwait(true);
            if (generation != _componentLoadGeneration || SelectedPreparation is null)
                return;
            _hourlySubContracts = load.HourlySubContracts;
            _hourReports = load.HourReports;
            _stageCatalog = load.Stages;
            RebuildStageEdits();
            BindHourlyCatalog();
            await RefreshHourlyOverlapAsync().ConfigureAwait(true);
            RefreshPreparationAmount();
            ClearDirty();
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] load preparation components failed", ex);
        }
        finally
        {
            _suspendDirty--;
        }
    }

    private async Task RefreshHourlyOverlapAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        var ids = HourlyScopeEdits
            .Where(e => e.Included)
            .SelectMany(e => e.ResolvedReports.Select(r => r.HoursReportId))
            .Distinct()
            .ToList();
        IReadOnlyList<int> hits = [];
        if (ids.Count > 0)
        {
            hits = await _preparation
                .FindHourReportIdsInOtherRequestsAsync(ids, SelectedPreparation.Id)
                .ConfigureAwait(true);
        }

        foreach (var edit in HourlyScopeEdits)
            edit.SetOverlappingIds(hits);
    }

    internal void RefreshPreparationAmount()
    {
        if (SelectedPreparation is null)
        {
            _liveAmount = new BillingLiveAmountSummary(0m, 0m, 0m, []);
            _projectFinancial = BillingProjectFinancialSummaryCalculator.Empty;
        }
        else
        {
            var stages = StageEdits
                .Where(s => s.HasPositiveAddition && !s.HasAdditionValidation)
                .Select(s => (s.ToPricingDraft(), s.RequestedDelta));
            var hours = HourlyScopeEdits.SelectMany(h => h.PricedHourItems());
            _liveAmount = BillingPreparationAmountCalculator.Summarize(stages, hours);
            _projectFinancial = BuildProjectFinancial(_liveAmount);
        }

        OnPropertyChanged(nameof(LiveAmount));
        OnPropertyChanged(nameof(PreparationAmountTitle));
        OnPropertyChanged(nameof(PreparationAmountMainText));
        OnPropertyChanged(nameof(PreparationStageAmountText));
        OnPropertyChanged(nameof(PreparationHoursAmountText));
        OnPropertyChanged(nameof(PreparationTotalCaption));
        OnPropertyChanged(nameof(PreparationTotalText));
        OnPropertyChanged(nameof(ShowPreparationAmountMissing));
        OnPropertyChanged(nameof(PreparationAmountMissingCountText));
        OnPropertyChanged(nameof(PreparationAmountMissingDetails));
        OnPropertyChanged(nameof(SelectedPreparationInstructions));
        OnPropertyChanged(nameof(ShowProjectFinancialCard));
        OnPropertyChanged(nameof(ProjectFinancial));
        OnPropertyChanged(nameof(ProjectTotalFeeText));
        OnPropertyChanged(nameof(ProjectBalanceBeforeText));
        OnPropertyChanged(nameof(ProjectAlreadyBilledText));
        OnPropertyChanged(nameof(ProjectCurrentBillText));
        OnPropertyChanged(nameof(ProjectBalanceAfterText));
        OnPropertyChanged(nameof(ProjectObservedBarShare));
        OnPropertyChanged(nameof(ProjectAdditionBarShare));
        OnPropertyChanged(nameof(ProjectRemainingBarShare));
        OnPropertyChanged(nameof(ShowProjectFinancialBar));
        OnPropertyChanged(nameof(ShowProjectFinancialBarUnavailable));
        OnPropertyChanged(nameof(ProjectFinancialBarUnavailableText));
        OnPropertyChanged(nameof(ProjectFinancialSourceText));
        OnPropertyChanged(nameof(ProjectFinancialExceedsText));
        OnPropertyChanged(nameof(ShowProjectFinancialExceeds));
        OnPropertyChanged(nameof(ProjectFinancialHourlyNote));
        OnPropertyChanged(nameof(ShowProjectFinancialHourlyNote));
        OnPropertyChanged(nameof(ProjectFinancialInCreationNote));
        OnPropertyChanged(nameof(ShowProjectFinancialInCreationNote));
        RaisePreparationCommands();
    }

    private BillingProjectFinancialSummary BuildProjectFinancial(BillingLiveAmountSummary live)
    {
        var candidate = FindSelectedProjectCandidate();
        var snapshotDate = candidate?.SnapshotDate ?? _lastResult?.Summary.MonthlySnapshotDate;
        return BillingProjectFinancialSummaryCalculator.Build(
            totalFee: candidate?.CurrentFeeSum,
            balanceBefore: candidate?.SnapshotBalance,
            snapshotDate: snapshotDate,
            feeMix: candidate?.SnapshotFeeTypes?.Mix,
            billsInCreation: candidate?.BillsInCreation ?? 0,
            currentBill: live.PricedTotal,
            currentBillIsComplete: !live.IsPartial);
    }

    private BillingCandidateRow? FindSelectedProjectCandidate()
    {
        if (SelectedPreparation is null)
            return null;

        var id = SelectedPreparation.MasterPlanProjectId;
        var fromLoaded = _allRows.FirstOrDefault(r => r.ProjectId == id);
        if (fromLoaded is not null)
            return fromLoaded.Source;

        return _lastResult?.Candidates.FirstOrDefault(c => c.ProjectId == id);
    }

    private bool CanEditPreparation() =>
        !IsBusy
        && _preparation is not null
        && SelectedPreparation is { Status: BillingPreparationStatus.WaitingForSelection
            or BillingPreparationStatus.WaitingForSnapshot
            or BillingPreparationStatus.ReadyForApproval };

    private bool CanSavePreparation() =>
        CanEditPreparation() && !StageEdits.Any(s => s.HasAdditionValidation);

    private bool CanApprovePreparation() =>
        CanSavePreparation()
        && SelectedPreparation is not null
        && (!_liveAmount.IsPartial || SelectedPreparation.Source.ManualOverride)
        && (SelectedPreparation.Source.ManualOverride
            || StageEdits.Any(s => s.HasPositiveAddition)
            || HourlyScopeEdits.Any(h => h.Included && h.SelectedSubContractCount > 0)
            || SelectedPreparation.Source.Stages.Count > 0
            || SelectedPreparation.Source.Hours.Count > 0);

    private bool CanConfirmHourly() =>
        !IsBusy && _preparation is not null && CanConfirmHourlyVisible;

    internal async Task SavePreparationAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        OperationErrorMessage = string.Empty;
        try
        {
            var invalid = StageEdits.FirstOrDefault(s => s.HasAdditionValidation);
            if (invalid is not null)
            {
                OperationErrorMessage = invalid.AdditionValidationMessage
                    ?? "יש לתקן את אחוז התוספת לפני שמירה.";
                return;
            }

            var stages = StageEdits
                .Where(s => s.HasPositiveAddition)
                .Select(s => s.ToSnapshot())
                .ToList();
            var included = HourlyScopeEdits.Where(h => h.Included && h.SelectedSubContractCount > 0).ToList();
            var reportIds = included
                .SelectMany(h => h.ResolvedReports.Select(r => r.HoursReportId))
                .ToList();
            if (reportIds.Count != reportIds.Distinct().Count())
            {
                OperationErrorMessage = "אותו דיווח שעות לא יכול להופיע פעמיים באותה בקשת הכנה.";
                return;
            }

            var overlapping = reportIds.Count == 0
                ? []
                : await _preparation
                    .FindHourReportIdsInOtherRequestsAsync(reportIds.Distinct().ToList(), SelectedPreparation.Id)
                    .ConfigureAwait(true);
            var stamp = SelectedPreparation.Source.SnapshotTimestampUtc
                        ?? _timeProvider.GetUtcNow().UtcDateTime;
            var hours = included
                .SelectMany(h => h.ToSnapshots(stamp, overlapping))
                .ToList();
            if (hours.SelectMany(h => h.Reports.Select(r => r.HoursReportId)).Distinct().Count()
                != hours.SelectMany(h => h.Reports.Select(r => r.HoursReportId)).Count())
            {
                OperationErrorMessage = "אותו דיווח שעות לא יכול להופיע פעמיים באותה בקשת הכנה.";
                return;
            }
            var updated = await _preparation
                .SaveSelectionAsync(SelectedPreparation.Id, stages, hours)
                .ConfigureAwait(true);
            ClearDirty();
            ReplacePreparation(updated);
        }
        catch (Exception ex)
        {
            OperationErrorMessage = ex.Message;
        }
    }

    private async Task ApplyManualOverrideAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        try
        {
            var updated = await _preparation
                .ApplyManualOverrideAsync(SelectedPreparation.Id, ManualOverrideReason)
                .ConfigureAwait(true);
            ReplacePreparation(updated);
        }
        catch (Exception ex)
        {
            OperationErrorMessage = ex.Message;
        }
    }

    private async Task ApprovePreparationAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        try
        {
            if (IsDirty)
                await SavePreparationAsync().ConfigureAwait(true);
            else
                OperationErrorMessage = string.Empty;
            if (!string.IsNullOrEmpty(OperationErrorMessage) || SelectedPreparation is null)
                return;
            if (_liveAmount.IsPartial && !SelectedPreparation.Source.ManualOverride)
            {
                OperationErrorMessage = "לא ניתן לאשר — יש רכיבים שנבחרו ללא בסיס תמחור. "
                    + PreparationAmountMissingDetails;
                return;
            }
            var approved = await _preparation
                .ApproveAndCreateTaskAsync(SelectedPreparation.Id)
                .ConfigureAwait(true);
            ReplacePreparation(approved.Request);
            StatusMessage = $"נפתחה משימת הכנת חשבון #{approved.TaskId}";
        }
        catch (Exception ex)
        {
            OperationErrorMessage = ex.Message;
        }
    }

    private async Task ConfirmHourlyAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        try
        {
            var updated = await _preparation
                .ConfirmHourlyManuallyAsync(SelectedPreparation.Id, HourlyConfirmNote)
                .ConfigureAwait(true);
            ReplacePreparation(updated);
        }
        catch (Exception ex)
        {
            OperationErrorMessage = ex.Message;
        }
    }

    private void ReplacePreparation(BillingPreparationRequestRecord updated)
    {
        var vm = new BillingPreparationRequestRowVm(updated);
        var existing = PreparationRows.FirstOrDefault(r => r.Id == updated.Id);
        var index = existing is null ? -1 : PreparationRows.IndexOf(existing);
        if (index >= 0)
            PreparationRows[index] = vm;
        else
            PreparationRows.Insert(0, vm);
        _suppressSelectionGuard = true;
        _suspendDirty++;
        try
        {
            SelectedPreparation = vm;
        }
        finally
        {
            _suspendDirty--;
            _suppressSelectionGuard = false;
        }

        ClearDirty();
        RaisePreparationCommands();
    }

    private void RaisePreparationCommands()
    {
        (RefreshPreparationCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SavePreparationCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ManualOverrideCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ApprovePreparationCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ConfirmHourlyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        _addHourlyScopeCommand.RaiseCanExecuteChanged();
        _removeHourlyScopeCommand.RaiseCanExecuteChanged();
    }

    private static string BuildDiagnostics(BillingDashboardResult result)
    {
        var d = result.Diagnostics;
        var lines = new[]
        {
            $"מקור מוגדר: {d.ConfiguredDataSource ?? BillingDashboardFormatters.EmDash}",
            $"מסד: {d.InitialCatalog ?? d.DatabaseName ?? BillingDashboardFormatters.EmDash}",
            $"שרת בפועל: {d.SqlServerName ?? BillingDashboardFormatters.EmDash}",
            $"מכונה: {d.SqlMachineName ?? BillingDashboardFormatters.EmDash}",
            $"סנכרון פרויקטים: {BillingDashboardFormatters.DateTimeStamp(d.ProjectsSyncTime)}",
            $"סנכרון חשבונות: {BillingDashboardFormatters.DateTimeStamp(d.BillsSyncTime)}",
            $"סנכרון שעות מורחב: {BillingDashboardFormatters.DateTimeStamp(d.ProjectHoursExtendedSyncTime)}"
        };
        var warnings = result.Warnings.Select(w => w.Message);
        return string.Join(Environment.NewLine, lines.Concat(warnings));
    }
}

public sealed record BillingDashboardStateFilterOption(
    string Label,
    BillingCandidateState? State,
    BillingLocalDecisionType? LocalDecision = null)
{
    public static BillingDashboardStateFilterOption All { get; } = new("כל המצבים", null);
    public static BillingDashboardStateFilterOption MarkedPrepareBill { get; } =
        new("סומן להכנת חשבון", null, BillingLocalDecisionType.PrepareBill);
    public static BillingDashboardStateFilterOption Held { get; } =
        new("מושהה", null, BillingLocalDecisionType.NotNow);

    public override string ToString() => Label;
}
