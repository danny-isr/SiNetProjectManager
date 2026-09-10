using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SiNet.Application.Billing;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;

namespace SiNet.App.Wpf.Billing;

/// <summary>
/// One manager-selected hourly scope: a date range plus one or more FeeType=4 SubContracts.
/// Save flattens to one <see cref="BillingPreparationHoursLineSnapshot"/> per SubContract.
/// </summary>
public sealed class BillingPreparationHoursScopeEditVm : ObservableObject
{
    private bool _included = true;
    private bool _isSelectorOpen;
    private bool _showSelectedNames;
    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _reportCount;
    private decimal _totalHours;
    private string? _validationMessage;
    private IReadOnlyList<int> _overlappingHourReportIds = [];
    private IReadOnlyList<BillingPreparationHourReportSnapshot> _resolvedReports = [];
    private IReadOnlyList<BillingHourReportFact> _hourReports = [];
    private BillingConfirmationMode _confirmationMode;
    private DateTime? _confirmedAtUtc;
    private int? _confirmedByUserId;
    private string? _confirmationNote;
    private HashSet<int> _pendingSelectedIds = [];
    private readonly List<BillingHourlySubContractChoiceVm> _choices = [];

    public BillingPreparationHoursScopeEditVm()
    {
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        ClearAllCommand = new RelayCommand(_ => ClearAll());
    }

    public BillingPreparationHoursScopeEditVm(BillingPreparationHoursLineSnapshot saved)
        : this()
    {
        ArgumentNullException.ThrowIfNull(saved);
        _included = true;
        _fromDate = saved.FromDate.Date;
        _toDate = saved.ToDate.Date;
        _reportCount = saved.ReportCount;
        _totalHours = saved.TotalHours;
        _resolvedReports = saved.Reports;
        _overlappingHourReportIds = saved.OverlappingHourReportIds;
        _confirmationMode = saved.ConfirmationMode;
        _confirmedAtUtc = saved.ConfirmedAtUtc;
        _confirmedByUserId = saved.ConfirmedByUserId;
        _confirmationNote = saved.ConfirmationNote;
        _pendingSelectedIds = [saved.MasterPlanSubContractId];
    }

    public static BillingPreparationHoursScopeEditVm FromSavedLines(
        IReadOnlyList<BillingPreparationHoursLineSnapshot> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            return new BillingPreparationHoursScopeEditVm();

        var first = lines[0];
        var vm = new BillingPreparationHoursScopeEditVm
        {
            _included = true,
            _fromDate = first.FromDate.Date,
            _toDate = first.ToDate.Date,
            _confirmationMode = first.ConfirmationMode,
            _confirmedAtUtc = first.ConfirmedAtUtc,
            _confirmedByUserId = first.ConfirmedByUserId,
            _confirmationNote = first.ConfirmationNote,
            _pendingSelectedIds = lines.Select(l => l.MasterPlanSubContractId).ToHashSet()
        };
        vm._resolvedReports = BillingPreparationHoursScopeComposer.DistinctReports(lines.SelectMany(l => l.Reports));
        vm._reportCount = vm._resolvedReports.Count;
        vm._totalHours = vm._resolvedReports.Sum(r => r.Hours);
        vm._overlappingHourReportIds = lines.SelectMany(l => l.OverlappingHourReportIds).Distinct().ToList();
        return vm;
    }

    public ICommand SelectAllCommand { get; }
    public ICommand ClearAllCommand { get; }

    public bool Included
    {
        get => _included;
        set
        {
            if (SetField(ref _included, value))
                RefreshPreview();
        }
    }

    public bool IsSelectorOpen
    {
        get => _isSelectorOpen;
        set => SetField(ref _isSelectorOpen, value);
    }

    public bool ShowSelectedNames
    {
        get => _showSelectedNames;
        set => SetField(ref _showSelectedNames, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
                OnPropertyChanged(nameof(VisibleChoices));
        }
    }

    public int MasterPlanSubContractId
    {
        get => SelectedIds.Count == 1 ? SelectedIds[0] : 0;
        set
        {
            foreach (var choice in _choices)
                choice.IsSelected = choice.MasterPlanSubContractId == value;
            if (_choices.Count == 0 && value > 0)
                _pendingSelectedIds = [value];
            RefreshPreview();
            OnPropertyChanged(nameof(MasterPlanSubContractId));
            OnPropertyChanged(nameof(SubContractName));
        }
    }

