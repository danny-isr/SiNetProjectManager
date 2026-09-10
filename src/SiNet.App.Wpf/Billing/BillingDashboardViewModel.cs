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
    private int _componentLoadGeneration;

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
        _notNowCommand = new AsyncRelayCommand(NotNowAsync, CanWriteNewDecision);
        _clearDecisionCommand = new AsyncRelayCommand(ClearDecisionAsync, CanClearDecision);
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        RefreshPreparationCommand = new AsyncRelayCommand(RefreshPreparationAsync, () => !IsBusy && _preparation is not null);
        SavePreparationCommand = new AsyncRelayCommand(SavePreparationAsync, CanEditPreparation);
        ManualOverrideCommand = new AsyncRelayCommand(ApplyManualOverrideAsync, CanEditPreparation);
        ApprovePreparationCommand = new AsyncRelayCommand(ApprovePreparationAsync, CanApprovePreparation);
        ConfirmHourlyCommand = new AsyncRelayCommand(ConfirmHourlyAsync, CanConfirmHourly);
        _addHourlyScopeCommand = new RelayCommand(_ => AddHourlyScope(), _ => CanEditPreparation());
        _removeHourlyScopeCommand = new RelayCommand(
            p => RemoveHourlyScope(p as BillingPreparationHoursScopeEditVm),
            p => CanEditPreparation() && p is BillingPreparationHoursScopeEditVm);
        Rows = new ObservableCollection<BillingDashboardRowVm>();
        PreparationRows = new ObservableCollection<BillingPreparationRequestRowVm>();
        StageEdits = new ObservableCollection<BillingPreparationStageEditVm>();
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
    public ObservableCollection<BillingPreparationHoursScopeEditVm> HourlyScopeEdits { get; }
    public IReadOnlyList<BillingDashboardStateFilterOption> StateFilterOptions { get; }
    public ICommand RefreshCommand => _refreshCommand;
    public ICommand ClearFiltersCommand { get; }
    public ICommand PrepareBillCommand => _prepareBillCommand;
    public ICommand NotNowCommand => _notNowCommand;
    public ICommand ClearDecisionCommand => _clearDecisionCommand;
    public ICommand RefreshPreparationCommand { get; }
    public ICommand SavePreparationCommand { get; }
    public ICommand ManualOverrideCommand { get; }
    public ICommand ApprovePreparationCommand { get; }
    public ICommand ConfirmHourlyCommand { get; }
    public ICommand AddHourlyScopeCommand => _addHourlyScopeCommand;
    public ICommand RemoveHourlyScopeCommand => _removeHourlyScopeCommand;
    public string HourlyScopeCaption => BillingPreparationHoursScopeComposer.ManagerSelectedScopeCaption;

    public int SelectedWorkspaceTab
    {
        get => _selectedWorkspaceTab;
        set
        {
            if (SetField(ref _selectedWorkspaceTab, value) && value == 1)
                _ = RefreshPreparationAsync();
        }
    }

    public BillingPreparationRequestRowVm? SelectedPreparation
    {
        get => _selectedPreparation;
        set
        {
            if (SetField(ref _selectedPreparation, value))
            {
                ReloadStageEdits();
                ReloadHourlyScopeEdits();
                OnPropertyChanged(nameof(HasSelectedPreparation));
                OnPropertyChanged(nameof(SelectedPreparationStatusText));
                OnPropertyChanged(nameof(SelectedPreparationInstructions));
                OnPropertyChanged(nameof(ShowWaitingForSnapshot));
                OnPropertyChanged(nameof(CanConfirmHourlyVisible));
                RaisePreparationCommands();
                if (value is not null)
                    _ = LoadPreparationComponentsAsync();
            }
        }
    }

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
            : BillingPreparationTaskInstructions.Build(SelectedPreparation.Source);

    public string ManualOverrideReason
    {
        get => _manualOverrideReason;
        set => SetField(ref _manualOverrideReason, value);
    }

    public string HourlyConfirmNote
    {
        get => _hourlyConfirmNote;
        set => SetField(ref _hourlyConfirmNote, value);
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
            OnPropertyChanged(nameof(ShowCandidatesArea));
            OnPropertyChanged(nameof(ShowOperationalChrome));
            OnPropertyChanged(nameof(ShowHeaderStatusMessage));
            OnPropertyChanged(nameof(ShowEmptyFilterState));
            OnPropertyChanged(nameof(ShowEmptyServiceState));
            OnPropertyChanged(nameof(EmptyListMessage));
            OnPropertyChanged(nameof(ShowDecisionWriteButtons));
            OnPropertyChanged(nameof(ShowClearDecisionButton));
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
    public bool ShowCandidatesArea =>
        UiState == BillingDashboardUiState.Loaded && _lastResult is { CandidatesBlocked: false };
    public bool ShowOperationalChrome => ShowCandidatesArea;
    public bool ShowHeaderStatusMessage =>
        !ShowBlockedPanel && !ShowErrorBanner && !string.IsNullOrWhiteSpace(StatusMessage);
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
        private set => SetField(ref _errorMessage, value);
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

        if (_preparation is null)
            return;

        try
        {
            await _preparation.EnsureFromPrepareBillAsync(
                    row.ProjectId,
                    row.ProjectNumber,
                    row.ProjectName,
                    row.CustomerName)
                .ConfigureAwait(true);
            SelectedWorkspaceTab = 1;
            await RefreshPreparationAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] ensure preparation request failed", ex);
            ErrorMessage = ex.Message;
        }
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
            ErrorMessage = "חובה לציין סיבה להשהיה.";
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
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] clear local decision failed", ex);
            ErrorMessage = ex.Message;
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
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] save local decision failed", ex);
            ErrorMessage = ex.Message;
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

    private void RaiseDecisionCommands()
    {
        _prepareBillCommand.RaiseCanExecuteChanged();
        _notNowCommand.RaiseCanExecuteChanged();
        _clearDecisionCommand.RaiseCanExecuteChanged();
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

        var selectedId = SelectedPreparation?.Id;
        var rows = await _preparation.ListForPreparationTabAsync().ConfigureAwait(true);
        PreparationRows.Clear();
        foreach (var row in rows)
            PreparationRows.Add(new BillingPreparationRequestRowVm(row));
        SelectedPreparation = selectedId is int id
            ? PreparationRows.FirstOrDefault(r => r.Id == id)
            : PreparationRows.Count > 0 ? PreparationRows[0] : null;
        if (SelectedPreparation is not null)
            await LoadPreparationComponentsAsync().ConfigureAwait(true);
        RaisePreparationCommands();
    }

    private void ReloadStageEdits()
    {
        StageEdits.Clear();
        if (SelectedPreparation is null)
            return;
        foreach (var stage in SelectedPreparation.Source.Stages)
            StageEdits.Add(new BillingPreparationStageEditVm(stage));
    }

    private void ReloadHourlyScopeEdits()
    {
        foreach (var existing in HourlyScopeEdits)
            existing.PropertyChanged -= OnHourlyScopePropertyChanged;
        HourlyScopeEdits.Clear();
        if (SelectedPreparation is null)
            return;
        foreach (var hours in SelectedPreparation.Source.Hours)
            AttachHourlyScope(new BillingPreparationHoursScopeEditVm(hours));
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
        RaisePreparationCommands();
        _ = RefreshHourlyOverlapAsync();
    }

    private void RemoveHourlyScope(BillingPreparationHoursScopeEditVm? edit)
    {
        if (edit is null || !CanEditPreparation())
            return;
        edit.PropertyChanged -= OnHourlyScopePropertyChanged;
        HourlyScopeEdits.Remove(edit);
        RaisePreparationCommands();
        _ = RefreshHourlyOverlapAsync();
    }

    private void OnHourlyScopePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BillingPreparationHoursScopeEditVm.Included)
            or nameof(BillingPreparationHoursScopeEditVm.MasterPlanSubContractId)
            or nameof(BillingPreparationHoursScopeEditVm.FromDate)
            or nameof(BillingPreparationHoursScopeEditVm.ToDate)
            or nameof(BillingPreparationHoursScopeEditVm.ReportCount))
        {
            RaisePreparationCommands();
            _ = RefreshHourlyOverlapAsync();
        }
    }

    internal async Task LoadPreparationComponentsAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        var generation = ++_componentLoadGeneration;
        try
        {
            var load = await _preparation.LoadComponentsAsync(SelectedPreparation.Id).ConfigureAwait(true);
            if (generation != _componentLoadGeneration || SelectedPreparation is null)
                return;
            _hourlySubContracts = load.HourlySubContracts;
            _hourReports = load.HourReports;
            BindHourlyCatalog();
            if (SelectedPreparation.Source.Stages.Count == 0 && StageEdits.Count == 0)
            {
                var stamp = load.LatestBackupUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
                foreach (var draft in load.Stages)
                {
                    var observed = draft.Observed.Value;
                    var snapshot = new BillingPreparationStageLineSnapshot(
                        draft.MasterPlanStageId,
                        draft.MasterPlanSubContractId,
                        draft.StageName,
                        draft.SubContractName,
                        draft.StageWeightWithinSubContract,
                        observed,
                        observed ?? 0m,
                        0m,
                        draft.Observed.HasOutliers,
                        stamp,
                        BillingConfirmationMode.None,
                        null,
                        null,
                        null);
                    StageEdits.Add(new BillingPreparationStageEditVm(snapshot));
                }
            }

            await RefreshHourlyOverlapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Error("[Billing] load preparation components failed", ex);
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

    private bool CanEditPreparation() =>
        !IsBusy
        && _preparation is not null
        && SelectedPreparation is { Status: BillingPreparationStatus.WaitingForSelection
            or BillingPreparationStatus.WaitingForSnapshot
            or BillingPreparationStatus.ReadyForApproval };

    private bool CanApprovePreparation() =>
        CanEditPreparation()
        && SelectedPreparation is not null
        && (SelectedPreparation.Source.ManualOverride
            || SelectedPreparation.Source.Stages.Count > 0
            || SelectedPreparation.Source.Hours.Count > 0
            || StageEdits.Count > 0
            || HourlyScopeEdits.Any(h => h.Included));

    private bool CanConfirmHourly() =>
        !IsBusy && _preparation is not null && CanConfirmHourlyVisible;

    internal async Task SavePreparationAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        ErrorMessage = string.Empty;
        try
        {
            var stages = StageEdits.Select(s => s.ToSnapshot()).ToList();
            var included = HourlyScopeEdits.Where(h => h.Included).ToList();
            var reportIds = included
                .SelectMany(h => h.ResolvedReports.Select(r => r.HoursReportId))
                .Distinct()
                .ToList();
            var overlapping = reportIds.Count == 0
                ? []
                : await _preparation
                    .FindHourReportIdsInOtherRequestsAsync(reportIds, SelectedPreparation.Id)
                    .ConfigureAwait(true);
            var stamp = SelectedPreparation.Source.SnapshotTimestampUtc
                        ?? _timeProvider.GetUtcNow().UtcDateTime;
            var hours = included.Select(h => h.ToSnapshot(stamp, overlapping)).ToList();
            var updated = await _preparation
                .SaveSelectionAsync(SelectedPreparation.Id, stages, hours)
                .ConfigureAwait(true);
            ReplacePreparation(updated);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
            ErrorMessage = ex.Message;
        }
    }

    private async Task ApprovePreparationAsync()
    {
        if (_preparation is null || SelectedPreparation is null)
            return;
        try
        {
            await SavePreparationAsync().ConfigureAwait(true);
            if (!string.IsNullOrEmpty(ErrorMessage) || SelectedPreparation is null)
                return;
            var approved = await _preparation
                .ApproveAndCreateTaskAsync(SelectedPreparation.Id)
                .ConfigureAwait(true);
            ReplacePreparation(approved.Request);
            StatusMessage = $"נפתחה משימת הכנת חשבון #{approved.TaskId}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
            ErrorMessage = ex.Message;
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
        SelectedPreparation = vm;
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
