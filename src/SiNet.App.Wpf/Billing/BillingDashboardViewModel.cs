using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Billing;

namespace SiNet.App.Wpf.Billing;

/// <summary>
/// Read-only Billing Control Center. Consumes <see cref="IBillingDashboardReadService"/> only.
/// Does not resolve candidate state, query SQL, or run MasterPlan sync.
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
    private readonly AsyncRelayCommand _refreshCommand;

    private CancellationTokenSource? _loadCts;
    private IReadOnlyList<BillingDashboardRowVm> _allRows = [];
    private BillingDashboardResult? _lastResult;
    private BillingDashboardUiState _uiState = BillingDashboardUiState.Idle;
    private bool _isBusy;
    private bool _activeOnly = true;
    private bool _actionableOnly = true;
    private string _filterText = string.Empty;
    private BillingDashboardStateFilterOption _stateFilter = BillingDashboardStateFilterOption.All;
    private BillingDashboardRowVm? _selected;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private string _asOfDateText = BillingDashboardFormatters.EmDash;
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

    public BillingDashboardViewModel(
        IBillingDashboardReadService service,
        TimeProvider? timeProvider = null,
        IAppLogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        Rows = new ObservableCollection<BillingDashboardRowVm>();
        StateFilterOptions =
        [
            BillingDashboardStateFilterOption.All,
            new("לבדוק עכשיו", BillingCandidateState.ReviewNow),
            new("עבודה שהצטברה", BillingCandidateState.AccumulatedWork),
            new("חשבון בהכנה", BillingCandidateState.BillInPreparation),
            new("מכוסה בחשבון האחרון", BillingCandidateState.CoveredByLatestBill),
            new("לא דחוף", BillingCandidateState.NotUrgent)
        ];
    }

    public ObservableCollection<BillingDashboardRowVm> Rows { get; }
    public IReadOnlyList<BillingDashboardStateFilterOption> StateFilterOptions { get; }
    public ICommand RefreshCommand => _refreshCommand;
    public ICommand ClearFiltersCommand { get; }

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
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                _refreshCommand.RaiseCanExecuteChanged();
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
        }
    }

    public bool HasSelection => Selected is not null;
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
            AsOfDateText = asOf.ToString("dd/MM/yyyy");
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
        if (StateFilter.State is BillingCandidateState specific)
        {
            query = query.Where(r => r.CandidateState == specific);
        }
        else if (ActionableOnly)
        {
            query = query.Where(r => DefaultActionableStates.Contains(r.CandidateState));
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
    }

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

public sealed record BillingDashboardStateFilterOption(string Label, BillingCandidateState? State)
{
    public static BillingDashboardStateFilterOption All { get; } = new("כל המצבים", null);

    public override string ToString() => Label;
}