    public string SubContractName =>
        SelectedIds.Count switch
        {
            0 => string.Empty,
            1 => _choices.FirstOrDefault(c => c.IsSelected)?.Name ?? string.Empty,
            _ => SelectedSubContractCount + " הסכמי משנה"
        };

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetField(ref _fromDate, value?.Date))
                RefreshPreview();
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetField(ref _toDate, value?.Date))
                RefreshPreview();
        }
    }

    public int ReportCount => _reportCount;
    public decimal TotalHours => _totalHours;
    public string ReportCountText => _reportCount.ToString(CultureInfo.InvariantCulture);
    public string TotalHoursText => _totalHours.ToString("0.##", CultureInfo.InvariantCulture);
    public string? ValidationMessage => _validationMessage;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage);
    public IReadOnlyList<BillingPreparationHourReportSnapshot> ResolvedReports => _resolvedReports;
    public IReadOnlyList<int> OverlappingHourReportIds => _overlappingHourReportIds;
    public IReadOnlyList<BillingHourlySubContractChoiceVm> Choices => _choices;
    public IReadOnlyList<BillingHourlySubContractChoiceVm> VisibleChoices
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_searchText))
                return _choices;
            return _choices
                .Where(c => c.Name.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public IReadOnlyList<int> SelectedIds =>
        _choices.Count == 0
            ? _pendingSelectedIds.ToList()
            : _choices.Where(c => c.IsSelected).Select(c => c.MasterPlanSubContractId).ToList();

    public int SelectedSubContractCount => SelectedIds.Count;
    public int AvailableSubContractCount => _choices.Count == 0 ? _pendingSelectedIds.Count : _choices.Count;
    public string SelectionSummaryText =>
        "נבחרו " + SelectedSubContractCount + " מתוך " + Math.Max(AvailableSubContractCount, SelectedSubContractCount)
        + " הסכמי משנה";

    public string DateRangeText =>
        _fromDate is DateTime from && _toDate is DateTime to
            ? $"{from:dd/MM/yyyy}–{to:dd/MM/yyyy}"
            : string.Empty;

    public string AggregatePreviewText
    {
        get
        {
            if (_fromDate is not DateTime from || _toDate is not DateTime to)
                return string.Empty;
            return "טווח: " + from.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                   + "–" + to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                   + Environment.NewLine
                   + "הסכמי משנה שנבחרו: " + SelectedSubContractCount
                   + Environment.NewLine
                   + "דיווחים תואמים: " + ReportCount
                   + Environment.NewLine
                   + "סה\"כ שעות: " + TotalHoursText;
        }
    }

    public string SelectedNamesText =>
        string.Join(
            Environment.NewLine,
            _choices.Where(c => c.IsSelected).Select(c => "• " + c.Name + " — "
                + c.ReportCount + " דיווחים, " + c.TotalHours.ToString("0.##") + " שעות"));

    public IReadOnlyList<BillingHourlySubContractDraft> AvailableHourlySubContracts =>
        _choices.Select(c => c.Source).ToList();

    public string OverlapWarningText
    {
        get
        {
            if (_overlappingHourReportIds.Count == 0)
                return string.Empty;
            var affected = _choices
                .Where(c => c.IsSelected && c.ReportCount > 0)
                .Where(c =>
                {
                    var preview = BillingPreparationHoursScopeComposer.Preview(
                        c.MasterPlanSubContractId, _fromDate, _toDate, AvailableHourlySubContracts, _hourReports);
                    return preview.Reports.Any(r => _overlappingHourReportIds.Contains(r.HoursReportId));
                })
                .Select(c => c.Name)
                .ToList();
            var text = "דיווחי שעות כבר כלולים בבקשת הכנה אחרת ("
                       + _overlappingHourReportIds.Count + " דיווחים).";
            if (affected.Count > 0)
                text += " הסכמי משנה: " + string.Join(", ", affected);
            return text;
        }
    }

    public bool HasOverlapWarning => _overlappingHourReportIds.Count > 0;

    public void BindCatalog(
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts,
        IReadOnlyList<BillingHourReportFact> hourReports)
    {
        _hourReports = hourReports ?? [];
        var catalog = hourlySubContracts ?? [];
        if (catalog.Count == 0)
        {
            OnPropertyChanged(nameof(AvailableHourlySubContracts));
            RefreshPreview();
            return;
        }

        var selected = SelectedIds.Count > 0 ? SelectedIds.ToHashSet() : _pendingSelectedIds;
        foreach (var existing in _choices)
            existing.SelectionChanged -= OnChoiceSelectionChanged;
        _choices.Clear();
        foreach (var draft in catalog)
        {
            var choice = new BillingHourlySubContractChoiceVm(draft)
            {
                IsSelected = selected.Contains(draft.MasterPlanSubContractId)
            };
            choice.SelectionChanged += OnChoiceSelectionChanged;
            _choices.Add(choice);
        }

        if (selected.Count == 0 && _choices.Count == 1 && _pendingSelectedIds.Count == 0)
            _choices[0].IsSelected = true;

        _pendingSelectedIds = [];
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(VisibleChoices));
        OnPropertyChanged(nameof(AvailableHourlySubContracts));
        RefreshPreview();
    }

    private bool _suppressSelectionRefresh;

    public void SelectAll()
    {
        _suppressSelectionRefresh = true;
        try
        {
            foreach (var choice in _choices)
                choice.IsSelected = true;
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }

        RefreshPreview();
    }

    public void ClearAll()
    {
        _suppressSelectionRefresh = true;
        try
        {
            foreach (var choice in _choices)
                choice.IsSelected = false;
            _pendingSelectedIds.Clear();
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }

        RefreshPreview();
    }

    public void SetOverlappingIds(IReadOnlyList<int> overlappingHourReportIds)
    {
        var hits = overlappingHourReportIds is null
            ? []
            : overlappingHourReportIds
                .Where(id => _resolvedReports.Any(r => r.HoursReportId == id))
                .Distinct()
                .ToList();
        _overlappingHourReportIds = hits;
        OnPropertyChanged(nameof(OverlappingHourReportIds));
        OnPropertyChanged(nameof(OverlapWarningText));
        OnPropertyChanged(nameof(HasOverlapWarning));
    }

    public BillingPreparationHoursLineSnapshot ToSnapshot(
        DateTime snapshotTimestampUtc,
        IReadOnlyList<int> overlappingHourReportIds)
    {
        var lines = ToSnapshots(snapshotTimestampUtc, overlappingHourReportIds);
        if (lines.Count == 0)
            throw new InvalidOperationException("יש לבחור הסכם משנה שעתי מה-snapshot שנטען.");
        return lines[0];
    }

    public IReadOnlyList<BillingPreparationHoursLineSnapshot> ToSnapshots(
        DateTime snapshotTimestampUtc,
        IReadOnlyList<int> overlappingHourReportIds)
    {
        if (_fromDate is not DateTime from || _toDate is not DateTime to)
            throw new InvalidOperationException("יש לבחור טווח תאריכים להיקף השעות.");

        var ids = SelectedIds;
        if (ids.Count == 0)
            return [];

        return BillingPreparationHoursScopeComposer.ComposeMany(
            ids,
            from,
            to,
            AvailableHourlySubContracts,
            _hourReports,
            overlappingHourReportIds,
            snapshotTimestampUtc,
            _confirmationMode,
            _confirmedAtUtc,
            _confirmedByUserId,
            _confirmationNote);
    }

    private void OnChoiceSelectionChanged(object? sender, EventArgs e)
    {
        if (!_suppressSelectionRefresh)
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        var preview = BillingPreparationHoursScopeComposer.PreviewMany(
            SelectedIds,
            _fromDate,
            _toDate,
            AvailableHourlySubContracts,
            _hourReports);
        _resolvedReports = preview.Reports;
        _reportCount = preview.ReportCount;
        _totalHours = preview.TotalHours;
        _validationMessage = preview.ValidationMessage;
        foreach (var choice in _choices)
        {
            var per = preview.PerSubContract.FirstOrDefault(p => p.MasterPlanSubContractId == choice.MasterPlanSubContractId);
            choice.ReportCount = per.MasterPlanSubContractId == 0 ? 0 : per.ReportCount;
            choice.TotalHours = per.MasterPlanSubContractId == 0 ? 0m : per.TotalHours;
        }

        OnPropertyChanged(nameof(ReportCount));
        OnPropertyChanged(nameof(ReportCountText));
        OnPropertyChanged(nameof(TotalHours));
        OnPropertyChanged(nameof(TotalHoursText));
        OnPropertyChanged(nameof(ResolvedReports));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(DateRangeText));
        OnPropertyChanged(nameof(SelectedSubContractCount));
        OnPropertyChanged(nameof(AvailableSubContractCount));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(AggregatePreviewText));
        OnPropertyChanged(nameof(SelectedNamesText));
        OnPropertyChanged(nameof(SubContractName));
        OnPropertyChanged(nameof(MasterPlanSubContractId));
        SetOverlappingIds(_overlappingHourReportIds);
    }
}
