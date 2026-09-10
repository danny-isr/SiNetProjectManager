using SiNet.Application.Billing;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf.Billing;

/// <summary>
/// One manager-selected hourly scope row. Resolve via <see cref="BillingHourlyScopeResolver"/>.
/// </summary>
public sealed class BillingPreparationHoursScopeEditVm : ObservableObject
{
    private bool _included = true;
    private int _masterPlanSubContractId;
    private string _subContractName = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _reportCount;
    private decimal _totalHours;
    private string? _validationMessage;
    private IReadOnlyList<int> _overlappingHourReportIds = [];
    private IReadOnlyList<BillingPreparationHourReportSnapshot> _resolvedReports = [];
    private IReadOnlyList<BillingHourlySubContractDraft> _availableHourlySubContracts = [];
    private IReadOnlyList<BillingHourReportFact> _hourReports = [];
    private BillingConfirmationMode _confirmationMode;
    private DateTime? _confirmedAtUtc;
    private int? _confirmedByUserId;
    private string? _confirmationNote;

    public BillingPreparationHoursScopeEditVm()
    {
    }

    public BillingPreparationHoursScopeEditVm(BillingPreparationHoursLineSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _included = true;
        _masterPlanSubContractId = saved.MasterPlanSubContractId;
        _subContractName = saved.SubContractName;
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
    }

    public bool Included
    {
        get => _included;
        set
        {
            if (SetField(ref _included, value))
                RefreshPreview();
        }
    }

    public int MasterPlanSubContractId
    {
        get => _masterPlanSubContractId;
        set
        {
            if (!SetField(ref _masterPlanSubContractId, value))
                return;
            var match = _availableHourlySubContracts.FirstOrDefault(s => s.MasterPlanSubContractId == value);
            _subContractName = match?.Name ?? _subContractName;
            OnPropertyChanged(nameof(SubContractName));
            RefreshPreview();
        }
    }

    public string SubContractName => _subContractName;

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
    public string ReportCountText => _reportCount.ToString();
    public string TotalHoursText => _totalHours.ToString("0.##");
    public string? ValidationMessage => _validationMessage;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage);
    public IReadOnlyList<BillingPreparationHourReportSnapshot> ResolvedReports => _resolvedReports;
    public IReadOnlyList<int> OverlappingHourReportIds => _overlappingHourReportIds;

    public string OverlapWarningText =>
        _overlappingHourReportIds.Count == 0
            ? string.Empty
            : "דיווחי שעות כבר כלולים בבקשת הכנה אחרת (HoursReportID: "
              + string.Join(", ", _overlappingHourReportIds)
              + ").";

    public bool HasOverlapWarning => _overlappingHourReportIds.Count > 0;

    public string DateRangeText =>
        _fromDate is DateTime from && _toDate is DateTime to
            ? $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}"
            : string.Empty;

    public IReadOnlyList<BillingHourlySubContractDraft> AvailableHourlySubContracts =>
        _availableHourlySubContracts;

    public void BindCatalog(
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts,
        IReadOnlyList<BillingHourReportFact> hourReports)
    {
        _availableHourlySubContracts = hourlySubContracts ?? [];
        _hourReports = hourReports ?? [];
        OnPropertyChanged(nameof(AvailableHourlySubContracts));
        if (_masterPlanSubContractId <= 0 && _availableHourlySubContracts.Count == 1)
            MasterPlanSubContractId = _availableHourlySubContracts[0].MasterPlanSubContractId;
        else
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
        if (_fromDate is not DateTime from || _toDate is not DateTime to)
            throw new InvalidOperationException("יש לבחור טווח תאריכים להיקף השעות.");

        return BillingPreparationHoursScopeComposer.Compose(
            _masterPlanSubContractId,
            from,
            to,
            _availableHourlySubContracts,
            _hourReports,
            overlappingHourReportIds,
            snapshotTimestampUtc,
            _confirmationMode,
            _confirmedAtUtc,
            _confirmedByUserId,
            _confirmationNote);
    }

    private void RefreshPreview()
    {
        var preview = BillingPreparationHoursScopeComposer.Preview(
            _masterPlanSubContractId <= 0 ? null : _masterPlanSubContractId,
            _fromDate,
            _toDate,
            _availableHourlySubContracts,
            _hourReports);
        _resolvedReports = preview.Reports;
        _reportCount = preview.ReportCount;
        _totalHours = preview.TotalHours;
        _validationMessage = preview.ValidationMessage;
        OnPropertyChanged(nameof(ReportCount));
        OnPropertyChanged(nameof(ReportCountText));
        OnPropertyChanged(nameof(TotalHours));
        OnPropertyChanged(nameof(TotalHoursText));
        OnPropertyChanged(nameof(ResolvedReports));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(DateRangeText));
        SetOverlappingIds(_overlappingHourReportIds);
    }
}
